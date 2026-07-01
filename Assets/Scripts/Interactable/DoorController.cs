using System.Collections;
using Unity.Netcode;
using UnityEngine;

// 서버 권한 기믹 패턴(문). 표준 설명은 LeverGimmick.cs 참고.
//
// 동작 정리
//  • 직접 상호작용 문(canDirectInteract=true)  : 플레이어 E키 → 서버가 열림 상태 토글 → 모두 동기화.
//  • 스위치 구동 문(canDirectInteract=false)    : 레버/버튼의 동기화된 이벤트가 OpenDoor/CloseDoor를 호출.
//      - 이 문에 NetworkObject가 없으면, 동기화된 스위치 이벤트가 각 클라이언트에서 동시에 OpenDoor를
//        부르므로 NetworkObject 없이도 화면이 일치합니다(이 게임은 중간 난입이 없어 충분).
//      - 이 문에 NetworkObject가 있으면 서버만 상태를 바꾸고 나머지는 NetworkVariable로 따라옵니다.
//  • 열쇠 문(needsKey=true)                      : 잠긴 동안엔 열쇠를 든 플레이어만 열 수 있습니다.
[RequireComponent(typeof(NetworkObject))]
public class DoorController : NetworkBehaviour, IInteractable
{
    public enum DoorType { Slide, Rotate }

    [Header("문 설정")]
    public DoorType doorType = DoorType.Slide;
    public float moveSpeed = 3f;
    [Tooltip("Slide는 X축, Rotate는 Y축 변경")]
    public Vector3 openOffset;

    [Header("상호작용 설정")]
    [Tooltip("체크하면 플레이어가 다가가서 직접 E키로 열고 닫을 수 있습니다.\n체크 해제하면 버튼이나 레버 같은 외부 스위치로만 열립니다.")]
    public bool canDirectInteract = false;

    [Header("열쇠 잠금 설정")]
    [Tooltip("체크하면 문을 열기 위해 특정 열쇠 오브젝트가 필요합니다.")]
    public bool needsKey = false;
    [Tooltip("열쇠로 인식할 오브젝트의 태그입니다.")]
    public string keyTag = "Key";

    [Tooltip("잠긴 문을 열려고 시도했을 때 재생할 사운드입니다(선택). 비워두면 무시합니다.")]
    public AudioClip lockedSound;

    [Header("자동 반복 타이머 (Auto Timing Trap)")]
    [Tooltip("체크하면 설정한 시간에 맞춰 자동으로 열리고 닫히기를 무한 반복합니다.")]
    public bool isAutoLoop = false;
    public float openDuration = 3f;
    public float closeDuration = 2f;

    private bool isOpen = false; // 현재 문이 열려있는지(동기화 상태의 로컬 캐시)

    private Vector3 closedPosition;
    private Vector3 openPosition;
    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Coroutine moveCoroutine;
    private Coroutine autoLoopCoroutine;

    private readonly NetworkVariable<bool> networkIsOpen =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;

    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        // 위치 캐싱은 스폰 시 스냅보다 먼저 끝나야 하므로 Awake에서 처리합니다.
        closedPosition = transform.localPosition;
        closedRotation = transform.localRotation;

