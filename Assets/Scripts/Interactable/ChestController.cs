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
        if (keyObject != null)
        {
            keyObject.SetActive(false);
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
    }

    public override void OnNetworkDespawn()
    {
        networkOpened.OnValueChanged -= HandleOpenedChanged;
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

        // 표시(활성화)는 networkOpened 동기화로 모든 클라이언트가 동시에 실행합니다.
        keyObject.SetActive(true);

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
