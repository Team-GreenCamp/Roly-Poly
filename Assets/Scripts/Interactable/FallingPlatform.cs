using System.Collections;
using Unity.Netcode;
using UnityEngine;

// 7번: 밟으면 떨어지는 발판 (서버 권한).
//
// 동기화하려면 이 오브젝트에 NetworkObject + NetworkTransform(Authority: Server)이 필요합니다.
// 이 게임의 플레이어는 소유자만 dynamic, 원격 프록시는 kinematic이라 "서버에서" 충돌이 잡히지 않습니다.
// 따라서 밟은 플레이어의 소유 클라이언트가 서버에 낙하를 요청하고, 서버만 물리를 굴려 NetworkTransform으로
// 위치를 모두에게 전파합니다. (NetworkObject가 없으면 기존처럼 각 클라이언트가 로컬로 처리합니다.)
[RequireComponent(typeof(NetworkObject))]
public class FallingPlatform : NetworkBehaviour
{
    [Header("설정")]
    [Tooltip("플레이어가 밟고 나서 떨어질 때까지의 대기 시간 (초)")]
    public float fallDelay = 1f;

    [Tooltip("떨어지고 나서 다시 원래 위치로 복귀하는 시간 (초). 0이면 복귀하지 않고 아래 시간 후 제거됩니다.")]
    public float respawnDelay = 3f;

    [Tooltip("복귀하지 않는 발판(respawnDelay=0)이 낙하 후 제거될 때까지의 시간(초). 영원히 떨어지며 동기화 트래픽을 만드는 것을 막습니다.")]
    public float despawnAfterFallSeconds = 4f;

    [Tooltip("체크 해제하면 밟아도 떨어지지 않고, ServerForceFall() 호출로만 무너집니다(서든데스용 바닥 타일).")]
    public bool triggerByStepping = true;

    [Header("밟힘 표시")]
    [Tooltip("밟혀서 낙하가 예약된 발판을 이 색으로 물들입니다(낙하 유예 동안의 경고). 복귀하면 원래 색으로 돌아옵니다.")]
    public Color steppedTintColor = Color.white;
    [Tooltip("밟힘 색으로 물드는 보간 시간(초). 0이면 즉시 변합니다. 복귀 시에도 같은 속도로 되돌아옵니다.")]
    public float steppedTintFadeSeconds = 0.15f;
    [Tooltip("체크하면 낙하 시작 시 기존 VFX도 재생합니다. 기본은 색 변화만(발판이 많은 맵에서 화면이 정신없는 것 방지).")]
    public bool playFallVfx = false;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Rigidbody rb;
    private bool localFalling = false; // 오프라인 폴백용

    private readonly NetworkVariable<bool> networkFalling =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    // 밟힘 틴트용. 발판 수백 장이 머티리얼을 공유하므로 material 복제 대신 PropertyBlock을 쓴다.
    private Renderer[] tintRenderers;
    private Color[] tintOriginalColors; // 렌더러별 원래 색(보간 시작점). Awake에서 공유 머티리얼로부터 캐시.
    private MaterialPropertyBlock tintBlock;
    private Coroutine tintRoutine;
    private float tintWeight; // 0=원래 색, 1=밟힘 색
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");   // 빌트인/기타 셰이더 폴백

    // 낙하 중이거나 이미 떨어진 발판인지(코인 스폰 등 외부에서 온전한 발판만 고를 때 사용).
    // 밟는 즉시 true가 되며, 실제 물리 낙하(fallDelay 이후)보다 앞선다.
    public bool IsFalling => IsNetworkActive ? networkFalling.Value : localFalling;

    // 실제로 물리 낙하가 시작됐는지(fallDelay 유예 이후 rb.isKinematic이 풀린 시점).
    // 서버/오프라인 로컬 판정용 — 밟은 뒤 유예 동안에는 코인을 조기 회수하지 않도록 IsFalling과 구분한다.
    public bool HasPhysicallyDropped => rb != null && !rb.isKinematic;

