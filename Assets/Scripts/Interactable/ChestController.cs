using System.Collections;
using Unity.Netcode;
using UnityEngine;

// 서버 권한 기믹 패턴(상자, 1회성 열기). 표준 설명은 LeverGimmick.cs 참고.
// 열림 상태를 서버가 확정하고, 모든 클라이언트가 뚜껑 회전/열쇠 공개를 동일하게 실행합니다.
[RequireComponent(typeof(NetworkObject))]
public class ChestController : NetworkBehaviour, IInteractable
{
    [Header("상자 연출 설정")]
    [Tooltip("상자의 뚜껑(Lid) 오브젝트를 연결해 주세요.")]
    public Transform lidTransform;

    [Tooltip("상자가 열릴 때 뚜껑이 회전할 로컬 각도 오프셋입니다.")]
    public Vector3 openRotationOffset = new Vector3(-80f, 0f, 0f);

    [Tooltip("뚜껑이 열리는 속도입니다.")]
    public float openSpeed = 2f;

    [Header("열쇠 설정")]
    [Tooltip("상자 내부에 숨겨둘 열쇠 오브젝트(GrabbableObject 등)를 연결해 주세요.")]
    public GameObject keyObject;

    [Header("상호작용 설정")]
    [Tooltip("체크하면 플레이어가 직접 다가가 E키로 상자를 열 수 있습니다.")]
    public bool canDirectInteract = true;

    private bool isOpened = false; // 동기화 상태의 로컬 캐시
    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Coroutine openCoroutine;

    private readonly NetworkVariable<bool> networkOpened =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;
    private Coroutine keyFreezeRoutine;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        if (lidTransform != null)
        {
            closedRotation = lidTransform.localRotation;
            openRotation = closedRotation * Quaternion.Euler(openRotationOffset);
        }
        else
        {
            Debug.LogWarning($"🔒 [{gameObject.name}] 상자의 Lid Transform이 할당되지 않았습니다!");
        }

