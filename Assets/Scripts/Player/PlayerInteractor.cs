using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [Header("상호작용 설정")]
    public float interactRange = 3f;
    public float sphereCastRadius = 1.5f;
    [Tooltip("레이저가 무시할 대상(Player)은 체크 해제하고, 부딪힐 대상만 체크하세요.")]
    public LayerMask interactLayerMask = ~0;

    [Header("상호작용 기준 설정")]
    [Tooltip("상호작용 레이저가 시작될 높이 오프셋입니다.")]
    public float raycastHeightOffset = 1.0f;

    [Header("운반(Hold) 설정")]
    [Tooltip("가벼운 물체를 들 때 위치할 빈 오브젝트를 연결하세요.")]
    public Transform holdPoint; 
    [Tooltip("무거운 물체를 끌 때 위치할 빈 오브젝트를 연결하세요.")]
    public Transform dragPoint; 
    public float maxCarryMass = 20f;
    private float holdTimer = 0f;

    [Header("오뚜기 연출 설정")]
    [Tooltip("기울어질 캐릭터의 모델링(Visual) 오브젝트를 연결하세요.")]
    public Transform characterVisual; 
    public float tiltAngle = 15f;
    public float tiltSpeed = 10f;

    public InputActionReference interactAction; // E키
    public InputActionReference grabAction;     // R키
    
    private IInteractable currentTargetInteractable;
    private GrabbableObject currentTargetGrabbable;
    private GrabbableObject currentHeldGrabbable;

    public CapsuleCollider PlayerCollider { get; private set; }
    private PlayerController playerController;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        
        PlayerCollider = GetComponentInParent<CapsuleCollider>();
        if (PlayerCollider == null)
        {
            PlayerCollider = GetComponent<CapsuleCollider>();
        }
    }

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
        ForceDropHeldObject();
    }

    private void Update()
    {
        CheckForInteractable();
        HandleCharacterTilt();  

        // R키(grabAction)는 타이머를 통해 무거운 물체 끄는 것을 처리합니다.
        if (grabAction != null)
        {
            if (grabAction.action.IsPressed())
            {
                if (currentTargetGrabbable != null && currentTargetGrabbable.isHeavy && currentHeldGrabbable == null)
                {
                    holdTimer += Time.deltaTime;
                    if (holdTimer > 0.2f)
                    {
                        TryDragObject(currentTargetGrabbable);
                    }
                }
            }
            else
            {
                holdTimer = 0f;
                // R키를 뗐을 때 무거운 물체를 잡고 있었다면 놓기
                if (currentHeldGrabbable != null && currentHeldGrabbable.isHeavy)
                {
                    DropHeldObject();
                }
            }
        }
    }

    private void OnInteractStarted(InputAction.CallbackContext context)
    {
        // 이미 가벼운 무언가를 들고 있다면 E키로 내려놓기
        if (currentHeldGrabbable != null && !currentHeldGrabbable.isHeavy)
        {
            DropHeldObject();
            return;
        }

        // 아무것도 안들고 있고 타겟이 가벼운 물체일 때
        if (currentTargetGrabbable != null && !currentTargetGrabbable.isHeavy)
        {
            TryPickUp(currentTargetGrabbable);
            return;
        }

        // GrabbableObject가 아닌 일반 상호작용 (버튼, 레버 등)
        if (currentTargetInteractable != null && currentTargetGrabbable == null)
        {
            currentTargetInteractable.RequestInteract(gameObject);
        }
    }
    
    private void CheckForInteractable()
    {
        Vector3 origin = transform.position + Vector3.up * raycastHeightOffset;
        Ray ray = new Ray(origin, transform.forward);

        // 반경 내의 모든 물체를 감지 (부모 오브젝트 안에 있는 것도 찾기 위함)
        RaycastHit[] hits = Physics.SphereCastAll(ray, sphereCastRadius, interactRange, interactLayerMask);
        
        // 가까운 순서대로 정렬
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            // 부모에 묶인 경우를 대비해 GetComponentInParent 사용
            currentTargetInteractable = hit.collider.GetComponentInParent<IInteractable>();
            currentTargetGrabbable = hit.collider.GetComponentInParent<GrabbableObject>();

            if (currentTargetInteractable != null || currentTargetGrabbable != null)
            {
                return;
            }
        }

        currentTargetInteractable = null;
        currentTargetGrabbable = null;
    }

    private void HandleCharacterTilt()
    {
        if (characterVisual == null) return;

        if (currentHeldGrabbable != null && currentHeldGrabbable.isHeavy)
        {
            // 1. 캐릭터가 끌고 있는 물체를 바라보게 회전
            Vector3 directionToObject = currentHeldGrabbable.transform.position - transform.position;
            directionToObject.y = 0; 

            if (directionToObject.sqrMagnitude > 0.001f)
            {
                // 몸통(물리 바디) 자체가 물체를 향하도록 방향 덮어쓰기
                if (playerController != null)
                {
                    playerController.OverrideFacingDirection = directionToObject.normalized;
                }

                // 비주얼 오브젝트(모델링) 기울기 연출
                Quaternion lookRot = Quaternion.LookRotation(directionToObject);
                Quaternion targetWorldRotation = lookRot * Quaternion.Euler(tiltAngle, 0, 0);
                characterVisual.rotation = Quaternion.Slerp(characterVisual.rotation, targetWorldRotation, tiltSpeed * Time.deltaTime);
            }
        }
        else
        {
            // 방향 덮어쓰기 해제
            if (playerController != null)
            {
                playerController.OverrideFacingDirection = null;
            }

            characterVisual.localRotation = Quaternion.Slerp(characterVisual.localRotation, Quaternion.identity, tiltSpeed * Time.deltaTime);
        }
    }

    // ====================================================================
    // 물리 기반 잡기 / 놓기 로직 (GrabbableObject 연동)
    // ====================================================================

    private bool TryPickUp(GrabbableObject grabbable)
    {
        Rigidbody target = grabbable.GetComponent<Rigidbody>();
        if (target == null || target.mass > maxCarryMass) return false;
        if (holdPoint == null) return false;

        currentHeldGrabbable = grabbable;
        grabbable.AddInteractor(this);
        return true;
    }

    private bool TryDragObject(GrabbableObject grabbable)
    {
        Rigidbody target = grabbable.GetComponent<Rigidbody>();
        if (target == null) return false;
        if (dragPoint == null) return false;

        currentHeldGrabbable = grabbable;
        grabbable.AddInteractor(this);
        return true;
    }

    private void DropHeldObject()
    {
        if (currentHeldGrabbable == null) return;

        Rigidbody body = currentHeldGrabbable.GetComponent<Rigidbody>();

        // --- 기믹 6: 스냅존(SnapZone) 확인 ---
        Collider[] snapZones = Physics.OverlapSphere(body.transform.position, 1.5f, interactLayerMask, QueryTriggerInteraction.Collide);
        SnapZone closestSnapZone = null;
        float minDistance = float.MaxValue;

        foreach (var col in snapZones)
        {
            SnapZone snapZone = col.GetComponent<SnapZone>();
            if (snapZone != null && snapZone.CanSnap(currentHeldGrabbable))
            {
                float dist = Vector3.Distance(body.transform.position, snapZone.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestSnapZone = snapZone;
                }
            }
        }

        if (closestSnapZone != null)
        {
            // 스냅존에 물체를 넘김
            currentHeldGrabbable.RemoveInteractor(this, true);
            closestSnapZone.SnapObject(body);
        }
        else
        {
            currentHeldGrabbable.RemoveInteractor(this, false);
            
            // 가벼운 물체는 플레이어의 이동 속도를 받아 던지는 효과 부여
            Rigidbody playerRb = GetComponent<Rigidbody>();
            if (playerRb != null && !currentHeldGrabbable.isHeavy)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(playerRb.linearVelocity, Vector3.up);
                body.linearVelocity = planarVelocity;
            }
        }

        ClearHeldObjectState();
    }

    private void ForceDropHeldObject()
    {
        if (currentHeldGrabbable != null)
        {
            currentHeldGrabbable.RemoveInteractor(this, false);
            ClearHeldObjectState();
        }
    }

    private void ClearHeldObjectState()
    {
        currentHeldGrabbable = null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1, 1, 0, 0.3f);
        Vector3 origin = transform.position + Vector3.up * raycastHeightOffset;
        Ray ray = new Ray(origin, transform.forward);

        if (Physics.SphereCast(ray, sphereCastRadius, out RaycastHit hit, interactRange, interactLayerMask))
        {
            Gizmos.DrawSphere(ray.origin + ray.direction * hit.distance, sphereCastRadius);
        }
        else
        {
            Gizmos.DrawSphere(ray.origin + ray.direction * interactRange, sphereCastRadius);
        }
    }
}