using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 서버 권한 차량.
//
// 동기화하려면 NetworkObject + NetworkTransform(Authority: Server) (+ 가능하면 NetworkRigidbody)이 필요합니다.
//  • 좌석 배정/점유 : 서버 권한
//  • 차량 이동      : 서버만 물리를 굴리고 NetworkTransform이 전파 (탑승자는 좌석을 따라가므로 같이 이동)
//  • 입력           : 운전자(0번 좌석 소유자)만 자기 입력을 서버로 전송
// NetworkObject가 없으면 기존처럼 로컬에서 동작합니다.
[RequireComponent(typeof(NetworkObject))]
public class VehicleController : NetworkBehaviour, IInteractable
{
    [Header("차량 설정")]
    [Tooltip("첫 번째(0번) 좌석이 무조건 운전석이 됩니다.")]
    public Transform[] seatPoints;
    public float moveSpeed = 5f;
    public float turnSpeed = 100f;

    [Header("앞바퀴 시각 효과 (선택사항)")]
    public Transform frontLeftWheel;
    public Transform frontRightWheel;
    public float maxSteerAngle = 35f;

    [Header("입력")]
    public InputActionReference moveAction;
    public InputActionReference exitAction; // 내리기 키

    private PlayerMount[] passengers;
    private Rigidbody rb;

    private Vector2 currentInput;          // 권한 측 물리에 쓰는 입력
    private Vector2 lastSentInput;         // 운전자가 마지막으로 서버에 보낸 입력

    private const ulong NoDriver = ulong.MaxValue;

    private readonly NetworkVariable<ulong> driverClientId =
        new NetworkVariable<ulong>(NoDriver, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector2> networkInput =
        new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;
    private bool HasMoveAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        if (seatPoints != null && seatPoints.Length > 0)
            passengers = new PlayerMount[seatPoints.Length];
        else
            passengers = new PlayerMount[1];

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody>();
    }

    private void OnEnable()
    {
        if (exitAction != null) exitAction.action.Enable();
        if (moveAction != null) moveAction.action.Enable();
    }

    private void OnDisable()
    {
        if (exitAction != null) exitAction.action.Disable();
        if (moveAction != null) moveAction.action.Disable();
    }

