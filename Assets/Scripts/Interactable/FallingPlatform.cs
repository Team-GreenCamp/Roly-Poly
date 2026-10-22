using System.Collections;
using Unity.Netcode;
using UnityEngine;

// 7번: 밟으면 떨어지는 발판 (서버 권한).
//
// 이 게임의 플레이어는 소유자만 dynamic, 원격 프록시는 kinematic이라 "서버에서" 충돌이 잡히지 않습니다.
// 따라서 밟은 플레이어의 소유 클라이언트가 서버에 낙하를 요청하고, 서버는 "언제 떨어지기 시작하는지"(dropTime)만 동기화합니다.
// 실제 낙하 위치는 모든 머신이 서버 시각으로 같은 공식(등가속 낙하 + 살짝 기울기)을 계산해 kinematic으로 움직이므로
// 발판마다 NetworkTransform을 둘 필요가 없습니다(수백 장이 매 틱 위치를 보내던 부하 제거). 늦게 들어와도 같은 위치가 나옵니다.
// (NetworkObject가 스폰되지 않았으면 각 머신이 로컬 시계로 처리합니다.)
[RequireComponent(typeof(NetworkObject))]
public partial class FallingPlatform : NetworkBehaviour
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
    [Tooltip("밟힌 발판의 불투명도. 낮을수록 바닥이 더 비쳐 낙하 예정 상태가 잘 보입니다.")]
    [Range(0.1f, 1f)] public float steppedAlpha = 0.55f;
    [Tooltip("원래 불투명 상태에서 목표 불투명도까지 서서히 변하는 시간(초). 기본값은 낙하 유예 시간과 같다.")]
    public float steppedTransparencyFadeSeconds = 0.6f;
    [Tooltip("체크하면 낙하 시작 시 기존 VFX도 재생합니다. 기본은 색 변화만(발판이 많은 맵에서 화면이 정신없는 것 방지).")]
    public bool playFallVfx = false;

    [Header("낙하")]
    [Tooltip("낙하 가속도(m/s²). 모든 머신이 이 값으로 같은 위치를 계산한다.")]
    public float fallGravity = 9.81f;
    [Tooltip("이만큼 떨어지면 충돌을 끈다(m). 떨어지는 발판이 아래층 플레이어를 밀어내지 않게 한다.")]
    public float colliderOffDropDistance = 1.5f;
    [Tooltip("떨어지며 기우는 속도 범위(도/초). 발판마다 위치로 정해져 모든 머신에서 같다.")]
    public Vector2 tumbleDegreesPerSecond = new Vector2(20f, 55f);

    private readonly NetworkVariable<double> networkDropTime = new NetworkVariable<double>(0);
    private double localDropTime;
    private bool fallActive;
    private bool atRestPose = true;
    private Collider fallCollider;
    private Vector3 tumbleAxis = Vector3.right;
    private float tumbleSpeed;
    private double FallClock => IsNetworkActive ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    private double DropTime => IsNetworkActive ? networkDropTime.Value : localDropTime;

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
    private Material[][] originalSharedMaterials;
    private Material[][] transparentMaterials;
    private float transparencyWeight; // 0=원래 불투명, 1=목표 불투명도
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");   // 빌트인/기타 셰이더 폴백

    // 낙하 중이거나 이미 떨어진 발판인지(코인 스폰 등 외부에서 온전한 발판만 고를 때 사용).
    // 밟는 즉시 true가 되며, 실제 물리 낙하(fallDelay 이후)보다 앞선다.
    public bool IsFalling => IsNetworkActive ? networkFalling.Value : localFalling;

    // 실제로 낙하가 시작됐는지(fallDelay 유예 이후).
    // 밟은 뒤 유예 동안에는 코인을 조기 회수하지 않도록 IsFalling과 구분한다.
    public bool HasPhysicallyDropped => IsFalling && DropTime > 0 && FallClock >= DropTime;

    public override void OnNetworkSpawn()
    {
        networkFalling.OnValueChanged += HandleFallingChanged;
        // 스폰 시점에 이미 낙하 상태면(늦은 동기화 등) 틴트와 낙하 위치를 맞춰 둔다.
        SetSteppedTint(networkFalling.Value);
        RefreshFallActive();
    }

    public override void OnNetworkDespawn()
    {
        networkFalling.OnValueChanged -= HandleFallingChanged;
        RestoreOriginalMaterials();

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
        RefreshFallActive();

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

        // 두 연출 모두 보간이 없거나 비활성(코루틴 불가) 상태면 즉시 적용.
        if ((steppedTintFadeSeconds <= 0f && steppedTransparencyFadeSeconds <= 0f) || !isActiveAndEnabled)
        {
            tintWeight = targetWeight;
            transparencyWeight = targetWeight;
            ApplyTintWeight();
            return;
        }

        tintRoutine = StartCoroutine(TintFadeRoutine(targetWeight));
    }

    private IEnumerator TintFadeRoutine(float targetWeight)
    {
        float tintSpeed = steppedTintFadeSeconds <= 0f
            ? float.PositiveInfinity
            : 1f / steppedTintFadeSeconds;
        float transparencySpeed = steppedTransparencyFadeSeconds <= 0f
            ? float.PositiveInfinity
            : 1f / steppedTransparencyFadeSeconds;

        while (!Mathf.Approximately(tintWeight, targetWeight)
            || !Mathf.Approximately(transparencyWeight, targetWeight))
        {
            tintWeight = Mathf.MoveTowards(tintWeight, targetWeight, tintSpeed * Time.deltaTime);
            transparencyWeight = Mathf.MoveTowards(
                transparencyWeight, targetWeight, transparencySpeed * Time.deltaTime);
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

        if (tintWeight > 0f || transparencyWeight > 0f)
        {
            EnsureTransparentMaterials();
        }

        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Renderer tintRenderer = tintRenderers[i];
            if (tintRenderer == null)
            {
                continue;
            }

            if (tintWeight <= 0f && transparencyWeight <= 0f && regenerationOpacity >= 1f)
            {
                tintRenderer.SetPropertyBlock(null); // 블록 제거 → 머티리얼 원래 색 복원
                continue;
            }

            Color tinted = Color.Lerp(tintOriginalColors[i], steppedTintColor, tintWeight);
            // 색 경고와 별개로 원래 불투명 상태에서 천천히 반투명 상태로 전환한다.
            tinted.a = Mathf.Lerp(tintOriginalColors[i].a, steppedAlpha, transparencyWeight);
            tinted.a *= regenerationOpacity;
            tintBlock.Clear();
            tintBlock.SetColor(BaseColorId, tinted);
            tintBlock.SetColor(LegacyColorId, tinted);
            tintRenderer.SetPropertyBlock(tintBlock);
        }

        if (tintWeight <= 0f && transparencyWeight <= 0f && regenerationOpacity >= 1f)
        {
            RestoreOriginalMaterials();
        }
    }

    // URP Lit의 불투명 머티리얼은 색의 알파만 낮춰도 투명해지지 않으므로,
    // 밟힌 발판에만 머티리얼을 복제해 투명 렌더 상태로 전환한다.
    private void EnsureTransparentMaterials()
    {
        if (transparentMaterials != null)
        {
            return;
        }

        // 화면을 그리지 않는 데디케이티드 서버에서는 시각용 머티리얼을 만들지 않는다.
        if (NetworkManager != null && NetworkManager.IsServer && !NetworkManager.IsClient)
        {
            return;
        }

        originalSharedMaterials = new Material[tintRenderers.Length][];
        transparentMaterials = new Material[tintRenderers.Length][];

        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Renderer tintRenderer = tintRenderers[i];
            if (tintRenderer == null)
            {
                continue;
            }

            Material[] originals = tintRenderer.sharedMaterials;
            Material[] instances = new Material[originals.Length];
            originalSharedMaterials[i] = originals;
            transparentMaterials[i] = instances;

            for (int j = 0; j < originals.Length; j++)
            {
                if (originals[j] == null)
                {
                    continue;
                }

                Material instance = new Material(originals[j]);
                ConfigureTransparentMaterial(instance);
                instances[j] = instance;
            }

            tintRenderer.sharedMaterials = instances;
        }
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void RestoreOriginalMaterials()
    {
        if (transparentMaterials == null)
        {
            return;
        }

        for (int i = 0; i < tintRenderers.Length; i++)
        {
            if (tintRenderers[i] != null && originalSharedMaterials?[i] != null)
            {
                tintRenderers[i].sharedMaterials = originalSharedMaterials[i];
            }

            Material[] instances = transparentMaterials[i];
            if (instances == null)
            {
                continue;
            }

            for (int j = 0; j < instances.Length; j++)
            {
                if (instances[j] != null)
                {
                    Destroy(instances[j]);
                }
            }
        }

        originalSharedMaterials = null;
        transparentMaterials = null;
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
        rb.interpolation = RigidbodyInterpolation.None;
        fallCollider = GetComponent<Collider>();

        // 기우는 방향·속도는 발판 위치로 정해 모든 머신에서 같게 한다.
        float h = Mathf.Abs(Mathf.Sin(initialPosition.x * 12.9898f + initialPosition.z * 78.233f) * 43758.5453f) % 1f;
        float angle = h * Mathf.PI * 2f;
        tumbleAxis = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        tumbleSpeed = Mathf.Lerp(tumbleDegreesPerSecond.x, tumbleDegreesPerSecond.y, (h * 7.31f) % 1f);
    }

    public override void OnDestroy()
    {
        RestoreOriginalMaterials();
        base.OnDestroy();
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
        if (respawnTweenSeconds > 0f && SurvivalGameManager.Instance != null
            && SurvivalGameManager.Instance.State != SurvivalGameManager.MatchState.Playing) return;
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
        networkDropTime.Value = FallClock + fallDelay;
        networkFalling.Value = true;
        StartCoroutine(ServerFallRoutine());
    }

    // 서버는 시각만 관리한다. 낙하 위치는 모든 머신이 Update에서 dropTime으로 계산한다.
    private IEnumerator ServerFallRoutine()
    {
        double drop = networkDropTime.Value;
        if (respawnDelay > 0f)
        {
            // 재생성 발판: 떨어졌다가 제자리로 돌아와(페이드가 있으면 그만큼 더 기다린 뒤) 원래 상태로.
            double end = drop + respawnDelay + Mathf.Max(0f, respawnTweenSeconds);
            while (FallClock < end) yield return null;
            networkDropTime.Value = 0;
            networkFalling.Value = false;
            yield break;
        }

        // 복귀하지 않는 발판: 잠시 후 서버가 네트워크에서 내려 전 클라이언트에서 사라진다.
        while (FallClock < drop + Mathf.Max(0.5f, despawnAfterFallSeconds)) yield return null;
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

    // ───── 오프라인(비네트워크) 폴백: 같은 흐름을 로컬 시계로 ─────
    private IEnumerator FallRoutineLocal()
    {
        localFalling = true;
        localDropTime = FallClock + fallDelay;
        SetSteppedTint(true);
        RefreshFallActive();
        if (playFallVfx)
        {
            GameFeedback.PlatformFallAt(transform.position + Vector3.up * 0.3f);
        }

        if (respawnDelay > 0f)
        {
            double end = localDropTime + respawnDelay + Mathf.Max(0f, respawnTweenSeconds);
            while (FallClock < end) yield return null;
            localDropTime = 0;
            localFalling = false;
            SetSteppedTint(false);
            RefreshFallActive();
            yield break;
        }

        // 복귀하지 않는 발판(오프라인): 잠시 후 비활성화해 무한 낙하를 막는다.
        while (FallClock < localDropTime + Mathf.Max(0.5f, despawnAfterFallSeconds)) yield return null;
        RestoreOriginalMaterials();
        gameObject.SetActive(false);
    }

    // 낙하 중인 발판만 매 프레임 계산한다(가만히 있는 수백 장은 여기서 바로 빠진다).
    private void RefreshFallActive()
    {
        bool active = IsFalling;
        if (active == fallActive)
        {
            return;
        }

        fallActive = active;
        if (!active)
        {
            SetFallPose(initialPosition, initialRotation);
            if (fallCollider != null) fallCollider.enabled = true;
        }
    }

    // 낙하 위치 계산(모든 머신, Update에서 한 번). 가만히 있는 발판은 Update 첫 줄에서 바로 빠진다.
    // 충돌이 켜진 동안은 kinematic 이동(위에 선 플레이어가 함께 내려감), 충돌을 끈 뒤에는 위치만 옮겨 물리 부하를 줄인다.
    private void UpdateFallPose()
    {
        double drop = DropTime;
        float elapsed = drop > 0 ? (float)(FallClock - drop) : -1f;
        bool returning = respawnDelay > 0f && elapsed >= respawnDelay;
        if (elapsed <= 0f || returning)
        {
            // 떨어지기 전(경고 중)이거나 제자리로 돌아온 뒤: 원래 자리.
            if (!atRestPose) SetFallPose(initialPosition, initialRotation);
            SetFallCollider(!returning || respawnTweenSeconds <= 0f);
            return;
        }

        // 등가속 낙하 + 발판마다 정해진 방향으로 살짝 기울기. 충분히 떨어지면 충돌을 끈다.
        float distance = 0.5f * fallGravity * elapsed * elapsed;
        Quaternion tilt = Quaternion.AngleAxis(tumbleSpeed * elapsed, tumbleAxis);
        bool colliding = distance < colliderOffDropDistance;
        SetFallCollider(colliding);
        SetFallPose(initialPosition + Vector3.down * distance, tilt * initialRotation, move: colliding);
    }

    private void SetFallCollider(bool on)
    {
        if (fallCollider != null && fallCollider.enabled != on) fallCollider.enabled = on;
    }

    private void SetFallPose(Vector3 position, Quaternion rotation, bool move = false)
    {
        atRestPose = position == initialPosition && rotation == initialRotation;
        if (move && rb != null)
        {
            // kinematic 이동이라 위에 서 있던 플레이어도 자연스럽게 함께 내려간다.
            rb.MovePosition(position);
            rb.MoveRotation(rotation);
            return;
        }

        // 충돌이 꺼졌거나 제자리로 돌릴 때는 Transform만 옮긴다(물리 쪽은 다음 시뮬레이션에서 따라온다).
        transform.SetPositionAndRotation(position, rotation);
    }
}
