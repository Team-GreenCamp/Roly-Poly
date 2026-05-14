using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleController : MonoBehaviour, IInteractable
{
    [Header("차량 설정")]
    public Transform seatPoint;
    public float moveSpeed = 5f;
    public float turnSpeed = 100f;

    [Header("입력")]
    public InputActionReference moveAction;
    public InputActionReference exitAction; // 내리기 키

    private PlayerMount driver;
    private Rigidbody rb;

    private Vector2 currentInput;

    private void Awake()
    {
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
        if (driver != null) return; // 이미 운전자가 있음

        PlayerMount mount = interactor.GetComponent<PlayerMount>();
        if (mount != null)
        {
            driver = mount;
            driver.Mount(this, seatPoint);
        }
    }

    private void Update()
    {
        if (driver == null) 
        {
            currentInput = Vector2.zero;
            return;
        }

        // 탑승자가 내리기 버튼을 눌렀을 때
        if (exitAction != null && exitAction.action.WasPressedThisFrame())
        {
            driver.Unmount();
            
            // 쿨다운 등으로 인해 내리기가 무시되었다면 driver를 null로 풀면 안 됩니다!
            if (!driver.isMounted)
            {
                driver = null;
                currentInput = Vector2.zero;
            }
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

        // Rigidbody가 아예 없는 경우에만 Update에서 단순 이동 처리
        if (rb == null)
        {
            // 부모가 아닌 스크립트가 붙은 'Vehicle' 자신을 이동시킵니다.
            transform.Translate(Vector3.forward * currentInput.y * moveSpeed * Time.deltaTime, Space.Self);
            transform.Rotate(Vector3.up, currentInput.x * turnSpeed * Time.deltaTime, Space.Self);
        }
    }

    private void FixedUpdate()
    {
        if (driver == null) return;

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

        // 2. 회전: A/D 키를 누를 때 회전
        if (Mathf.Abs(currentInput.x) > 0.01f)
        {
            Quaternion turn = Quaternion.Euler(0, currentInput.x * turnSpeed * Time.fixedDeltaTime, 0);
            rb.MoveRotation(rb.rotation * turn);
        }
        else if (!rb.isKinematic)
        {
            // 회전 입력이 없을 때 미끄러지는 현상 방지
            rb.angularVelocity = Vector3.zero;
        }
    }
}
