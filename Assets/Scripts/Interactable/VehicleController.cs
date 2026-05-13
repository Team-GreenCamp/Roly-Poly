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

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        if (exitAction != null) exitAction.action.Enable();
    }

    private void OnDisable()
    {
        if (exitAction != null) exitAction.action.Disable();
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
        if (driver == null) return;

        // 탑승자가 내리기 버튼을 눌렀을 때
        if (exitAction != null && exitAction.action.WasPressedThisFrame())
        {
            driver.Unmount();
            driver = null;
            return;
        }

        // 플레이어의 이동 입력을 가로채서 차량 조작에 사용
        if (moveAction != null)
        {
            Vector2 input = moveAction.action.ReadValue<Vector2>();
            
            if (rb != null)
            {
                Vector3 moveDir = transform.forward * input.y * moveSpeed;
                Vector3 newPos = rb.position + moveDir * Time.deltaTime;
                rb.MovePosition(newPos);

                Quaternion turn = Quaternion.Euler(0, input.x * turnSpeed * Time.deltaTime, 0);
                rb.MoveRotation(rb.rotation * turn);
            }
            else
            {
                transform.Translate(Vector3.forward * input.y * moveSpeed * Time.deltaTime);
                transform.Rotate(Vector3.up, input.x * turnSpeed * Time.deltaTime);
            }
        }
    }
}