        // 시작 시 상자 안의 열쇠는 숨깁니다.
        // 주의: keyObject.SetActive(false)로 GameObject를 끄면, 열쇠가 씬에 배치된 NetworkObject일 때
        // NGO가 이를 스폰하지 않습니다(비활성 in-scene NetworkObject는 스폰 제외). 그러면 열쇠가 로컬 모드로만
        // 동작해 클라이언트마다 위치/소모가 어긋납니다. 따라서 오브젝트는 살려두고 렌더러만 끕니다.
        if (keyObject != null)
        {
            SetKeyRenderersVisible(false);
        }
        else
        {
            Debug.LogWarning($"🔒 [{gameObject.name}] 상자 내부의 Key Object가 할당되지 않았습니다!");
        }
    }

    public override void OnNetworkSpawn()
    {
        networkOpened.OnValueChanged += HandleOpenedChanged;

        // 이미 열린 상자라면(늦은 합류 등) 즉시 열린 모습으로 스냅합니다.
        if (networkOpened.Value)
        {
            isOpened = true;
            RevealContents();
            if (lidTransform != null) lidTransform.localRotation = openRotation;
        }
        else if (IsServer)
        {
            // 열쇠(중력 On·동적)가 열리기 전에 떨어지지 않도록 권한 측에서 고정한다.
            // 열쇠 NetworkObject의 NetworkRigidbody가 스폰 시 authority를 동적으로 되돌리므로 스폰 이후에 고정한다.
            if (keyFreezeRoutine != null) StopCoroutine(keyFreezeRoutine);
            keyFreezeRoutine = StartCoroutine(FreezeHiddenKeyRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        networkOpened.OnValueChanged -= HandleOpenedChanged;

        if (keyFreezeRoutine != null)
        {
            StopCoroutine(keyFreezeRoutine);
            keyFreezeRoutine = null;
        }
    }

    // 열쇠 NetworkObject가 스폰되어 NetworkRigidbody가 authority 상태를 잡은 뒤 kinematic으로 고정한다.
    private IEnumerator FreezeHiddenKeyRoutine()
    {
        NetworkObject keyNetworkObject = keyObject != null ? keyObject.GetComponent<NetworkObject>() : null;

        float timeout = 2f;
        while (keyNetworkObject != null && !keyNetworkObject.IsSpawned && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        yield return null; // NetworkRigidbody가 authority를 동적으로 만드는 프레임 이후로 미룬다.

        if (!networkOpened.Value && keyObject != null && keyObject.TryGetComponent(out Rigidbody keyRb))
        {
            keyRb.linearVelocity = Vector3.zero;
            keyRb.angularVelocity = Vector3.zero;
            keyRb.isKinematic = true;
        }

        keyFreezeRoutine = null;
    }

    // GameObject를 끄지 않고 렌더러만 껐다 켜서 열쇠(NetworkObject)의 스폰 상태를 유지한다.
    private void SetKeyRenderersVisible(bool visible)
    {
        if (keyObject == null) return;

        Renderer[] renderers = keyObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = visible;
        }
    }

    public void RequestInteract(GameObject interactor)
    {
        if (!canDirectInteract) return;

        if (!IsNetworkActive)
        {
            if (!isOpened) OpenChestLocal(interactor);
            return;
        }

        if (networkOpened.Value) return; // 이미 열림

        if (IsServer) networkOpened.Value = true;
        else RequestOpenServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!networkOpened.Value) networkOpened.Value = true;
    }

    private void HandleOpenedChanged(bool previousValue, bool newValue)
    {
        if (newValue && !isOpened)
        {
            isOpened = true;
            PlayOpen();
        }
    }

    // ───── 로컬(비네트워크) 폴백 ─────
    private void OpenChestLocal(GameObject interactor)
    {
        if (isOpened) return;
        isOpened = true;
        Debug.Log($"📦 {(interactor != null ? interactor.name : "Local")}이(가) [{gameObject.name}] 상자를 열었습니다!");
        PlayOpen();
    }

    // ───── 연출(모든 클라이언트 공통) ─────
    private void PlayOpen()
    {
        // 뚜껑이 플레이어를 밀쳐 추락시키는 물리 버그 방지: 뚜껑 콜라이더를 트리거로 변환.
        if (lidTransform != null)
        {
            Collider[] lidColliders = lidTransform.GetComponentsInChildren<Collider>(true);
            foreach (var col in lidColliders)
            {
                if (col != null) col.isTrigger = true;
            }
        }

        RevealContents();

        if (lidTransform != null)
        {
            if (openCoroutine != null) StopCoroutine(openCoroutine);
            openCoroutine = StartCoroutine(OpenLidRoutine());
        }
    }

    private void RevealContents()
    {
        if (keyObject == null) return;

        // 열기 전 고정 코루틴이 남아 있으면 중단(이제 열림 상태로 넘어가므로).
        if (keyFreezeRoutine != null)
        {
            StopCoroutine(keyFreezeRoutine);
            keyFreezeRoutine = null;
        }

        // 표시는 networkOpened 동기화로 모든 클라이언트가 동시에 실행합니다(렌더러만 켬).
        SetKeyRenderersVisible(true);

        // 물리 상태 변경은 권한 측에서만 합니다.
        //  • 열쇠가 NetworkObject(서버 권한 Rigidbody)면 서버만 isKinematic을 만지고 나머지는 동기화로 따라옵니다.
        //    (클라이언트가 임의로 isKinematic을 바꾸면 서버 권한 물리와 충돌합니다.)
        //  • 비네트워크 열쇠면 기존처럼 각 클라이언트가 로컬로 처리합니다.
        Rigidbody keyRb = keyObject.GetComponent<Rigidbody>();
        if (keyRb != null)
        {
            bool keyIsNetworked = keyObject.TryGetComponent(out NetworkObject keyNetworkObject) && keyNetworkObject.IsSpawned;
            if (!keyIsNetworked || IsServer)
            {
                keyRb.isKinematic = true;
            }
        }
    }

    private IEnumerator OpenLidRoutine()
    {
        while (Quaternion.Angle(lidTransform.localRotation, openRotation) > 0.1f)
        {
            lidTransform.localRotation = Quaternion.Slerp(lidTransform.localRotation, openRotation, openSpeed * Time.deltaTime);
            yield return null;
        }
        lidTransform.localRotation = openRotation;
    }
}