        if (doorType == DoorType.Slide)
            openPosition = closedPosition + openOffset;
        else
            openRotation = closedRotation * Quaternion.Euler(openOffset);
    }

    private void Start()
    {
        // NetworkObject가 아예 없는 순수 로컬 문(에디터 테스트 등)에서만 여기서 자동 루프를 돌립니다.
        // NetworkObject가 있는 문은 OnNetworkSpawn에서 서버만 루프를 돌립니다.
        if (cachedNetworkObject == null && isAutoLoop)
        {
            StartAutoLoop();
        }
    }

    public override void OnNetworkSpawn()
    {
        networkIsOpen.OnValueChanged += HandleOpenChanged;

        // 늦게 들어온 클라이언트도 현재 상태로 즉시 맞춥니다.
        isOpen = networkIsOpen.Value;
        ApplyDoorInstant(isOpen);

        if (IsServer && isAutoLoop)
        {
            StartAutoLoop();
        }
    }

    public override void OnNetworkDespawn()
    {
        networkIsOpen.OnValueChanged -= HandleOpenChanged;
        StopAutoLoop();
    }

    // ⭐ 플레이어가 문을 향해 E키를 눌렀을 때 실행되는 함수
    public void RequestInteract(GameObject interactor)
    {
        if (!IsNetworkActive)
        {
            RequestInteractLocal(interactor);
            return;
        }

        // 🔑 열쇠가 필요하고 아직 닫혀(=잠겨) 있는 상태
        if (needsKey && !networkIsOpen.Value)
        {
            // 잡고 있는 열쇠는 상호작용한 클라이언트(소유자)만 알 수 있으므로 그쪽에서 검사/소모합니다.
            PlayerInteractor playerInteractor = interactor != null ? interactor.GetComponent<PlayerInteractor>() : null;
            GrabbableObject heldObj = playerInteractor != null ? playerInteractor.CurrentHeldGrabbable : null;

            if (heldObj != null && heldObj.CompareTag(keyTag))
            {
                Debug.Log($"🔑 [{gameObject.name}] {interactor.name}이(가) 열쇠({keyTag})를 사용했습니다!");

                NetworkObject keyNetworkObject = heldObj.GetComponent<NetworkObject>();
                if (keyNetworkObject != null && keyNetworkObject.IsSpawned)
                {
                    // 서버가 열쇠 보유를 검증한 뒤 '열쇠 소모 + 문 열기'를 원자적으로 처리합니다.
                    // (클라이언트의 주장만 믿지 않고, 서버 권한 홀더 목록으로 실제 보유를 확인)
                    ulong keyId = keyNetworkObject.NetworkObjectId;
                    if (IsServer) TryUnlockWithKeyOnServer(keyId, NetworkManager.LocalClientId);
                    else RequestUnlockWithKeyServerRpc(keyId);

                    // 소유자 로컬의 잡기 효과(운반 속도/아웃라인 등)만 정리합니다.
                    // 실제 Despawn은 서버가 수행하고 모든 클라이언트에 복제됩니다.
                    playerInteractor.NotifyHeldObjectConsumedLocally();
                }
                else
                {
                    // 비네트워크 열쇠(구 프리팹/단독 테스트) 폴백: 기존 로컬 소모 경로.
                    playerInteractor.ConsumeHeldObject();
                    if (IsServer) SetOpenOnServer(true);
                    else RequestSetOpenServerRpc(true);
                }
            }
            else
            {
                Debug.Log($"🔒 [{gameObject.name}] 문이 잠겨있습니다! 열쇠({keyTag})가 필요합니다.");
                PlayLockedFeedback();
            }
            return;
        }

        // 직접 열 수 없는 문이라면 거절
        if (!canDirectInteract)
        {
            Debug.Log($"🔒 [{gameObject.name}] 문은 다른 장치(스위치)로 열어야 합니다!");
            PlayLockedFeedback();
            return;
        }

        // 직접 열 수 있다면 토글 요청
        if (IsServer) ToggleOnServer();
        else RequestToggleServerRpc();
    }

    // 외부 스위치(버튼, 레버)나 자동 루프에서 호출하는 함수
    public void OpenDoor()
    {
        if (!IsNetworkActive)
        {
            SetDoorLocal(true);
            return;
        }

        // 네트워크 문은 서버만 상태를 바꿉니다. 클라이언트 호출은 무시(서버 상태가 내려옴).
        if (IsServer && !networkIsOpen.Value)
        {
            networkIsOpen.Value = true;
        }
    }

    public void CloseDoor()
    {
        if (!IsNetworkActive)
        {
            SetDoorLocal(false);
            return;
        }

        if (IsServer && networkIsOpen.Value)
        {
            networkIsOpen.Value = false;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleServerRpc(ServerRpcParams rpcParams = default)
    {
        ToggleOnServer();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSetOpenServerRpc(bool open, ServerRpcParams rpcParams = default)
    {
        SetOpenOnServer(open);
    }

    // 클라이언트가 "이 열쇠로 문을 연다"고 요청 → 서버가 보유를 검증하고 처리합니다.
    [ServerRpc(RequireOwnership = false)]
    private void RequestUnlockWithKeyServerRpc(ulong keyNetworkObjectId, ServerRpcParams rpcParams = default)
    {
        TryUnlockWithKeyOnServer(keyNetworkObjectId, rpcParams.Receive.SenderClientId);
    }

    // 서버 전용: 요청자가 실제로 열쇠를 들고 있는지 확인한 뒤, 열쇠 소모와 문 열기를 한 번에 처리합니다.
    private void TryUnlockWithKeyOnServer(ulong keyNetworkObjectId, ulong requesterClientId)
    {
        if (!IsServer || networkIsOpen.Value) return;

        NetworkManager networkManager = NetworkManager;
        if (networkManager == null || networkManager.SpawnManager == null) return;
        if (!networkManager.SpawnManager.SpawnedObjects.TryGetValue(keyNetworkObjectId, out NetworkObject keyNetworkObject)
            || keyNetworkObject == null)
        {
            return;
        }

        GrabbableObject keyGrabbable = keyNetworkObject.GetComponent<GrabbableObject>();
        if (keyGrabbable == null || !keyGrabbable.CompareTag(keyTag)) return;

        // 요청자가 정말 이 열쇠를 들고 있는지 서버 권한 홀더 목록으로 검증합니다.
        if (!keyGrabbable.IsHeldByClientOnServer(requesterClientId)) return;

        keyGrabbable.ServerConsume();
        networkIsOpen.Value = true;
    }

    private void PlayLockedFeedback()
    {
        if (lockedSound != null)
        {
            AudioSource.PlayClipAtPoint(lockedSound, transform.position);
        }
    }

    private void ToggleOnServer()
    {
        networkIsOpen.Value = !networkIsOpen.Value;
    }

    private void SetOpenOnServer(bool open)
    {
        if (networkIsOpen.Value != open)
        {
            networkIsOpen.Value = open;
        }
    }

    private void HandleOpenChanged(bool previousValue, bool newValue)
    {
        isOpen = newValue;
        AnimateDoor(newValue);
    }

    // ───── 로컬(비네트워크) 폴백 ─────
    private void RequestInteractLocal(GameObject interactor)
    {
        if (needsKey && !isOpen)
        {
            PlayerInteractor playerInteractor = interactor != null ? interactor.GetComponent<PlayerInteractor>() : null;
            GrabbableObject heldObj = playerInteractor != null ? playerInteractor.CurrentHeldGrabbable : null;
            if (heldObj != null && heldObj.CompareTag(keyTag))
            {
                playerInteractor.ConsumeHeldObject();
                SetDoorLocal(true);
            }
            else
            {
                Debug.Log($"🔒 [{gameObject.name}] 문이 잠겨있습니다! 열쇠({keyTag})가 필요합니다.");
                PlayLockedFeedback();
            }
            return;
        }

        if (!canDirectInteract)
        {
            Debug.Log($"🔒 [{gameObject.name}] 문은 다른 장치(스위치)로 열어야 합니다!");
            PlayLockedFeedback();
            return;
        }

        SetDoorLocal(!isOpen);
    }

    private void SetDoorLocal(bool open)
    {
        if (isOpen == open) return;
        isOpen = open;
        AnimateDoor(open);
    }

    // ───── 연출(모든 클라이언트 공통) ─────
    private void AnimateDoor(bool open)
    {
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);

        if (doorType == DoorType.Slide)
            moveCoroutine = StartCoroutine(MoveRoutine(open ? openPosition : closedPosition));
        else
            moveCoroutine = StartCoroutine(RotateRoutine(open ? openRotation : closedRotation));
    }

    private void ApplyDoorInstant(bool open)
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        if (doorType == DoorType.Slide)
            transform.localPosition = open ? openPosition : closedPosition;
        else
            transform.localRotation = open ? openRotation : closedRotation;
    }

    private IEnumerator MoveRoutine(Vector3 targetPos)
    {
        while (Vector3.Distance(transform.localPosition, targetPos) > 0.001f)
        {
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, targetPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.localPosition = targetPos;
    }

    private IEnumerator RotateRoutine(Quaternion targetRot)
    {
        while (Quaternion.Angle(transform.localRotation, targetRot) > 0.1f)
        {
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.localRotation = targetRot;
    }

    // ───── 자동 반복(권위 인스턴스에서만) ─────
    private void StartAutoLoop()
    {
        if (autoLoopCoroutine != null) return;
        autoLoopCoroutine = StartCoroutine(AutoLoopRoutine());
    }

    private void StopAutoLoop()
    {
        if (autoLoopCoroutine != null)
        {
            StopCoroutine(autoLoopCoroutine);
            autoLoopCoroutine = null;
        }
    }

    private IEnumerator AutoLoopRoutine()
    {
        while (true)
        {
            OpenDoor();
            yield return new WaitForSeconds(openDuration);

            CloseDoor();
            yield return new WaitForSeconds(closeDuration);
        }
    }
}
