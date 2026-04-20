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

    [Header("운반(Hold) 설정")]
    [Tooltip("가벼운 물체를 들 때 위치할 빈 오브젝트를 연결하세요.")]
    public Transform holdPoint; 
    [Tooltip("무거운 물체를 끌 때 위치할 빈 오브젝트를 연결하세요.")]
    public Transform dragPoint; 
    private GrabbableObject currentHeldObject; 
    private float holdTimer = 0f;

    [Header("오뚜기 연출 설정")]
    [Tooltip("기울어질 캐릭터의 모델링(Visual) 오브젝트를 연결하세요.")]
    public Transform characterVisual; 
    public float tiltAngle = 15f; // 기울어질 각도
    public float tiltSpeed = 10f; // 기울어지는 속도

    public InputActionReference interactAction; // E키 (짧게 누르기 - 버튼/레버)
    public InputActionReference grabAction;     // R키 (꾹 누르기 - 상자 줍기)
    private IInteractable currentTarget;


    private void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.action.Enable();
            interactAction.action.started += OnInteractStarted;
        }
        if (grabAction != null) grabAction.action.Enable();
    }

    private void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.action.started -= OnInteractStarted;
            interactAction.action.Disable();
        }
        if (grabAction != null) grabAction.action.Disable();
    }

    private void Update()
    {
        CheckForInteractable();
        HandleCharacterTilt();  

        // R키(grabAction)는 타이머를 통해 짧게 누르는 것을 무시합니다.
        if (grabAction != null)
        {
            if (grabAction.action.IsPressed())
            {
                if (currentTarget is GrabbableObject && currentHeldObject == null)
                {
                    holdTimer += Time.deltaTime;
                    if (holdTimer > 0.2f)
                    {
                        currentTarget.RequestInteract(gameObject);
                    }
                }
            }
            else
            {
                holdTimer = 0f;
                if (currentHeldObject != null)
                {
                    currentHeldObject.Drop();
                    currentHeldObject = null;
                }
            }
        }
    }

    private void OnInteractStarted(InputAction.CallbackContext context)
    {
        if (currentTarget != null && currentHeldObject == null)
        {
            // E키: 대상 오브젝트에 GrabbableObject 스크립트가 있다면 완벽하게 무시합니다.
            MonoBehaviour targetMono = currentTarget as MonoBehaviour;
            if (targetMono != null && targetMono.GetComponent<GrabbableObject>() != null)
            {
                Debug.Log("🚫 [방어 작동] E키를 눌렀지만 대상이 상자(GrabbableObject)이므로 무시합니다!");
                return;
            }
            
            currentTarget.RequestInteract(gameObject);
        }
    }
    
    private void HandleCharacterTilt()
    {
        if (characterVisual == null) return;

        if (currentHeldObject != null && currentHeldObject.isHeavy)
        {
            // 1. 캐릭터가 끌고 있는 물체를 바라보게 회전
            Vector3 directionToObject = currentHeldObject.transform.position - transform.position;
            directionToObject.y = 0; 

            if (directionToObject.sqrMagnitude > 0.001f)
            {
                // 월드 기준으로 오브젝트를 바라보는 회전
                Quaternion lookRot = Quaternion.LookRotation(directionToObject);
                // 앞으로 기울이는 각도(X축) 추가
                Quaternion targetWorldRotation = lookRot * Quaternion.Euler(tiltAngle, 0, 0);

                // 부드럽게 회전 적용 (월드 기준)
                characterVisual.rotation = Quaternion.Slerp(characterVisual.rotation, targetWorldRotation, tiltSpeed * Time.deltaTime);
            }
        }
        else
        {
            // 빈 손이거나 가벼운 상자를 들고 있다면 똑바로 세움 (로컬 기준 초기화)
            characterVisual.localRotation = Quaternion.Slerp(characterVisual.localRotation, Quaternion.identity, tiltSpeed * Time.deltaTime);
        }
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

    public void Grab(GrabbableObject targetBox)
    {
        currentHeldObject = targetBox;
        targetBox.PickUp(holdPoint, dragPoint);
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