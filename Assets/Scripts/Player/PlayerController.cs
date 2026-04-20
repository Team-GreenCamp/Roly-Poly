using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody physicsBody;
    [SerializeField] private CapsuleCollider physicsCollider;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private NetworkObject networkObject;
    [SerializeField] private Transform holdPoint;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float movementAcceleration = 24f;
    [SerializeField] private float airAcceleration = 10f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float terminalVelocity = -50f;
    [SerializeField] private float fallGravityMultiplier = 2f;

    [Header("Balance")]
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.35f, 0f);
    [SerializeField] private float uprightTorque = 90f;
    [SerializeField] private float uprightDamping = 10f;
    [SerializeField] private float fallenTiltAngle = 35f;
    [SerializeField] private float recoveryDelay = 1f;
    [SerializeField] private float recoveryTorqueMultiplier = 2.5f;
    [SerializeField] private float turnTorque = 35f;
    [SerializeField] private float turnDamping = 6f;
    [SerializeField] private float maxAngularVelocity = 25f;

    [Header("Interaction")]
    [SerializeField] private float interactionDistance = 2.2f;
    [SerializeField] private float interactionRadius = 0.25f;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private float maxCarryMass = 20f;
    [SerializeField] private Vector3 holdPointLocalOffset = new Vector3(0.35f, 1.2f, 1.5f);
    [SerializeField] private Color interactableOutlineColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private float interactableOutlineWidth = 4f;
    [SerializeField] private Outline.Mode interactableOutlineMode = Outline.Mode.OutlineVisible;

    [Header("Throw")]
    [SerializeField] private float throwForce = 8f;
    [SerializeField] private float throwUpwardRatio = 0.18f;
    [SerializeField] private float throwVelocityInheritance = 0.75f;
    [SerializeField] private float throwCollisionIgnoreTime = 0.25f;

    [Header("Ground Check")]
    [SerializeField] private float groundedOffset = 0.1f;
    [SerializeField] private float groundedRadius = 0.25f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Impact")]
    [SerializeField] private float playerCollisionImpact = 5f;
    [SerializeField] private float rigidbodyImpactSpeedThreshold = 5f;
    [SerializeField] private float heavyObjectMassThreshold = 20f;
    [SerializeField] private float heavyObjectDownwardSpeedThreshold = 6f;
    [SerializeField] private float landingImpactThreshold = 10f;
    [SerializeField] private float landingTorqueMultiplier = 0.2f;
    [SerializeField] private float externalImpactForceMultiplier = 1f;
    [SerializeField] private float externalImpactTorqueMultiplier = 0.35f;

    [Header("Slope")]
    [SerializeField] private float slideSlopeAngle = 32f;
    [SerializeField] private float slideForce = 18f;

    [Header("Respawn")]
    [SerializeField] private Transform currentCheckpoint;
    [SerializeField] private float respawnUpOffset = 0.25f;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;
    private InputAction interactAction;
    private InputAction throwAction;

    private Vector2 moveInput;
    private Vector3 currentHorizontalVelocity;
    private Vector3 groundNormal = Vector3.up;
    private bool isGrounded;
    private bool jumpQueued;
    private bool isPhysicsOwner;
    private float lastVerticalVelocity;
    private float timeSinceLargeTilt;

    private Rigidbody heldRigidbody;
    private Transform heldOriginalParent;
    private Collider[] heldColliders = Array.Empty<Collider>();
    private bool heldUseGravity;
    private bool heldIsKinematic;
    private RigidbodyInterpolation heldInterpolation;
    private CollisionDetectionMode heldCollisionMode;
    private CarryableObject activeCarryableObject;
    private float activeCarrySpeedMultiplier = 1f;
    private bool activeCarryBlocksJump;
    private InteractableOutlineHighlight currentInteractionHighlight;
    private Vector3 fallbackRespawnPosition;
    private Quaternion fallbackRespawnRotation;

    private void Reset()
    {
        CacheReferences();
        ApplySafeRanges();
    }

    private void OnValidate()
    {
        CacheReferences();
        ApplySafeRanges();
    }

    private void Awake()
    {
        CacheReferences();
        ApplySafeRanges();
        CacheFallbackRespawnPoint();
        ResolveInputActions();
        EnsureLayerMasksConfigured();
        EnsureRuntimePhysicsComponents();
        UpdatePhysicsOwnershipState(force: true);
    }

    private void OnEnable()
    {
        ResolveInputActions();
        EnsureLayerMasksConfigured();
    }

    private void OnDisable()
    {
        ClearInteractionHighlight();
        ForceDropHeldObject();
        activeCarryableObject?.ReleaseCarrier(this);
        ClearActiveCarryRule();
    }

    private void Update()
    {
        UpdatePhysicsOwnershipState();

        if (IsServer && heldRigidbody != null && (!IsSpawned || IsOwner))
        {
            UpdateHeldObjectPose();
        }

        if (!CanProcessInput())
        {
            ClearInteractionHighlight();
            return;
        }

        if (playerInput == null || physicsBody == null)
        {
            ClearInteractionHighlight();
            return;
        }

        ReadInput();
        UpdateInteractionHighlight();
        HandleThrowInput();
        HandleInteractionInput();
        UpdateHeldObjectPose();
        SendHeldObjectPoseToServer();
    }

    private void LateUpdate()
    {
        if (!CanProcessInput())
        {
            return;
        }

        // 네트워크 보간이 로컬 예측 위치를 덮어쓴 뒤에도 손 위치에 붙어 보이게 한다.
        UpdateHeldObjectPose();
    }

    private void FixedUpdate()
    {
        if (!CanProcessInput() || physicsBody == null || physicsBody.isKinematic)
        {
            return;
        }

        UpdateGroundedState();
        ApplyCustomGravity();
        ApplyJump();
        UpdateMovement();
        ApplySlopeSlide();
        ApplyBalanceTorques();
        ClampVerticalVelocity();
    }

    public bool IsInteractPressedThisFrame()
    {
        return CanProcessInput() && interactAction != null && interactAction.WasPressedThisFrame();
    }

    public bool CanCarryObject(CarryableObject carryableObject)
    {
        return CanProcessInput()
            && carryableObject != null
            && heldRigidbody == null
            && activeCarryableObject == null;
    }

    public bool TryStartCarryObject(CarryableObject carryableObject)
    {
        if (!CanCarryObject(carryableObject))
        {
            return false;
        }

        return TryPickUp(carryableObject.AttachedRigidbody, carryableObject);
    }

    public void ApplyCarryRule(CarryableObject carryableObject)
    {
        if (carryableObject == null)
        {
            return;
        }

        if (activeCarryableObject != null && activeCarryableObject != carryableObject)
        {
            return;
        }

        activeCarryableObject = carryableObject;
        activeCarrySpeedMultiplier = Mathf.Clamp(carryableObject.MoveSpeedMultiplier, 0.1f, 1f);
        activeCarryBlocksJump = carryableObject.BlockJumpWhileCarrying;
    }

    public void ClearCarryRule(CarryableObject carryableObject)
    {
        if (activeCarryableObject != carryableObject)
        {
            return;
        }

        ClearActiveCarryRule();
    }

    public Collider GetCarryCollisionCollider()
    {
        return physicsCollider;
    }

    public void DropCarriedObject()
    {
        if (heldRigidbody != null)
        {
            DropHeldObject();
            return;
        }

        activeCarryableObject?.ReleaseCarrier(this);
    }

    public void SetCheckpoint(Transform checkpoint)
    {
        if (checkpoint == null || !CanProcessInput())
        {
            return;
        }

        currentCheckpoint = checkpoint;
    }

    public void RespawnAtCheckpoint()
    {
        if (!CanProcessInput() || physicsBody == null)
        {
            return;
        }

        ForceDropHeldObject();
        activeCarryableObject?.ReleaseCarrier(this);
        ClearActiveCarryRule();

        Vector3 spawnPosition = currentCheckpoint != null
            ? currentCheckpoint.position
            : fallbackRespawnPosition;
        Quaternion spawnRotation = currentCheckpoint != null
            ? currentCheckpoint.rotation
            : fallbackRespawnRotation;

        spawnPosition += Vector3.up * respawnUpOffset;
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.position = spawnPosition;
        physicsBody.rotation = spawnRotation;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        currentHorizontalVelocity = Vector3.zero;
        lastVerticalVelocity = 0f;
        jumpQueued = false;
    }

    public void ApplyWobbleImpulse(Vector3 worldDirection, float strength)
    {
        Vector3 direction = worldDirection.sqrMagnitude > 0.0001f
            ? worldDirection.normalized
            : transform.forward;
        Vector3 hitPoint = physicsBody != null
            ? physicsBody.worldCenterOfMass + Vector3.up * 0.5f
            : transform.position + Vector3.up * 0.5f;

        ApplyExternalImpulse(direction * strength, hitPoint);
    }

    public void ApplyExternalImpulse(Vector3 force, Vector3 hitPoint)
    {
        if (!CanProcessInput() || physicsBody == null || physicsBody.isKinematic)
        {
            return;
        }

        physicsBody.AddForce(force * externalImpactForceMultiplier, ForceMode.Impulse);

        Vector3 lever = hitPoint - physicsBody.worldCenterOfMass;
        Vector3 torque = Vector3.Cross(lever, force) * externalImpactTorqueMultiplier;
        physicsBody.AddTorque(torque, ForceMode.Impulse);
    }

    private void CacheReferences()
    {
        if (physicsBody == null)
        {
            physicsBody = GetComponent<Rigidbody>();
        }

        if (physicsCollider == null)
        {
            physicsCollider = GetComponent<CapsuleCollider>();
        }

        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInput>();
        }

        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (holdPoint == null)
        {
            Transform candidate = transform.Find("HoldPoint");
            if (candidate != null)
            {
                holdPoint = candidate;
            }
        }
    }

    private void ApplySafeRanges()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        rotationSpeed = Mathf.Max(1f, rotationSpeed);
        movementAcceleration = Mathf.Max(0f, movementAcceleration);
        airAcceleration = Mathf.Max(0f, airAcceleration);
        jumpHeight = Mathf.Max(0.1f, jumpHeight);
        gravity = Mathf.Min(-0.01f, gravity);
        terminalVelocity = Mathf.Min(-1f, terminalVelocity);
        fallGravityMultiplier = Mathf.Max(1f, fallGravityMultiplier);
        uprightTorque = Mathf.Max(0f, uprightTorque);
        uprightDamping = Mathf.Max(0f, uprightDamping);
        fallenTiltAngle = Mathf.Clamp(fallenTiltAngle, 1f, 89f);
        recoveryDelay = Mathf.Max(0f, recoveryDelay);
        recoveryTorqueMultiplier = Mathf.Max(1f, recoveryTorqueMultiplier);
        turnTorque = Mathf.Max(0f, turnTorque);
        turnDamping = Mathf.Max(0f, turnDamping);
        maxAngularVelocity = Mathf.Max(1f, maxAngularVelocity);
        interactionDistance = Mathf.Max(0.25f, interactionDistance);
        interactionRadius = Mathf.Max(0f, interactionRadius);
        maxCarryMass = Mathf.Max(0.1f, maxCarryMass);
        interactableOutlineWidth = Mathf.Clamp(interactableOutlineWidth, 0f, 10f);
        throwForce = Mathf.Max(0f, throwForce);
        throwUpwardRatio = Mathf.Clamp01(throwUpwardRatio);
        throwVelocityInheritance = Mathf.Max(0f, throwVelocityInheritance);
        throwCollisionIgnoreTime = Mathf.Max(0f, throwCollisionIgnoreTime);
        groundedOffset = Mathf.Max(0.01f, groundedOffset);
        groundedRadius = Mathf.Max(0.01f, groundedRadius);
        playerCollisionImpact = Mathf.Max(0f, playerCollisionImpact);
        rigidbodyImpactSpeedThreshold = Mathf.Max(0f, rigidbodyImpactSpeedThreshold);
        heavyObjectMassThreshold = Mathf.Max(0f, heavyObjectMassThreshold);
        heavyObjectDownwardSpeedThreshold = Mathf.Max(0f, heavyObjectDownwardSpeedThreshold);
        landingImpactThreshold = Mathf.Max(0f, landingImpactThreshold);
        landingTorqueMultiplier = Mathf.Max(0f, landingTorqueMultiplier);
        externalImpactForceMultiplier = Mathf.Max(0f, externalImpactForceMultiplier);
        externalImpactTorqueMultiplier = Mathf.Max(0f, externalImpactTorqueMultiplier);
        slideSlopeAngle = Mathf.Clamp(slideSlopeAngle, 1f, 89f);
        slideForce = Mathf.Max(0f, slideForce);
        respawnUpOffset = Mathf.Max(0f, respawnUpOffset);
    }

    private void CacheFallbackRespawnPoint()
    {
        fallbackRespawnPosition = transform.position;
        fallbackRespawnRotation = transform.rotation;
    }

    private void EnsureLayerMasksConfigured()
    {
        if (groundLayers.value == 0)
        {
            groundLayers = ~0;
        }

        if (interactionLayers.value == 0)
        {
            interactionLayers = ~0;
        }

        int selfLayerMask = 1 << gameObject.layer;
        groundLayers &= ~selfLayerMask;
        interactionLayers &= ~selfLayerMask;
    }

    private void EnsureRuntimePhysicsComponents()
    {
        if (physicsBody == null)
        {
            physicsBody = gameObject.AddComponent<Rigidbody>();
        }

        if (physicsCollider == null)
        {
            physicsCollider = gameObject.AddComponent<CapsuleCollider>();
            physicsCollider.height = 1.75f;
            physicsCollider.radius = 0.2f;
            physicsCollider.center = new Vector3(0f, 0.9f, 0f);
            physicsCollider.direction = 1;
        }

        physicsBody.useGravity = false;
        physicsBody.interpolation = RigidbodyInterpolation.Interpolate;
        physicsBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        physicsBody.maxAngularVelocity = maxAngularVelocity;
        physicsBody.centerOfMass = centerOfMassOffset;
    }

    private void ResolveInputActions()
    {
        if (playerInput == null || playerInput.actions == null)
        {
            return;
        }

        moveAction = playerInput.actions["Move"];
        jumpAction = playerInput.actions["Jump"];
        sprintAction = playerInput.actions["Sprint"];
        interactAction = playerInput.actions["Interact"];
        throwAction = playerInput.actions.FindAction("Throw", false);
    }

    private bool CanProcessInput()
    {
        if (networkObject == null)
        {
            return true;
        }

        if (networkObject.IsSpawned)
        {
            return networkObject.IsOwner;
        }

        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
    }

    private void UpdatePhysicsOwnershipState(bool force = false)
    {
        if (physicsBody == null)
        {
            return;
        }

        bool shouldSimulate = CanProcessInput();
        if (!force && isPhysicsOwner == shouldSimulate)
        {
            return;
        }

        isPhysicsOwner = shouldSimulate;
        physicsBody.isKinematic = !shouldSimulate;
        physicsBody.useGravity = false;
        physicsBody.maxAngularVelocity = maxAngularVelocity;
        physicsBody.centerOfMass = centerOfMassOffset;

        if (!shouldSimulate)
        {
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
        }
    }

    private void ReadInput()
    {
        moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        if (jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            jumpQueued = true;
        }
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        Transform movementReference = GetMovementReference();
        Vector3 forward = Vector3.ProjectOnPlane(movementReference.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(movementReference.right, Vector3.up).normalized;
        Vector3 direction = (forward * input.y) + (right * input.x);
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    private Transform GetMovementReference()
    {
        // 카메라가 실제로 바라보는 방향 기준으로 이동한다.
        Camera currentCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (currentCamera != null)
        {
            return currentCamera.transform;
        }

        Transform cameraRoot = transform.Find("CameraRoot");
        return cameraRoot != null ? cameraRoot : transform;
    }

    private Vector3 GetFacingDirection()
    {
        Transform movementReference = GetMovementReference();
        Vector3 facingDirection = Vector3.ProjectOnPlane(movementReference.forward, Vector3.up);
        return facingDirection.sqrMagnitude > 0.0001f ? facingDirection.normalized : transform.forward;
    }

    private Vector3 GetCharacterForwardDirection()
    {
        // 던지기는 카메라가 아니라 캐릭터 몸체가 바라보는 방향을 기준으로 한다.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private void UpdateGroundedState()
    {
        if (physicsCollider == null || physicsBody == null)
        {
            isGrounded = false;
            groundNormal = Vector3.up;
            return;
        }

        bool wasGrounded = isGrounded;
        float previousVerticalVelocity = lastVerticalVelocity;

        Vector3 center = transform.TransformPoint(physicsCollider.center);
        float worldRadius = GetWorldRadius();
        float halfHeight = GetWorldHalfHeight();
        float castDistance = Mathf.Max(groundedOffset, groundedRadius) + 0.05f;
        Vector3 castOrigin = center + (Vector3.up * Mathf.Max(0f, halfHeight - worldRadius + 0.02f));

        bool hitGround = Physics.SphereCast(
            castOrigin,
            Mathf.Max(groundedRadius, worldRadius * 0.95f),
            Vector3.down,
            out RaycastHit hit,
            (halfHeight - worldRadius) + castDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        groundNormal = hitGround ? hit.normal : Vector3.up;
        isGrounded = hitGround && physicsBody.linearVelocity.y <= 1f;

        if (!wasGrounded && isGrounded && previousVerticalVelocity <= -landingImpactThreshold)
        {
            Vector3 landingDirection = currentHorizontalVelocity.sqrMagnitude > 0.01f
                ? currentHorizontalVelocity.normalized
                : transform.forward;
            Vector3 landingForce = landingDirection * (Mathf.Abs(previousVerticalVelocity) * landingTorqueMultiplier);
            ApplyExternalImpulse(landingForce, center + (Vector3.up * 0.5f));
        }

        lastVerticalVelocity = physicsBody.linearVelocity.y;
    }

    private float GetWorldRadius()
    {
        if (physicsCollider == null)
        {
            return groundedRadius;
        }

        Vector3 scale = transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        return physicsCollider.radius * horizontalScale;
    }

    private float GetWorldHalfHeight()
    {
        if (physicsCollider == null)
        {
            return 0.5f;
        }

        return (physicsCollider.height * Mathf.Abs(transform.lossyScale.y)) * 0.5f;
    }

    private void ApplyCustomGravity()
    {
        float verticalVelocity = physicsBody.linearVelocity.y;
        float appliedGravity = verticalVelocity < 0f
            ? gravity * fallGravityMultiplier
            : gravity;

        physicsBody.AddForce(Vector3.up * appliedGravity, ForceMode.Acceleration);
    }

    private void ApplyJump()
    {
        bool shouldJump = jumpQueued;
        jumpQueued = false;

        if (!shouldJump || !isGrounded || activeCarryBlocksJump)
        {
            return;
        }

        Vector3 velocity = physicsBody.linearVelocity;
        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        physicsBody.linearVelocity = velocity;
        isGrounded = false;
    }

    private void UpdateMovement()
    {
        Vector3 moveDirection = GetMoveDirection(moveInput);
        float inputMagnitude = Mathf.Clamp01(moveInput.magnitude);
        // 운반 중에는 오브젝트별 이동 속도 제한을 적용한다.
        float targetSpeed = (CanSprint() ? sprintSpeed : walkSpeed) * inputMagnitude * activeCarrySpeedMultiplier;
        float acceleration = isGrounded ? movementAcceleration : airAcceleration;

        Vector3 velocity = physicsBody.linearVelocity;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 desiredVelocity = moveDirection * targetSpeed;
        Vector3 nextPlanarVelocity = Vector3.MoveTowards(planarVelocity, desiredVelocity, acceleration * Time.fixedDeltaTime);

        physicsBody.linearVelocity = nextPlanarVelocity + (Vector3.up * velocity.y);
        currentHorizontalVelocity = nextPlanarVelocity;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            // 이동 입력이 있을 때만 이동 방향을 향해 돈다.
            ApplyTurnTorque(moveDirection);
        }
        else
        {
            StabilizeIdleYaw();
        }
    }

    private void ApplyTurnTorque(Vector3 facingDirection)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude <= 0.0001f)
        {
            flatForward = Vector3.forward;
        }

        float currentYaw = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        // 좌우 이동에도 몸이 옆으로 꺾이지 않게 카메라 전방 기준으로 회전한다.
        float targetYaw = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
        float maxStep = rotationSpeed * Time.fixedDeltaTime;
        float desiredYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, maxStep);
        float yawDelta = Mathf.DeltaAngle(currentYaw, desiredYaw);
        float desiredYawVelocity = yawDelta * Mathf.Deg2Rad / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float currentYawVelocity = Vector3.Dot(physicsBody.angularVelocity, Vector3.up);
        float torque = (desiredYawVelocity - currentYawVelocity) * turnTorque - (currentYawVelocity * turnDamping);

        physicsBody.AddTorque(Vector3.up * torque, ForceMode.Acceleration);
    }

    private void StabilizeIdleYaw()
    {
        float currentYawVelocity = Vector3.Dot(physicsBody.angularVelocity, Vector3.up);
        float torque = (-currentYawVelocity * turnDamping);
        physicsBody.AddTorque(Vector3.up * torque, ForceMode.Acceleration);
    }

    private void ApplySlopeSlide()
    {
        if (!isGrounded)
        {
            return;
        }

        float slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
        if (slopeAngle < slideSlopeAngle)
        {
            return;
        }

        Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
        if (slideDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        slideDirection.Normalize();
        float slopeFactor = Mathf.InverseLerp(slideSlopeAngle, 89f, slopeAngle);
        physicsBody.AddForce(slideDirection * (slideForce * slopeFactor), ForceMode.Acceleration);
    }

    private void ApplyBalanceTorques()
    {
        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle >= fallenTiltAngle)
        {
            timeSinceLargeTilt += Time.fixedDeltaTime;
        }
        else
        {
            timeSinceLargeTilt = 0f;
        }

        float recoveryMultiplier = timeSinceLargeTilt >= recoveryDelay ? recoveryTorqueMultiplier : 1f;
        Vector3 uprightAxis = Vector3.Cross(transform.up, Vector3.up);
        Vector3 tiltAngularVelocity = Vector3.ProjectOnPlane(physicsBody.angularVelocity, Vector3.up);

        Vector3 correctiveTorque =
            (uprightAxis * (uprightTorque * recoveryMultiplier)) -
            (tiltAngularVelocity * (uprightDamping * recoveryMultiplier));

        physicsBody.AddTorque(correctiveTorque, ForceMode.Acceleration);
    }

    private void ClampVerticalVelocity()
    {
        Vector3 velocity = physicsBody.linearVelocity;
        if (velocity.y < terminalVelocity)
        {
            velocity.y = terminalVelocity;
            physicsBody.linearVelocity = velocity;
        }

        lastVerticalVelocity = physicsBody.linearVelocity.y;
    }

    private bool CanSprint()
    {
        if (sprintAction == null || !sprintAction.IsPressed())
        {
            return false;
        }

        return moveInput.sqrMagnitude > 0.01f;
    }

    private void HandleThrowInput()
    {
        if (throwAction == null || !throwAction.WasPressedThisFrame())
        {
            return;
        }

        TryThrowHeldObject();
    }

    private void UpdateInteractionHighlight()
    {
        InteractableOutlineHighlight nextHighlight = null;
        if (heldRigidbody == null
            && activeCarryableObject == null
            && TryGetInteractionHit(out RaycastHit hit)
            && TryGetInteractionHighlight(hit, out InteractableOutlineHighlight highlight))
        {
            nextHighlight = highlight;
        }

        if (currentInteractionHighlight == nextHighlight)
        {
            return;
        }

        ClearInteractionHighlight();
        currentInteractionHighlight = nextHighlight;

        if (currentInteractionHighlight != null)
        {
            currentInteractionHighlight.Configure(
                interactableOutlineColor,
                interactableOutlineWidth,
                interactableOutlineMode);
            currentInteractionHighlight.SetHighlighted(true);
        }
    }

    private void ClearInteractionHighlight()
    {
        if (currentInteractionHighlight == null)
        {
            return;
        }

        currentInteractionHighlight.SetHighlighted(false);
        currentInteractionHighlight = null;
    }

    private void HandleInteractionInput()
    {
        if (!IsInteractPressedThisFrame())
        {
            return;
        }

        if (heldRigidbody != null)
        {
            DropHeldObject();
            return;
        }

        if (activeCarryableObject != null)
        {
            activeCarryableObject.ReleaseCarrier(this);
            return;
        }

        if (!TryGetInteractionHit(out RaycastHit hit))
        {
            return;
        }

        if (TryGetInteractable(hit.collider, out IPlayerInteractable interactable)
            && interactable.CanInteract(this))
        {
            interactable.Interact(this);
            return;
        }

        if (hit.rigidbody != null)
        {
            TryPickUp(hit.rigidbody);
        }
    }

    private bool TryThrowHeldObject()
    {
        if (heldRigidbody == null)
        {
            return false;
        }

        SetHeldObjectCollisionIgnored(true);

        Rigidbody body = heldRigidbody;
        Transform originalParent = heldOriginalParent;
        Collider[] thrownColliders = heldColliders;
        CarryableObject carryableObject = activeCarryableObject;
        Vector3 releaseVelocity = currentHorizontalVelocity * throwVelocityInheritance;
        Vector3 throwDirection = (GetCharacterForwardDirection() + (Vector3.up * throwUpwardRatio)).normalized;
        Vector3 throwImpulse = throwDirection * throwForce;

        if (TryGetSpawnedNetworkObject(body, out NetworkObject targetNetworkObject) && !IsServer)
        {
            // 던지기도 서버가 부모 해제와 힘 적용을 처리해야 다른 클라이언트에 동기화된다.
            RequestThrowNetworkObjectServerRpc(targetNetworkObject, releaseVelocity, throwImpulse);
        }

        SetHeldObjectParent(body, originalParent, true);
        body.useGravity = heldUseGravity;
        body.isKinematic = heldIsKinematic;
        body.interpolation = heldInterpolation;
        body.collisionDetectionMode = heldCollisionMode;
        body.linearVelocity = releaseVelocity;

        // 던질 때 플레이어 속도를 일부 이어받고 전방으로 즉시 힘을 준다.
        body.AddForce(throwImpulse, ForceMode.Impulse);

        ClearHeldObjectState();
        ClearActiveCarryRule();
        carryableObject?.NotifyCarrierReleased(this);

        if (throwCollisionIgnoreTime > 0f && physicsCollider != null && thrownColliders != null)
        {
            StartCoroutine(RestoreThrownCollision(thrownColliders, throwCollisionIgnoreTime));
        }
        else
        {
            SetCollisionIgnoredForColliders(thrownColliders, false);
        }

        return true;
    }

    [ServerRpc]
    private void RequestPickUpNetworkObjectServerRpc(NetworkObjectReference targetReference)
    {
        if (!targetReference.TryGet(out NetworkObject targetNetworkObject))
        {
            return;
        }

        Rigidbody targetBody = targetNetworkObject.GetComponent<Rigidbody>();
        if (targetBody == null)
        {
            return;
        }

        CarryableObject carryableObject = targetNetworkObject.GetComponent<CarryableObject>();
        TryPickUp(targetBody, carryableObject);
    }

    [ServerRpc]
    private void RequestDropNetworkObjectServerRpc(NetworkObjectReference targetReference, Vector3 releaseVelocity)
    {
        ReleaseNetworkHeldObject(targetReference, releaseVelocity, Vector3.zero, false);
    }

    [ServerRpc]
    private void RequestThrowNetworkObjectServerRpc(NetworkObjectReference targetReference, Vector3 releaseVelocity, Vector3 throwImpulse)
    {
        ReleaseNetworkHeldObject(targetReference, releaseVelocity, throwImpulse, true);
    }

    private void ReleaseNetworkHeldObject(
        NetworkObjectReference targetReference,
        Vector3 releaseVelocity,
        Vector3 throwImpulse,
        bool shouldThrow)
    {
        if (!targetReference.TryGet(out NetworkObject targetNetworkObject))
        {
            return;
        }

        Rigidbody body = targetNetworkObject.GetComponent<Rigidbody>();
        if (body == null || body != heldRigidbody)
        {
            return;
        }

        SetHeldObjectCollisionIgnored(false);
        SetHeldObjectParent(body, heldOriginalParent, true);
        body.useGravity = heldUseGravity;
        body.isKinematic = heldIsKinematic;
        body.interpolation = heldInterpolation;
        body.collisionDetectionMode = heldCollisionMode;
        body.linearVelocity = releaseVelocity;

        if (shouldThrow)
        {
            body.AddForce(throwImpulse, ForceMode.Impulse);
        }

        CarryableObject carryableObject = activeCarryableObject;
        ClearHeldObjectState();
        ClearActiveCarryRule();
        carryableObject?.NotifyCarrierReleased(this);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void UpdateHeldObjectPoseServerRpc(
        NetworkObjectReference targetReference,
        Vector3 targetPosition,
        Quaternion targetRotation)
    {
        if (!targetReference.TryGet(out NetworkObject targetNetworkObject))
        {
            return;
        }

        Rigidbody body = targetNetworkObject.GetComponent<Rigidbody>();
        if (body == null || body != heldRigidbody)
        {
            return;
        }

        // 클라이언트가 보는 HoldPoint 위치를 서버에 반영해 서버 보간 지연을 줄인다.
        body.transform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private bool TryGetInteractionHit(out RaycastHit hit)
    {
        // 상호작용은 몸체 회전보다 플레이어가 바라보는 카메라 방향을 우선한다.
        Vector3 rayDirection = GetFacingDirection();
        Vector3 rayOrigin = transform.position + (Vector3.up * 0.9f) + (rayDirection * 0.1f);

        if (interactionRadius > 0f)
        {
            return Physics.SphereCast(
                rayOrigin,
                interactionRadius,
                rayDirection,
                out hit,
                interactionDistance,
                interactionLayers,
                QueryTriggerInteraction.Ignore);
        }

        return Physics.Raycast(
            rayOrigin,
            rayDirection,
            out hit,
            interactionDistance,
            interactionLayers,
            QueryTriggerInteraction.Ignore);
    }

    private bool TryGetInteractionHighlight(RaycastHit hit, out InteractableOutlineHighlight highlight)
    {
        if (TryGetInteractable(hit.collider, out IPlayerInteractable interactable)
            && interactable.CanInteract(this))
        {
            highlight = GetOrAddInteractionHighlight(hit.collider);
            return highlight != null;
        }

        if (CanHighlightRigidbody(hit.rigidbody))
        {
            highlight = GetOrAddInteractionHighlight(hit.rigidbody.gameObject);
            return highlight != null;
        }

        highlight = null;
        return false;
    }

    private bool CanHighlightRigidbody(Rigidbody target)
    {
        return target != null
            && target != heldRigidbody
            && !target.isKinematic
            && target.mass <= maxCarryMass
            && target.GetComponentInParent<PlayerController>() == null;
    }

    private InteractableOutlineHighlight GetOrAddInteractionHighlight(Collider source)
    {
        MonoBehaviour[] behaviours = source.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerInteractable)
            {
                GameObject targetObject = behaviours[i].gameObject;
                InteractableOutlineHighlight highlight = targetObject.GetComponent<InteractableOutlineHighlight>();
                if (highlight == null)
                {
                    highlight = targetObject.AddComponent<InteractableOutlineHighlight>();
                }

                return highlight;
            }
        }

        return null;
    }

    private static InteractableOutlineHighlight GetOrAddInteractionHighlight(GameObject targetObject)
    {
        InteractableOutlineHighlight highlight = targetObject.GetComponent<InteractableOutlineHighlight>();
        if (highlight == null)
        {
            highlight = targetObject.AddComponent<InteractableOutlineHighlight>();
        }

        return highlight;
    }

    private void OnCollisionEnter(Collision collision)
    {
        EvaluateImpactCollision(collision);
    }

    private void EvaluateImpactCollision(Collision collision)
    {
        if (!CanProcessInput() || physicsBody == null || physicsBody.isKinematic || collision.contactCount == 0)
        {
            return;
        }

        ContactPoint contact = collision.GetContact(0);
        Vector3 impactDirection = -contact.normal;
        float relativeSpeed = collision.relativeVelocity.magnitude;

        PlayerController otherPlayer = collision.collider.GetComponentInParent<PlayerController>();
        if (otherPlayer != null && otherPlayer != this && relativeSpeed > 0.5f)
        {
            ApplyExternalImpulse(impactDirection.normalized * playerCollisionImpact, contact.point);
        }

        Rigidbody otherBody = collision.rigidbody;
        if (otherBody == null)
        {
            return;
        }

        if (relativeSpeed >= rigidbodyImpactSpeedThreshold)
        {
            ApplyExternalImpulse(collision.relativeVelocity * 0.08f, contact.point);
        }

        bool isHeavyDownwardImpact =
            contact.normal.y < -0.2f &&
            otherBody.mass >= heavyObjectMassThreshold &&
            otherBody.linearVelocity.y <= -heavyObjectDownwardSpeedThreshold;

        if (isHeavyDownwardImpact)
        {
            ApplyExternalImpulse(Vector3.down * (otherBody.mass * 0.1f), contact.point);
        }
    }

    private bool TryPickUp(Rigidbody target)
    {
        return TryPickUp(target, null);
    }

    private bool TryPickUp(Rigidbody target, CarryableObject carryableObject)
    {
        if (target == null || target == heldRigidbody)
        {
            return false;
        }

        if (target.isKinematic || (carryableObject == null && target.mass > maxCarryMass))
        {
            return false;
        }

        EnsureHoldPointExists();
        if (holdPoint == null)
        {
            return false;
        }

        heldRigidbody = target;
        heldOriginalParent = target.transform.parent;
        heldUseGravity = target.useGravity;
        heldIsKinematic = target.isKinematic;
        heldInterpolation = target.interpolation;
        heldCollisionMode = target.collisionDetectionMode;
        heldColliders = target.GetComponentsInChildren<Collider>(true);
        activeCarryableObject = carryableObject;
        if (carryableObject != null)
        {
            activeCarrySpeedMultiplier = Mathf.Clamp(carryableObject.MoveSpeedMultiplier, 0.1f, 1f);
            activeCarryBlocksJump = carryableObject.BlockJumpWhileCarrying;
        }

        if (TryGetSpawnedNetworkObject(target, out NetworkObject targetNetworkObject) && !IsServer)
        {
            // 네트워크 오브젝트 부모 변경은 서버만 가능하므로 서버에 잡기 처리를 요청한다.
            RequestPickUpNetworkObjectServerRpc(targetNetworkObject);
        }

        target.linearVelocity = Vector3.zero;
        target.angularVelocity = Vector3.zero;
        target.useGravity = false;
        target.isKinematic = true;
        target.interpolation = RigidbodyInterpolation.None;
        target.collisionDetectionMode = CollisionDetectionMode.Discrete;
        SetHeldObjectParent(target, holdPoint, true);

        UpdateHeldObjectPose();
        SetHeldObjectCollisionIgnored(true);
        return true;
    }

    private void DropHeldObject()
    {
        if (heldRigidbody == null)
        {
            return;
        }

        SetHeldObjectCollisionIgnored(false);

        Rigidbody body = heldRigidbody;
        Transform originalParent = heldOriginalParent;
        CarryableObject carryableObject = activeCarryableObject;

        if (TryGetSpawnedNetworkObject(body, out NetworkObject targetNetworkObject) && !IsServer)
        {
            // 클라이언트는 직접 re-parent하지 않고 서버가 최종 부모/물리 상태를 복구한다.
            RequestDropNetworkObjectServerRpc(targetNetworkObject, currentHorizontalVelocity);
        }

        SetHeldObjectParent(body, originalParent, true);
        body.useGravity = heldUseGravity;
        body.isKinematic = heldIsKinematic;
        body.interpolation = heldInterpolation;
        body.collisionDetectionMode = heldCollisionMode;
        body.linearVelocity = currentHorizontalVelocity;

        ClearHeldObjectState();
        ClearActiveCarryRule();
        carryableObject?.NotifyCarrierReleased(this);
    }

    private void ForceDropHeldObject()
    {
        if (heldRigidbody == null)
        {
            return;
        }

        SetHeldObjectCollisionIgnored(false);

        Rigidbody body = heldRigidbody;
        Transform originalParent = heldOriginalParent;
        CarryableObject carryableObject = activeCarryableObject;

        SetHeldObjectParent(body, originalParent, true);
        body.useGravity = heldUseGravity;
        body.isKinematic = heldIsKinematic;
        body.interpolation = heldInterpolation;
        body.collisionDetectionMode = heldCollisionMode;

        ClearHeldObjectState();
        ClearActiveCarryRule();
        carryableObject?.NotifyCarrierReleased(this);
    }

    private void ClearHeldObjectState()
    {
        heldRigidbody = null;
        heldOriginalParent = null;
        heldColliders = Array.Empty<Collider>();
    }

    private void ClearActiveCarryRule()
    {
        activeCarryableObject = null;
        activeCarrySpeedMultiplier = 1f;
        activeCarryBlocksJump = false;
    }

    private void UpdateHeldObjectPose()
    {
        if (heldRigidbody == null || holdPoint == null)
        {
            return;
        }

        heldRigidbody.transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
    }

    private void SendHeldObjectPoseToServer()
    {
        if (IsServer || heldRigidbody == null || holdPoint == null)
        {
            return;
        }

        if (!TryGetSpawnedNetworkObject(heldRigidbody, out NetworkObject targetNetworkObject))
        {
            return;
        }

        // 로컬 HoldPoint 기준 위치를 서버에 보내 원격 서버의 플레이어 보간 지연을 피한다.
        UpdateHeldObjectPoseServerRpc(targetNetworkObject, holdPoint.position, holdPoint.rotation);
    }

    private void SetHeldObjectParent(Rigidbody body, Transform parent, bool worldPositionStays)
    {
        if (!TryGetSpawnedNetworkObject(body, out NetworkObject targetNetworkObject))
        {
            body.transform.SetParent(parent, worldPositionStays);
            return;
        }

        if (!IsServer)
        {
            return;
        }

        NetworkObject parentNetworkObject = parent != null
            ? parent.GetComponentInParent<NetworkObject>()
            : null;

        if (parentNetworkObject != null && parentNetworkObject.IsSpawned)
        {
            targetNetworkObject.TrySetParent(parentNetworkObject, worldPositionStays);
        }
        else
        {
            targetNetworkObject.TryRemoveParent(worldPositionStays);
        }
    }

    private static bool TryGetSpawnedNetworkObject(Rigidbody body, out NetworkObject targetNetworkObject)
    {
        targetNetworkObject = body != null ? body.GetComponent<NetworkObject>() : null;
        return targetNetworkObject != null && targetNetworkObject.IsSpawned;
    }

    private void EnsureHoldPointExists()
    {
        if (holdPoint != null)
        {
            return;
        }

        Transform existing = transform.Find("HoldPoint");
        if (existing != null)
        {
            holdPoint = existing;
        }
        else
        {
            GameObject holdPointObject = new GameObject("HoldPoint");
            holdPoint = holdPointObject.transform;
            holdPoint.SetParent(transform, false);
        }

        holdPoint.localPosition = holdPointLocalOffset;
        holdPoint.localRotation = Quaternion.identity;
    }

    private void SetHeldObjectCollisionIgnored(bool shouldIgnore)
    {
        SetCollisionIgnoredForColliders(heldColliders, shouldIgnore);
    }

    private IEnumerator RestoreThrownCollision(Collider[] thrownColliders, float delay)
    {
        yield return new WaitForSeconds(delay);
        SetCollisionIgnoredForColliders(thrownColliders, false);
    }

    private void SetCollisionIgnoredForColliders(Collider[] targetColliders, bool shouldIgnore)
    {
        if (targetColliders == null || physicsCollider == null)
        {
            return;
        }

        for (int i = 0; i < targetColliders.Length; i++)
        {
            Collider heldCollider = targetColliders[i];
            if (heldCollider != null)
            {
                Physics.IgnoreCollision(physicsCollider, heldCollider, shouldIgnore);
            }
        }
    }

    private static bool TryGetInteractable(Collider source, out IPlayerInteractable interactable)
    {
        MonoBehaviour[] behaviours = source.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerInteractable candidate)
            {
                interactable = candidate;
                return true;
            }
        }

        interactable = null;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector3 spherePosition = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawWireSphere(spherePosition, groundedRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + (transform.forward * interactionDistance));

        if (holdPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(holdPoint.position, 0.1f);
        }

        Gizmos.color = Color.blue;
        Vector3 normalOrigin = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawLine(normalOrigin, normalOrigin + (groundNormal.normalized * 1.2f));
    }
}

public interface IPlayerInteractable
{
    bool CanInteract(PlayerController controller);
    void Interact(PlayerController controller);
}
