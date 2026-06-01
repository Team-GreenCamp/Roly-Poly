using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleController : MonoBehaviour, IInteractable
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

    private Vector2 currentInput;

    private void Awake()
    {
        // 좌석 배열 초기화 (최소 1자리 보장)
        if (seatPoints != null && seatPoints.Length > 0)
            passengers = new PlayerMount[seatPoints.Length];
        else
            passengers = new PlayerMount[1];

        // 1. 자기 자신에게 Rigidbody가 있는지 먼저 확인
        rb = GetComponent<Rigidbody>();
        // 2. 자신에게 없다면 부모(루트)를 확인
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        // 3. 그래도 없다면 자식에 있는지 확인
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

    public void RequestInteract(GameObject interactor)
    {
        PlayerMount mount = interactor.GetComponent<PlayerMount>();
        if (mount == null) return;

        // 이미 어떤 좌석에 타고 있는지 확인
        for (int i = 0; i < passengers.Length; i++)
        {
            if (passengers[i] == mount) return;
        }

        int closestSeatIndex = -1;
        float minDistance = float.MaxValue;

        // 가장 가까운 빈 좌석 찾기
        for (int i = 0; i < passengers.Length; i++)
        {
            if (passengers[i] != null) continue; // 이미 누가 타고 있음

            Transform targetSeat = (seatPoints != null && i < seatPoints.Length && seatPoints[i] != null) ? seatPoints[i] : transform;
            float dist = Vector3.Distance(interactor.transform.position, targetSeat.position);

            if (dist < minDistance)
            {
                minDistance = dist;
                closestSeatIndex = i;
            }
        }

        // 빈 좌석이 있으면 탑승
        if (closestSeatIndex != -1)
        {
            passengers[closestSeatIndex] = mount;
            Transform seatToUse = (seatPoints != null && closestSeatIndex < seatPoints.Length && seatPoints[closestSeatIndex] != null) ? seatPoints[closestSeatIndex] : transform;
            mount.Mount(this, seatToUse);
        }
    }

    private void Update()
    {
        // 탑승자가 내리기 버튼을 눌렀을 때 (자기가 탄 자리에서 내림)
        if (exitAction != null && exitAction.action.WasPressedThisFrame())
        {
            for (int i = 0; i < passengers.Length; i++)
            {
                if (passengers[i] != null)
                {
                    passengers[i].Unmount();
                    if (!passengers[i].isMounted)
                    {
                        passengers[i] = null; // 실제로 내렸다면 자리 비우기
                    }
                }
            }
        }

        // 운전자(0번 좌석)가 없으면 조작 불가
        if (passengers == null || passengers.Length == 0 || passengers[0] == null)
        {
            currentInput = Vector2.zero;
            return;
        }

        // 1. 플레이어의 이동 입력을 가로채서 차량 조작에 사용
        Vector2 input = Vector2.zero;
        if (moveAction != null && moveAction.action != null)
        {
            input = moveAction.action.ReadValue<Vector2>();
        }

        // 2. 입력값이 0이라면(세팅이 꺼져있거나 안 먹히는 경우), 하드코딩된 키보드 입력을 시도합니다.
        if (input.sqrMagnitude < 0.001f && Keyboard.current != null)
        {
            float x = 0; float y = 0;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1;
            input = new Vector2(x, y);
        }

        currentInput = input;

        // 앞바퀴 시각적 회전 (A/D 키 입력에 따라 바퀴가 돌아가는 연출)
        if (frontLeftWheel != null)
        {
            Vector3 localRot = frontLeftWheel.localEulerAngles;
            frontLeftWheel.localEulerAngles = new Vector3(localRot.x, currentInput.x * maxSteerAngle, localRot.z);
        }
        if (frontRightWheel != null)
        {
            Vector3 localRot = frontRightWheel.localEulerAngles;
            frontRightWheel.localEulerAngles = new Vector3(localRot.x, currentInput.x * maxSteerAngle, localRot.z);
        }

        // Rigidbody가 아예 없는 경우에만 Update에서 단순 이동 처리
        if (rb == null)
        {
            // 부모가 아닌 스크립트가 붙은 'Vehicle' 자신을 이동시킵니다.
            transform.Translate(Vector3.forward * currentInput.y * moveSpeed * Time.deltaTime, Space.Self);
            
            // 실제 자동차처럼 전후진(W/S) 중에만 회전(A/D) 하도록 변경
            if (Mathf.Abs(currentInput.y) > 0.01f)
            {
                float direction = Mathf.Sign(currentInput.y);
                transform.Rotate(Vector3.up, currentInput.x * turnSpeed * direction * Time.deltaTime, Space.Self);
            }
        }
    }

    private void FixedUpdate()
    {
        // 운전자(0번 좌석)가 없으면 물리 조작 안함
        if (passengers == null || passengers.Length == 0 || passengers[0] == null) return;

        // 게임 중에 Rigidbody가 늦게 추가되었을 경우를 대비해 다시 찾기
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        // 물리 엔진이 차량을 정지(Sleep) 상태로 판정했다면 억지로 깨워줍니다.
        if (!rb.isKinematic && rb.IsSleeping())
        {
            rb.WakeUp();
        }

        // 차량 방향은 실제 차량 스크립트가 붙은 방향(transform) 기준
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = transform.forward;

        Vector3 targetVel = flatForward * currentInput.y * moveSpeed;

        // 1. 이동: Kinematic 여부에 따라 적절한 물리 함수 사용
        if (rb.isKinematic)
        {
            // Kinematic인 경우 MovePosition으로 직접 좌표 이동 (마찰 무시)
            Vector3 nextPos = rb.position + targetVel * Time.fixedDeltaTime;
            rb.MovePosition(nextPos);
        }
        else
        {
            // Dynamic인 경우 속도(Velocity)를 직접 제어 (마찰력 극복)
            Vector3 currentVel = rb.linearVelocity;
            rb.linearVelocity = new Vector3(targetVel.x, currentVel.y, targetVel.z);
        }

        // 2. 회전: 실제 자동차처럼 전진/후진(W/S) 중일 때만 좌우(A/D) 회전이 적용됩니다.
        if (Mathf.Abs(currentInput.x) > 0.01f && Mathf.Abs(currentInput.y) > 0.01f)
        {
            // 후진(S) 중일 때는 핸들 꺾는 방향이 반대로 적용되어야 자연스럽습니다.
            float direction = Mathf.Sign(currentInput.y);
            Quaternion turn = Quaternion.Euler(0, currentInput.x * turnSpeed * direction * Time.fixedDeltaTime, 0);
            rb.MoveRotation(rb.rotation * turn);
        }
        else if (!rb.isKinematic)
        {
            // 전/후진을 안 할 때는 회전 속도를 죽여서 미끄러지는 현상 방지
            rb.angularVelocity = Vector3.zero;
        }
    }
}
