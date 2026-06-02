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

    [Header("상호작용 하이라이트 설정")]
    [SerializeField] private bool useInteractableOutline = true;
    [SerializeField] private Color interactableOutlineColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private float interactableOutlineWidth = 4f;
    [SerializeField] private Outline.Mode interactableOutlineMode = Outline.Mode.OutlineVisible;

    [Header("운반(Hold) 설정")]
    [Tooltip("가벼운 물체를 들 때 위치할 빈 오브젝트를 연결하세요.")]
    public Transform holdPoint; 
    [Tooltip("무거운 물체를 끌 때 위치할 빈 오브젝트를 연결하세요.")]
    public Transform dragPoint; 
    public float maxCarryMass = 20f;
    private float holdTimer = 0f;

    [Header("던지기 설정")]
    public InputActionReference throwAction;
    [SerializeField] private float throwForwardSpeed = 9f;
    [SerializeField] private float throwUpwardSpeed = 2.5f;
    [SerializeField] private float heavyThrowSpeedMultiplier = 0.45f;
    [SerializeField] private float throwSpinSpeed = 6f;

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
    private InteractableOutlineHighlight currentOutlineHighlight;

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
        if (throwAction != null)
        {
            throwAction.action.Enable();
            throwAction.action.started += OnThrowStarted;
        }
    }

    private void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.action.started -= OnInteractStarted;
            interactAction.action.Disable();
        }
        if (grabAction != null) grabAction.action.Disable();
        if (throwAction != null)
        {
            throwAction.action.started -= OnThrowStarted;
            throwAction.action.Disable();
        }
        ClearCurrentOutlineHighlight();
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

    private void OnThrowStarted(InputAction.CallbackContext context)
    {
        ThrowHeldObject();
    }

    private void OnInteractStarted(InputAction.CallbackContext context)
    {
        // 이미 가벼운 무언가를 들고 있다면 E키로 내려놓기
        if (currentHeldGrabbable != null && !currentHeldGrabbable.isHeavy)
        {
            // 💡 [개선] 만약 손에 물건을 든 상태에서 조준선에 상호작용 가능한 문(IInteractable)이 감지된다면,
            // 물건을 즉시 내려놓기 전에 문과의 상호작용을 먼저 수행해 열쇠 사용을 시도합니다.
            if (currentTargetInteractable != null)
            {
                currentTargetInteractable.RequestInteract(gameObject);
                
                // 만약 문이 열리면서 손에 쥐고 있던 열쇠가 안전하게 소모(파괴)되었다면 그대로 종료합니다.
                if (currentHeldGrabbable == null)
                {
                    return;
                }
            }

            // 조준선에 기믹이 없거나 열쇠가 사용되지 않았다면 원래대로 바닥에 내려놓습니다.
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

        // 반경 내의 모든 물체를 감지 (트리거 콜라이더 상태인 열쇠도 무조건 잡도록 QueryTriggerInteraction.Collide 명시)
        RaycastHit[] hits = Physics.SphereCastAll(ray, sphereCastRadius, interactRange, interactLayerMask, QueryTriggerInteraction.Collide);
        
        // 가까운 순서대로 정렬
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        GrabbableObject foundGrabbable = null;
        IInteractable foundInteractable = null;

        foreach (var hit in hits)
        {
            // 💡 상자 안에 담긴 열쇠를 조준할 때 상자 콜라이더가 가로막는 현상을 해결하기 위해
            // 레이캐스트에 걸린 모든 물체 중에서 들 수 있는 물체(GrabbableObject)와 상호작용 물체(IInteractable)를 각각 먼저 수집합니다.
            GrabbableObject grabbable = hit.collider.GetComponentInParent<GrabbableObject>();
            
            // 💥 [추가 핵심] 내가 이미 손(머리 위)에 들고 있는 물건은 본인의 조준 레이저를 가로막지 않도록 조준 검사 대상에서 제외합니다!
            if (grabbable != null && grabbable == currentHeldGrabbable)
            {
                continue;
            }

            if (grabbable != null && foundGrabbable == null)
            {
                foundGrabbable = grabbable;
            }

            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null && foundInteractable == null)
            {
                foundInteractable = interactable;
            }
        }

        // ⭐ 들고 다닐 수 있는 물체(GrabbableObject)를 일반 상호작용 기믹(IInteractable)보다 최우선적으로 조준합니다!
        if (foundGrabbable != null)
        {
            currentTargetGrabbable = foundGrabbable;
            currentTargetInteractable = null;
            SetCurrentOutlineTarget(foundGrabbable.gameObject);
        }
        else
        {
            currentTargetGrabbable = null;
            currentTargetInteractable = foundInteractable;
            SetCurrentOutlineTarget(GetInteractableGameObject(foundInteractable));
        }
    }

    private void SetCurrentOutlineTarget(GameObject targetObject)
    {
        if (!useInteractableOutline)
        {
            ClearCurrentOutlineHighlight();
            return;
        }

        InteractableOutlineHighlight nextHighlight = null;
        if (targetObject != null)
        {
            nextHighlight = targetObject.GetComponent<InteractableOutlineHighlight>();
            if (nextHighlight == null)
            {
                // 상호작용 가능한 대상에는 런타임에 Outline 래퍼를 붙여 별도 프리팹 수정 없이 표시합니다.
                nextHighlight = targetObject.AddComponent<InteractableOutlineHighlight>();
            }

            nextHighlight.Configure(interactableOutlineColor, interactableOutlineWidth, interactableOutlineMode);
        }

        if (currentOutlineHighlight == nextHighlight)
        {
            return;
        }

        ClearCurrentOutlineHighlight();
        currentOutlineHighlight = nextHighlight;

        if (currentOutlineHighlight != null)
        {
            currentOutlineHighlight.SetHighlighted(true);
        }
    }

    private void ClearCurrentOutlineHighlight()
    {
        if (currentOutlineHighlight != null)
        {
            currentOutlineHighlight.SetHighlighted(false);
            currentOutlineHighlight = null;
        }
    }

    private GameObject GetInteractableGameObject(IInteractable interactable)
    {
        Component component = interactable as Component;
        return component != null ? component.gameObject : null;
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
        if (target == null)
        {
            Debug.LogWarning($"⚠️ [{grabbable.gameObject.name}]에 Rigidbody 컴포넌트가 없어 집을 수 없습니다!");
            return false;
        }
        if (target.mass > maxCarryMass)
        {
            Debug.LogWarning($"⚠️ [{grabbable.gameObject.name}]의 무게({target.mass}kg)가 최대 들기 무게({maxCarryMass}kg)보다 무거워 집을 수 없습니다!");
            return false;
        }
        if (holdPoint == null)
        {
            Debug.LogWarning($"⚠️ PlayerInteractor의 holdPoint가 지정되지 않아 물건을 집을 수 없습니다!");
            return false;
        }

        currentHeldGrabbable = grabbable;
        grabbable.AddInteractor(this);
        return true;
    }

    private bool TryDragObject(GrabbableObject grabbable)
    {
        Rigidbody target = grabbable.GetComponent<Rigidbody>();
        if (target == null)
        {
            Debug.LogWarning($"⚠️ [{grabbable.gameObject.name}]에 Rigidbody 컴포넌트가 없어 끌 수 없습니다!");
            return false;
        }
        if (dragPoint == null)
        {
            Debug.LogWarning($"⚠️ PlayerInteractor의 dragPoint가 지정되지 않아 물건을 끌 수 없습니다!");
            return false;
        }

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

    private void ThrowHeldObject()
    {
        if (currentHeldGrabbable == null)
        {
            return;
        }

        if (currentHeldGrabbable.isHeavy && currentHeldGrabbable.InteractorCount > 1)
        {
            Debug.Log("[PlayerInteractor] 여러 명이 잡은 무거운 물체는 한 명만 임의로 던질 수 없습니다.");
            return;
        }

        GrabbableObject objectToThrow = currentHeldGrabbable;
        Rigidbody body = objectToThrow.GetComponent<Rigidbody>();
        if (body == null)
        {
            Debug.LogWarning($"⚠️ [{objectToThrow.gameObject.name}]에 Rigidbody 컴포넌트가 없어 던질 수 없습니다!");
            return;
        }

        Vector3 throwDirection = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;
        float objectThrowMultiplier = objectToThrow.isHeavy ? heavyThrowSpeedMultiplier : 1f;
        Vector3 throwVelocity = (throwDirection * throwForwardSpeed + Vector3.up * throwUpwardSpeed) * objectThrowMultiplier;

        Rigidbody playerBody = GetComponent<Rigidbody>();
        if (playerBody != null)
        {
            throwVelocity += Vector3.ProjectOnPlane(playerBody.linearVelocity, Vector3.up);
        }

        // 던지기는 스냅존 체크 없이 즉시 손에서 해제한 뒤 전방 속도를 부여합니다.
        objectToThrow.RemoveInteractor(this, false);
        ClearHeldObjectState();

        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = throwVelocity;

        Vector3 spinAxis = Vector3.Cross(Vector3.up, throwDirection);
        body.angularVelocity = spinAxis.sqrMagnitude > 0.001f
            ? spinAxis.normalized * throwSpinSpeed
            : Vector3.zero;
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

    public GrabbableObject CurrentHeldGrabbable => currentHeldGrabbable;

    // 손에 든 물체를 강제로 소모(파괴)시키는 메서드
    public void ConsumeHeldObject()
    {
        if (currentHeldGrabbable != null)
        {
            GrabbableObject objectToDestroy = currentHeldGrabbable;
            ForceDropHeldObject();
            Destroy(objectToDestroy.gameObject);
        }
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
