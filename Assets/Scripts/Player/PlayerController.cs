using System;
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

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    public Vector2 MoveInput => moveInput;
    private Vector2 moveInput;

    private float currentCarrySpeedMultiplier = 1f;

    public void SetCarrySpeedMultiplier(float multiplier)
    {
        currentCarrySpeedMultiplier = multiplier;
    }

    public void ResetCarrySpeedMultiplier()
    {
        currentCarrySpeedMultiplier = 1f;
    }
    
    public Vector3? OverrideFacingDirection { get; set; } = null;

    private Vector3 currentHorizontalVelocity;
    private Vector3 groundNormal = Vector3.up;
    private bool isGrounded;
    private bool jumpQueued;
    private bool isPhysicsOwner;
    private float lastVerticalVelocity;
    private float timeSinceLargeTilt;

    private Transform currentCheckpoint;
    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;

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
        ResolveInputActions();
        EnsureLayerMasksConfigured();
        EnsureRuntimePhysicsComponents();
        UpdatePhysicsOwnershipState(force: true);

        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;
    }

    private void OnEnable()
    {
        ResolveInputActions();
        EnsureLayerMasksConfigured();
    }

    private void OnDisable()
    {
    }

    private void Update()
    {
        UpdatePhysicsOwnershipState();

        if (!CanProcessInput())
        {
            return;
        }

        if (playerInput == null || physicsBody == null)
        {
            return;
        }

        ReadInput();
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
    }

    private void EnsureLayerMasksConfigured()
    {
        if (groundLayers.value == 0)
        {
            groundLayers = ~0;
        }

        int selfLayerMask = 1 << gameObject.layer;
        groundLayers &= ~selfLayerMask;
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

        if (!shouldJump || !isGrounded)
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
        float targetSpeed = (CanSprint() ? sprintSpeed : walkSpeed) * inputMagnitude * currentCarrySpeedMultiplier;
        float acceleration = isGrounded ? movementAcceleration : airAcceleration;

        Vector3 velocity = physicsBody.linearVelocity;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 desiredVelocity = moveDirection * targetSpeed;
        Vector3 nextPlanarVelocity = Vector3.MoveTowards(planarVelocity, desiredVelocity, acceleration * Time.fixedDeltaTime);

        physicsBody.linearVelocity = nextPlanarVelocity + (Vector3.up * velocity.y);
        currentHorizontalVelocity = nextPlanarVelocity;

        if (OverrideFacingDirection.HasValue && OverrideFacingDirection.Value.sqrMagnitude > 0.001f)
        {
            // 상호작용(예: 무거운 물체 끌기) 시에는 이동 방향 대신 지정된 방향을 바라본다.
            ApplyTurnTorque(OverrideFacingDirection.Value);
        }
        else if (moveDirection.sqrMagnitude > 0.0001f)
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


    public void SetCheckpoint(Transform checkpointPoint)
    {
        if (checkpointPoint != null)
        {
            currentCheckpoint = checkpointPoint;
        }
    }

    public void RespawnAtCheckpoint()
    {
        Vector3 spawnPosition = currentCheckpoint != null ? currentCheckpoint.position : initialSpawnPosition;
        Quaternion spawnRotation = currentCheckpoint != null ? currentCheckpoint.rotation : initialSpawnRotation;

        // 물리 연산 초기화 및 위치 즉시 이동
        if (physicsBody != null)
        {
            physicsBody.position = spawnPosition;
            physicsBody.rotation = spawnRotation;
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
        }
        
        transform.position = spawnPosition;
        transform.rotation = spawnRotation;

        currentHorizontalVelocity = Vector3.zero;
        lastVerticalVelocity = 0f;

        // 낙사 시 들고 있던 물건을 놓도록 PlayerInteractor가 있다면 비활성화 후 활성화하거나 내부 상태를 초기화할 수 있음
        PlayerInteractor interactor = GetComponent<PlayerInteractor>();
        if (interactor != null)
        {
            // PlayerInteractor의 OnDisable에서 놓기가 호출되므로 껐다 켜서 들고 있던 걸 초기화
            interactor.enabled = false;
            interactor.enabled = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector3 spherePosition = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawWireSphere(spherePosition, groundedRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + (transform.forward * 2.2f));

        Gizmos.color = Color.blue;
        Vector3 normalOrigin = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawLine(normalOrigin, normalOrigin + (groundNormal.normalized * 1.2f));
    }
}
