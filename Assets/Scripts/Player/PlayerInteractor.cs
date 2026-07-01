using System;
using Unity.Netcode;
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
    private bool currentHeldIsHeavy;
    private InteractableOutlineHighlight currentOutlineHighlight;
    private InteractableOutlineHighlight currentHeldOutlineHighlight;

    public CapsuleCollider PlayerCollider { get; private set; }
    private PlayerController playerController;
    private NetworkObject playerNetworkObject;

    // 잡기 요청 시 서버가 이 클라이언트의 PlayerInteractor를 역참조할 때 사용합니다.
    public ulong OwnerClientId => playerNetworkObject != null ? playerNetworkObject.OwnerClientId : 0;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerNetworkObject = GetComponentInParent<NetworkObject>();

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
        ForceDropHeldObject();
        ClearCurrentOutlineHighlight();
        ClearHeldOutlineHighlight();
    }

    private void Update()
    {
        // 원격 프록시 인스턴스는 로컬 키보드 입력에 반응하면 안 되므로 소유자만 처리한다.
        if (playerController != null && !playerController.HasInputAuthority)
        {
            return;
        }

        if (playerController != null && playerController.IsKnockedDown)
        {
            playerController.OverrideFacingDirection = null;
            ClearCurrentInteractionTargets();
            ForceDropHeldObject();
            holdTimer = 0f;
            return;
        }

        // 들고 있는 물체와 너무 멀어지면 강제로 놓는다. (소유자가 직접 판단 → 서버에 놓기 요청)
        if (currentHeldGrabbable != null)
        {
            float dist = Vector3.Distance(currentHeldGrabbable.transform.position, transform.position);
            if (dist > currentHeldGrabbable.HeldFollowMaxDistance)
            {
                ForceDropHeldObject();
            }
        }

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
        if (playerController != null && !playerController.HasInputAuthority)
        {
            return;
        }

        if (playerController != null && playerController.IsKnockedDown)
        {
            return;
        }

        ThrowHeldObject();
    }

    private void OnInteractStarted(InputAction.CallbackContext context)
    {
        if (playerController != null && !playerController.HasInputAuthority)
        {
            return;
        }

        if (playerController != null && playerController.IsKnockedDown)
        {
            return;
        }

        // 이미 가벼운 무언가를 들고 있다면 E키로 내려놓기
        if (currentHeldGrabbable != null && !currentHeldGrabbable.isHeavy)
        {
            // 손에 물건을 든 상태에서 조준선에 상호작용 가능한 문(IInteractable)이 감지되면,
            // 내려놓기 전에 문과 상호작용을 먼저 수행해 열쇠 사용을 시도합니다.
            if (currentTargetInteractable != null)
            {
                currentTargetInteractable.RequestInteract(gameObject);

                // 열쇠가 소모되어 손이 비었으면 그대로 종료
                if (currentHeldGrabbable == null)
                {
                    return;
                }
            }

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

        RaycastHit[] hits = Physics.SphereCastAll(ray, sphereCastRadius, interactRange, interactLayerMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        GrabbableObject foundGrabbable = null;
        IInteractable foundInteractable = null;

        foreach (var hit in hits)
        {
            GrabbableObject grabbable = hit.collider.GetComponentInParent<GrabbableObject>();

            // 내가 이미 들고 있는 물건은 조준 검사 대상에서 제외
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
            ClearHeldOutlineHighlight();
            return;
        }

        InteractableOutlineHighlight nextHighlight = null;
        if (targetObject != null)
        {
            nextHighlight = GetOrCreateOutlineHighlight(targetObject);
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
            if (currentOutlineHighlight != currentHeldOutlineHighlight)
            {
                currentOutlineHighlight.SetHighlighted(false);
            }

            currentOutlineHighlight = null;
        }
    }

    private void SetHeldOutlineTarget(GameObject targetObject)
    {
        if (!useInteractableOutline)
        {
            ClearHeldOutlineHighlight();
            return;
        }

        InteractableOutlineHighlight nextHighlight = null;
        if (targetObject != null)
        {
            nextHighlight = GetOrCreateOutlineHighlight(targetObject);
            nextHighlight.Configure(interactableOutlineColor, interactableOutlineWidth, interactableOutlineMode);
        }

        if (currentHeldOutlineHighlight == nextHighlight)
        {
            return;
        }

        ClearHeldOutlineHighlight();
        currentHeldOutlineHighlight = nextHighlight;

        if (currentHeldOutlineHighlight != null)
        {
            currentHeldOutlineHighlight.SetHighlighted(true);
        }
    }

    private void ClearHeldOutlineHighlight()
    {
        if (currentHeldOutlineHighlight != null)
        {
            if (currentHeldOutlineHighlight != currentOutlineHighlight)
            {
                currentHeldOutlineHighlight.SetHighlighted(false);
            }

            currentHeldOutlineHighlight = null;
        }
    }

    private InteractableOutlineHighlight GetOrCreateOutlineHighlight(GameObject targetObject)
    {
        InteractableOutlineHighlight outlineHighlight = targetObject.GetComponent<InteractableOutlineHighlight>();
        if (outlineHighlight == null)
        {
            outlineHighlight = targetObject.AddComponent<InteractableOutlineHighlight>();
        }

        return outlineHighlight;
    }

    private void ClearCurrentInteractionTargets()
    {
        currentTargetGrabbable = null;
        currentTargetInteractable = null;
        ClearCurrentOutlineHighlight();
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
            Vector3 directionToObject = currentHeldGrabbable.transform.position - transform.position;
            directionToObject.y = 0;

            if (directionToObject.sqrMagnitude > 0.001f)
            {
                if (playerController != null)
                {
                    playerController.OverrideFacingDirection = directionToObject.normalized;
                }

                Quaternion lookRot = Quaternion.LookRotation(directionToObject);
                Quaternion targetWorldRotation = lookRot * Quaternion.Euler(tiltAngle, 0, 0);
                characterVisual.rotation = Quaternion.Slerp(characterVisual.rotation, targetWorldRotation, tiltSpeed * Time.deltaTime);
            }
        }
        else
        {
            if (playerController != null)
            {
                playerController.OverrideFacingDirection = null;
            }

            characterVisual.localRotation = Quaternion.Slerp(characterVisual.localRotation, Quaternion.identity, tiltSpeed * Time.deltaTime);
        }
    }

    // ====================================================================
    // 물리 기반 잡기 / 놓기 로직 (서버 권한 GrabbableObject 연동)
    // ====================================================================

    private bool TryPickUp(GrabbableObject grabbable)
    {
        Rigidbody target = grabbable.GetComponent<Rigidbody>();
        if (target == null)
        {
            Debug.LogWarning($"⚠️ [{grabbable.gameObject.name}]에 Rigidbody가 없어 집을 수 없습니다!");
            return false;
        }
        if (target.mass > maxCarryMass)
        {
            Debug.LogWarning($"⚠️ [{grabbable.gameObject.name}]의 무게가 최대 들기 무게보다 무거워 집을 수 없습니다!");
            return false;
        }
        if (holdPoint == null)
        {
            Debug.LogWarning($"⚠️ PlayerInteractor의 holdPoint가 지정되지 않아 물건을 집을 수 없습니다!");
            return false;
        }

        BeginHold(grabbable);
        return true;
    }

    private bool TryDragObject(GrabbableObject grabbable)
    {
        Rigidbody target = grabbable.GetComponent<Rigidbody>();
        if (target == null)
        {
            Debug.LogWarning($"⚠️ [{grabbable.gameObject.name}]에 Rigidbody가 없어 끌 수 없습니다!");
            return false;
        }
        if (dragPoint == null)
        {
            Debug.LogWarning($"⚠️ PlayerInteractor의 dragPoint가 지정되지 않아 물건을 끌 수 없습니다!");
            return false;
        }

        BeginHold(grabbable);
        return true;
    }

    // 잡기 시작: 서버에 홀더 추가를 요청하고, 소유자 로컬 효과(아웃라인/충돌무시/운반속도)를 적용한다.
    private void BeginHold(GrabbableObject grabbable)
    {
        currentHeldGrabbable = grabbable;
        currentHeldIsHeavy = grabbable.isHeavy;

        grabbable.RequestAddInteractor(this);

        if (!grabbable.isHeavy)
        {
            // 가벼운 물체는 내 플레이어와 충돌하지 않도록 로컬에서 무시 처리(각 머신 기준).
            grabbable.SetIgnoreCollisionWith(this, true);
        }

        if (playerController != null)
        {
            playerController.SetCarrySpeedMultiplier(grabbable.GetCarrySpeedMultiplier());
        }

        SetHeldOutlineTarget(grabbable.gameObject);
    }

    private void DropHeldObject()
    {
        if (currentHeldGrabbable == null) return;

        GrabbableObject grabbable = currentHeldGrabbable;
        Rigidbody body = grabbable.GetComponent<Rigidbody>();

        // --- 스냅존(SnapZone) 확인 ---
        SnapZone closestSnapZone = FindSnapZone(grabbable);

        if (closestSnapZone != null)
        {
            grabbable.RequestRemoveInteractor(this, true);
            closestSnapZone.RequestSnap(grabbable);
        }
        else if (!grabbable.isHeavy)
        {
            // 가벼운 물체: 플레이어의 이동 속도를 실어 살짝 던지듯 내려놓기 (서버에서 물리 적용)
            Vector3 tossVelocity = Vector3.zero;
            Rigidbody playerRb = GetComponent<Rigidbody>();
            if (playerRb != null)
            {
                tossVelocity = Vector3.ProjectOnPlane(playerRb.linearVelocity, Vector3.up);
            }
            grabbable.RequestThrow(this, tossVelocity, Vector3.zero);
        }
        else
        {
            grabbable.RequestRemoveInteractor(this, false);
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

        Vector3 throwDirection = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;
        float objectThrowMultiplier = objectToThrow.isHeavy ? heavyThrowSpeedMultiplier : 1f;
        Vector3 throwVelocity = (throwDirection * throwForwardSpeed + Vector3.up * throwUpwardSpeed) * objectThrowMultiplier;

        Rigidbody playerBody = GetComponent<Rigidbody>();
        if (playerBody != null)
        {
            throwVelocity += Vector3.ProjectOnPlane(playerBody.linearVelocity, Vector3.up);
        }

        Vector3 spinAxis = Vector3.Cross(Vector3.up, throwDirection);
        Vector3 angularVelocity = spinAxis.sqrMagnitude > 0.001f
            ? spinAxis.normalized * throwSpinSpeed
            : Vector3.zero;

        objectToThrow.RequestThrow(this, throwVelocity, angularVelocity);
        ClearHeldObjectState();
    }

    private void ForceDropHeldObject()
    {
        if (currentHeldGrabbable != null)
        {
            currentHeldGrabbable.RequestRemoveInteractor(this, false);
            ClearHeldObjectState();
        }
    }

    private void ClearHeldObjectState()
    {
        if (currentHeldGrabbable != null && !currentHeldIsHeavy)
        {
            // 충돌 무시 해제(로컬)
            currentHeldGrabbable.SetIgnoreCollisionWith(this, false);
        }

        if (playerController != null)
        {
            playerController.ResetCarrySpeedMultiplier();
        }

        ClearHeldOutlineHighlight();
        currentHeldGrabbable = null;
        currentHeldIsHeavy = false;
    }

    private SnapZone FindSnapZone(GrabbableObject grabbable)
    {
        Rigidbody body = grabbable.GetComponent<Rigidbody>();
        Vector3 center = body != null ? body.position : grabbable.transform.position;

        Collider[] snapZones = Physics.OverlapSphere(center, 1.5f, interactLayerMask, QueryTriggerInteraction.Collide);
        SnapZone closest = null;
        float minDistance = float.MaxValue;

        foreach (var col in snapZones)
        {
            SnapZone snapZone = col.GetComponent<SnapZone>();
            if (snapZone != null && snapZone.CanSnap(grabbable))
            {
                float dist = Vector3.Distance(center, snapZone.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = snapZone;
                }
            }
        }

        return closest;
    }

    public GrabbableObject CurrentHeldGrabbable => currentHeldGrabbable;

    // 서버가 들고 있던 물체를 소모(Despawn)하기로 결정했을 때, 소유자 로컬의 잡기 효과만 정리합니다.
    // 실제 Despawn은 서버가 수행하며 NetworkObject 파괴가 모든 클라이언트에 복제됩니다.
    // (DoorController의 서버 권한 열쇠 사용 경로에서 호출)
    public void NotifyHeldObjectConsumedLocally()
    {
        ClearHeldObjectState();
    }

    // 손에 든 물체를 소모(파괴)시키는 메서드 (네트워크에서는 서버가 Despawn)
    public void ConsumeHeldObject()
    {
        if (currentHeldGrabbable != null)
        {
            GrabbableObject objectToConsume = currentHeldGrabbable;
            objectToConsume.RequestRemoveInteractor(this, true);
            ClearHeldObjectState();
            objectToConsume.RequestConsume(this);
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