    public override void OnNetworkSpawn()
    {
        networkFalling.OnValueChanged += HandleFallingChanged;
        // 스폰 시점에 이미 낙하 상태면(늦은 동기화 등) 틴트를 맞춰 둔다.
        SetSteppedTint(networkFalling.Value);
    }

    public override void OnNetworkDespawn()
    {
        networkFalling.OnValueChanged -= HandleFallingChanged;

        // 복귀하지 않는 발판이 낙하 후 Despawn(false)로 네트워크에서 내려가면, 실제 숨김은 여기서
        // 전 인스턴스(서버·클라)가 처리한다. 인씬 NetworkObject를 Despawn(true)로 파괴하면 NGO가
        // 경고를 내므로(씬 재동기화 desync 소지) 파괴 대신 비활성화한다.
        // 매치 종료(씬 언로드)로 despawn되는 경우엔 곧 오브젝트가 사라지므로 무해하다.
        gameObject.SetActive(false);
    }

    private void HandleFallingChanged(bool previousValue, bool newValue)
    {
        // 밟힘 표시: 낙하 유예 동안 발판을 하얗게(steppedTintColor) 물들여 "이미 밟힌 발판"임을 알린다.
        // 복귀형 발판은 networkFalling=false로 되돌아올 때 원래 색으로 복원된다.
        SetSteppedTint(newValue);

        if (newValue && playFallVfx)
        {
            // 낙하 시작 연출(모든 클라이언트에서 동기화 변수로 1회 실행). 기본 꺼짐 — 색 변화가 주 신호.
            GameFeedback.PlatformFallAt(transform.position + Vector3.up * 0.3f);
        }
    }

    // 밟힘 틴트 적용/해제(모든 클라이언트 공통). 짧은 보간으로 물들며, PropertyBlock이라 공유 머티리얼을 건드리지 않는다.
    private void SetSteppedTint(bool stepped)
    {
        float targetWeight = stepped ? 1f : 0f;

        if (tintRoutine != null)
        {
            StopCoroutine(tintRoutine);
            tintRoutine = null;
        }

        // 보간 0초이거나 비활성(코루틴 불가) 상태면 즉시 적용.
        if (steppedTintFadeSeconds <= 0f || !isActiveAndEnabled)
        {
            tintWeight = targetWeight;
            ApplyTintWeight();
            return;
        }

        tintRoutine = StartCoroutine(TintFadeRoutine(targetWeight));
    }

    private IEnumerator TintFadeRoutine(float targetWeight)
    {
        float speed = 1f / Mathf.Max(0.01f, steppedTintFadeSeconds);
        while (!Mathf.Approximately(tintWeight, targetWeight))
        {
            tintWeight = Mathf.MoveTowards(tintWeight, targetWeight, speed * Time.deltaTime);
            ApplyTintWeight();
            yield return null;
        }

        tintRoutine = null;
    }