    // ───────────────────────────────────────────────────────────
    // 탑승
    // ───────────────────────────────────────────────────────────
    public void RequestInteract(GameObject interactor)
    {
        PlayerMount mount = interactor.GetComponent<PlayerMount>();
        if (mount == null) return;

        if (!IsNetworkActive)
        {
            MountLocal(mount, interactor.transform.position);
            return;
        }

        if (IsServer) AssignSeatOnServer(mount.OwnerClientId);
        else RequestMountServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMountServerRpc(ServerRpcParams rpcParams = default)
    {
        AssignSeatOnServer(rpcParams.Receive.SenderClientId);
    }

    private void AssignSeatOnServer(ulong clientId)
    {
        PlayerMount mount = ResolveMount(clientId);
        if (mount == null) return;

        // 이미 탑승 중이면 무시
        for (int i = 0; i < passengers.Length; i++)
        {
            if (passengers[i] == mount) return;
        }

        Vector3 interactorPos = mount.transform.position;
        int closestSeatIndex = FindClosestEmptySeat(interactorPos);
        if (closestSeatIndex == -1) return;

        passengers[closestSeatIndex] = mount;
        if (closestSeatIndex == 0) driverClientId.Value = clientId;

        // 좌석 배정 결과를 해당 소유자에게 알려, 소유자가 직접 탑승 처리(조작 차단/물리 정지/좌석 추종)를 수행합니다.
        ClientRpcParams target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        ConfirmMountClientRpc(closestSeatIndex, target);
    }

    [ClientRpc]
    private void ConfirmMountClientRpc(int seatIndex, ClientRpcParams rpcParams = default)
    {
        PlayerMount localMount = GetLocalPlayerMount();
        if (localMount == null) return;

        Transform seat = (seatPoints != null && seatIndex < seatPoints.Length && seatPoints[seatIndex] != null)
            ? seatPoints[seatIndex]
            : transform;
        localMount.Mount(this, seat);
    }

    private int FindClosestEmptySeat(Vector3 fromPosition)
    {
        int closestSeatIndex = -1;
        float minDistance = float.MaxValue;

        for (int i = 0; i < passengers.Length; i++)
        {
            if (passengers[i] != null) continue;

            Transform targetSeat = (seatPoints != null && i < seatPoints.Length && seatPoints[i] != null) ? seatPoints[i] : transform;
            float dist = Vector3.Distance(fromPosition, targetSeat.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestSeatIndex = i;
            }
        }

        return closestSeatIndex;
    }

    // ───── 오프라인(비네트워크) 폴백 ─────
    private void MountLocal(PlayerMount mount, Vector3 fromPosition)
    {
        for (int i = 0; i < passengers.Length; i++)
        {
            if (passengers[i] == mount) return;
        }

        int seatIndex = FindClosestEmptySeat(fromPosition);
        if (seatIndex == -1) return;

        passengers[seatIndex] = mount;
        Transform seat = (seatPoints != null && seatIndex < seatPoints.Length && seatPoints[seatIndex] != null) ? seatPoints[seatIndex] : transform;
        mount.Mount(this, seat);
    }

    // ───────────────────────────────────────────────────────────
    // 하차 + 입력
    // ───────────────────────────────────────────────────────────
    private void Update()
    {
        HandleExitInput();
        HandleDriverInput();
        UpdateWheelVisual();
    }

    private void HandleExitInput()
    {
        if (exitAction == null || !exitAction.action.WasPressedThisFrame()) return;

        if (!IsNetworkActive)
        {
            // 오프라인: 자기(로컬) 탑승자만 내림
            for (int i = 0; i < passengers.Length; i++)
            {
                if (passengers[i] != null)
                {
                    passengers[i].Unmount();
                    if (!passengers[i].isMounted) passengers[i] = null;
                }
            }
            return;
        }

        // 네트워크: 내 로컬 플레이어가 이 차량에 타고 있을 때만, 그 자리만 비웁니다.
        PlayerMount localMount = GetLocalPlayerMount();
        if (localMount == null || !localMount.IsMountedTo(this)) return;

        localMount.Unmount();
        if (!localMount.isMounted)
        {
            if (IsServer) FreeSeatOnServer(localMount.OwnerClientId);
            else RequestUnmountServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestUnmountServerRpc(ServerRpcParams rpcParams = default)
    {
        FreeSeatOnServer(rpcParams.Receive.SenderClientId);
    }

    private void FreeSeatOnServer(ulong clientId)
    {
        for (int i = 0; i < passengers.Length; i++)
        {
            if (passengers[i] != null && passengers[i].OwnerClientId == clientId)
            {
                passengers[i] = null;
                if (i == 0) driverClientId.Value = NoDriver;
            }
        }
    }

    private void HandleDriverInput()
    {
        if (!IsNetworkActive)
        {
            // 오프라인: 운전석(0번)이 차 있으면 로컬 입력 사용
            currentInput = (passengers != null && passengers.Length > 0 && passengers[0] != null && moveAction != null)
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            return;
        }

        // 네트워크: 내가 운전자일 때만 입력을 읽어 서버로 전송 (변할 때만)
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != driverClientId.Value)
        {
            return;
        }

        Vector2 input = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        if ((input - lastSentInput).sqrMagnitude > 0.0001f)
        {
            lastSentInput = input;
            SubmitInputServerRpc(input);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitInputServerRpc(Vector2 input, ServerRpcParams rpcParams = default)
    {
        // 운전자가 보낸 입력만 수용합니다.
        if (rpcParams.Receive.SenderClientId != driverClientId.Value) return;
        currentInput = input;
        networkInput.Value = input; // 클라이언트 바퀴 연출용
    }

    private void UpdateWheelVisual()
    {
        float steer = IsNetworkActive ? networkInput.Value.x : currentInput.x;

        if (frontLeftWheel != null)
        {
            Vector3 localRot = frontLeftWheel.localEulerAngles;
            frontLeftWheel.localEulerAngles = new Vector3(localRot.x, steer * maxSteerAngle, localRot.z);
        }
        if (frontRightWheel != null)
        {
            Vector3 localRot = frontRightWheel.localEulerAngles;
            frontRightWheel.localEulerAngles = new Vector3(localRot.x, steer * maxSteerAngle, localRot.z);
        }
    }

    private void FixedUpdate()
    {
        if (!HasMoveAuthority) return; // 클라이언트는 NetworkTransform이 차량을 옮김

        // 운전자(0번 좌석)가 없으면 조작 안 함
        bool hasDriver = IsNetworkActive ? driverClientId.Value != NoDriver
                                         : (passengers != null && passengers.Length > 0 && passengers[0] != null);
        if (!hasDriver) return;

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        if (!rb.isKinematic && rb.IsSleeping()) rb.WakeUp();

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = transform.forward;

        Vector3 targetVel = flatForward * currentInput.y * moveSpeed;

        if (rb.isKinematic)
        {
            rb.MovePosition(rb.position + targetVel * Time.fixedDeltaTime);
        }
        else
        {
            Vector3 currentVel = rb.linearVelocity;
            rb.linearVelocity = new Vector3(targetVel.x, currentVel.y, targetVel.z);
        }

        // 실제 자동차처럼 전/후진 중일 때만 좌우 회전
        if (Mathf.Abs(currentInput.x) > 0.01f && Mathf.Abs(currentInput.y) > 0.01f)
        {
            float direction = Mathf.Sign(currentInput.y);
            Quaternion turn = Quaternion.Euler(0, currentInput.x * turnSpeed * direction * Time.fixedDeltaTime, 0);
            rb.MoveRotation(rb.rotation * turn);
        }
        else if (!rb.isKinematic)
        {
            rb.angularVelocity = Vector3.zero;
        }
    }

    private PlayerMount ResolveMount(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
        {
            return client.PlayerObject.GetComponent<PlayerMount>();
        }
        return null;
    }

    private PlayerMount GetLocalPlayerMount()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
        {
            return nm.LocalClient.PlayerObject.GetComponent<PlayerMount>();
        }
        return null;
    }
}
