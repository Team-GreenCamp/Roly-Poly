using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(NetworkObject))]
public partial class PlayerController : NetworkBehaviour
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

    [Header("Landing Stability")]
    [SerializeField] private float landingSpeedPreserveDuration = 0.18f;
    [SerializeField] private float landingSpeedPreserveRatio = 0.9f;
    [SerializeField] private float landingTiltAngularDamping = 18f;

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
    [SerializeField] private float groundedContactGraceTime = 0.08f;
    [SerializeField] private float minGroundContactNormalY = 0.5f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Surface Control")]
    [SerializeField] private bool useLowFrictionColliderMaterial = true;
    [SerializeField] private float idleGroundFriction = 1f;
    [SerializeField] private float idleFrictionInputThreshold = 0.05f;

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

    public Vector3? OverrideFacingDirection { get; set; } = null;

    private Vector3 currentHorizontalVelocity;
    private Vector3 groundNormal = Vector3.up;
    private bool isGrounded;
    private bool jumpQueued;
    private bool isPhysicsOwner;
    private bool gameplayInputEnabled = true;
    private float groundedContactTimer;
    private float lastVerticalVelocity;
    private float timeSinceLargeTilt;
    private float landingSpeedPreserveTimer;
    private Vector3 landingPreservedPlanarVelocity;
    private PhysicsMaterial lowFrictionColliderMaterial;
    private PhysicsMaterial groundGripColliderMaterial;

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
        EnsurePlayerLayerConfigured();
        EnsureLayerMasksConfigured();
        EnsureRuntimePhysicsComponents();
        UpdatePhysicsOwnershipState(force: true);

        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;
    }

    private void OnEnable()
    {
        ResolveInputActions();
        EnsurePlayerLayerConfigured();
        EnsureLayerMasksConfigured();
    }

    private void Update()
    {
        UpdatePhysicsOwnershipState();

        if (!CanProcessInput())
        {
            return;
        }

        if (!gameplayInputEnabled)
        {
            ClearGameplayInputState();
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

        if (!gameplayInputEnabled)
        {
            StopGameplayMotion();
            return;
        }

        UpdateGroundedState();
        UpdateColliderSurfaceMaterial();
        ApplyCustomGravity();
        ApplyJump();
        UpdateMovement();
        ApplySlopeSlide();
        ApplyLandingTiltDamping();
        ApplyBalanceTorques();
        ClampVerticalVelocity();
        UpdateLandingStabilityTimers();
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
        landingSpeedPreserveDuration = Mathf.Max(0f, landingSpeedPreserveDuration);
        landingSpeedPreserveRatio = Mathf.Clamp01(landingSpeedPreserveRatio);
        landingTiltAngularDamping = Mathf.Max(0f, landingTiltAngularDamping);
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
        groundedContactGraceTime = Mathf.Max(0f, groundedContactGraceTime);
        minGroundContactNormalY = Mathf.Clamp01(minGroundContactNormalY);
        idleGroundFriction = Mathf.Max(0f, idleGroundFriction);
        idleFrictionInputThreshold = Mathf.Max(0f, idleFrictionInputThreshold);
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

    private void EnsurePlayerLayerConfigured()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer < 0 || gameObject.layer == playerLayer)
        {
            return;
        }

        // 플레이어가 Default로 생성되면 Ground Mask에서 Default 바닥까지 빠지므로 루트만 Player 레이어로 보정한다.
        gameObject.layer = playerLayer;
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
        EnsureLowFrictionColliderMaterial();
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

}