    private void ApplyTintWeight()
    {
        if (tintRenderers == null)
        {
            return;
        }

        if (tintBlock == null)
        {
            tintBlock = new MaterialPropertyBlock();
        }

        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Renderer tintRenderer = tintRenderers[i];
            if (tintRenderer == null)
            {
                continue;
            }

            if (tintWeight <= 0f)
            {
                tintRenderer.SetPropertyBlock(null); // 블록 제거 → 머티리얼 원래 색 복원
                continue;
            }

            Color tinted = Color.Lerp(tintOriginalColors[i], steppedTintColor, tintWeight);
            tintBlock.Clear();
            tintBlock.SetColor(BaseColorId, tinted);
            tintBlock.SetColor(LegacyColorId, tinted);
            tintRenderer.SetPropertyBlock(tintBlock);
        }
    }

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);
        tintRenderers = GetComponentsInChildren<Renderer>(true);

        // 보간 시작점이 될 원래 색을 공유 머티리얼에서 캐시(런타임 인스턴스 생성 없음).
        tintOriginalColors = new Color[tintRenderers.Length];
        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Material sharedMaterial = tintRenderers[i] != null ? tintRenderers[i].sharedMaterial : null;
            if (sharedMaterial != null && sharedMaterial.HasProperty(BaseColorId))
            {
                tintOriginalColors[i] = sharedMaterial.GetColor(BaseColorId);
            }
            else if (sharedMaterial != null && sharedMaterial.HasProperty(LegacyColorId))
            {
                tintOriginalColors[i] = sharedMaterial.GetColor(LegacyColorId);
            }
            else
            {
                tintOriginalColors[i] = Color.white;
            }
        }

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // 서든데스 등 외부(서버)에서 강제로 무너뜨릴 때 호출. 오프라인에서는 로컬로 처리.
    public void ServerForceFall()
    {
        if (!IsNetworkActive)
        {
            if (!localFalling) StartCoroutine(FallRoutineLocal());
            return;
        }

        if (IsServer) BeginFallOnServer();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!triggerByStepping) return;
        if (!collision.gameObject.CompareTag("Player")) return;
        // 위에서 밟았을 때만 떨어집니다.
        if (collision.transform.position.y <= transform.position.y) return;

        if (!IsNetworkActive)
        {
            if (!localFalling) StartCoroutine(FallRoutineLocal());
            return;
        }

        if (networkFalling.Value) return;

        // 밟은 플레이어를 소유한 클라이언트만 서버에 낙하를 요청합니다.
        PlayerController pc = collision.gameObject.GetComponentInParent<PlayerController>();
        if (pc == null || !pc.HasInputAuthority) return;

        if (IsServer) BeginFallOnServer();
        else RequestFallServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestFallServerRpc(ServerRpcParams rpcParams = default)
    {
        BeginFallOnServer();
    }

    private void BeginFallOnServer()
    {
        if (networkFalling.Value) return;
        networkFalling.Value = true;
        StartCoroutine(ServerFallRoutine());
    }

    // 서버만 물리를 굴리고, 결과 위치는 NetworkTransform이 클라이언트에 전파합니다.
    private IEnumerator ServerFallRoutine()
    {
        yield return new WaitForSeconds(fallDelay);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);

            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.position = initialPosition;
            rb.rotation = initialRotation;
            transform.SetPositionAndRotation(initialPosition, initialRotation);

            networkFalling.Value = false;
        }
        else
        {
            // 복귀하지 않는 발판: 잠시 후 서버가 네트워크에서 내려 전 클라이언트에서 사라진다.
            yield return new WaitForSeconds(Mathf.Max(0.5f, despawnAfterFallSeconds));

            if (cachedNetworkObject != null && cachedNetworkObject.IsSpawned)
            {
                // 인씬 NetworkObject라 Despawn(true) 파괴는 NGO 경고 대상 → Despawn(false)로 내리고
                // 실제 숨김은 OnNetworkDespawn(서버·클라 공통)에서 SetActive(false)로 처리한다.
                cachedNetworkObject.Despawn(false);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    // ───── 오프라인(비네트워크) 폴백: 기존 동작 ─────
    private IEnumerator FallRoutineLocal()
    {
        localFalling = true;
        SetSteppedTint(true);
        if (playFallVfx)
        {
            GameFeedback.PlatformFallAt(transform.position + Vector3.up * 0.3f);
        }
        yield return new WaitForSeconds(fallDelay);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);

            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            transform.position = initialPosition;
            transform.rotation = initialRotation;

            localFalling = false;
            SetSteppedTint(false);
        }
        else
        {
            // 복귀하지 않는 발판(오프라인): 잠시 후 비활성화해 무한 낙하를 막는다.
            yield return new WaitForSeconds(Mathf.Max(0.5f, despawnAfterFallSeconds));
            gameObject.SetActive(false);
        }
    }
}
