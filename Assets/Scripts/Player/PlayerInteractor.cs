// 파일 경로: Assets/Scripts/Player/PlayerInteractor.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [Header("상호작용 설정")]
    public float interactRange = 3f;
    public float sphereCastRadius = 1.5f;

    [Tooltip("레이저가 무시할 대상(Player)은 체크 해제하고, 부딪힐 대상만 체크하세요.")]
    public LayerMask interactLayerMask = ~0; // 기본값: Everything (모든 레이어와 충돌)

    [Header("시점 설정")]
    public Camera viewCamera;

    public InputActionReference interactAction;

    private IInteractable currentTarget;

    private void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.action.Enable();
            interactAction.action.performed += OnInteractPerformed;
        }
    }

    private void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.action.performed -= OnInteractPerformed;
            interactAction.action.Disable();
        }
    }

    private void Update()
    {
        CheckForInteractable();
    }

    private void CheckForInteractable()
    {
        if (viewCamera == null) return;

        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Debug.DrawRay(ray.origin, ray.direction * 20f, Color.red);

        // ⭐ 여기에 interactLayerMask를 추가하여 특정 레이어를 무시하도록 설정합니다.
        if (Physics.SphereCast(ray, sphereCastRadius, out RaycastHit hit, 20f, interactLayerMask))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();

            if (interactable != null)
            {
                float distanceToPlayer = Vector3.Distance(transform.position, hit.collider.transform.position);

                if (distanceToPlayer <= interactRange)
                {
                    currentTarget = interactable;
                    Debug.DrawLine(ray.origin, hit.collider.transform.position, Color.green);
                    return;
                }
            }
        }

        currentTarget = null;
    }

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        if (currentTarget != null)
        {
            currentTarget.RequestInteract(gameObject);
        }
    }

    private void OnDrawGizmos()
    {
        if (viewCamera == null) return;

        Gizmos.color = new Color(1, 1, 0, 0.3f);
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        // ⭐ 기즈모를 그릴 때도 동일한 레이어 마스크를 적용하여 캐릭터를 통과하게 만듭니다.
        if (Physics.SphereCast(ray, sphereCastRadius, out RaycastHit hit, 20f, interactLayerMask))
        {
            Gizmos.DrawSphere(ray.origin + ray.direction * hit.distance, sphereCastRadius);
        }
        else
        {
            Gizmos.DrawSphere(ray.origin + ray.direction * 20f, sphereCastRadius);
        }
    }
}