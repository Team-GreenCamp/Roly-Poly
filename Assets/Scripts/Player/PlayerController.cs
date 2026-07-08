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
    [Tooltip("점프 입력 버퍼링 시간(초). 착지 직전에 누른 점프 입력을 이 시간 동안 기억했다가 착지하면 실행합니다.")]
    [SerializeField] private float jumpBufferTime = 0.12f;
    [Tooltip("코요테 타임(초). 발판에서 벗어난 직후 이 시간 동안은 여전히 점프할 수 있습니다.")]
    [SerializeField] private float coyoteTime = 0.1f;

    [Header("Landing Stability")]
    [SerializeField] private float landingSpeedPreserveDuration = 0.18f;
    [SerializeField] private float landingSpeedPreserveRatio = 0.9f;
    [SerializeField] private float landingTiltAngularDamping = 18f;

    [Header("Step Assist")]
    [SerializeField] private bool useStepAssist = true;
    [SerializeField] private float maxStepHeight = 0.35f;
    [SerializeField] private float stepCheckDistance = 0.35f;
    [SerializeField] private float stepLiftSpeed = 3.5f;
    [SerializeField] private float stepProbeRadius = 0.08f;
    [SerializeField] private LayerMask stepAssistLayers = 0;

    [Header("Balance")]
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.35f, 0f);
    [Tooltip("평상시 몸을 꼿꼿하게 세우는 힘. 높을수록 걷기 중 잔흔들림이 없어지고, 피격 시 휘청과의 대비가 커집니다.")]
    [SerializeField] private float uprightTorque = 150f;
    [SerializeField] private float uprightDamping = 16f;
    [SerializeField] private float fallenTiltAngle = 35f;
    [SerializeField] private float recoveryDelay = 1f;
    [SerializeField] private float recoveryTorqueMultiplier = 2.5f;
    [SerializeField] private float knockdownMinimumDuration = 0.45f;
    [SerializeField] private float knockdownUprightAngle = 12f;
    [SerializeField] private float knockdownRecoveryAngularSpeed = 1.2f;
    [Tooltip("정지 중 외부 충격으로 생긴 yaw 회전이 멈추는 감쇠 속도. (이동 중 회전은 rotationSpeed로 직접 제어)")]
    [SerializeField] private float turnDamping = 8f;
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
    [Tooltip("일반 점프/스톰프 바운스 착지 속도보다 이만큼 더 빠르게 떨어져야 넉다운됩니다(m/s). 평범한 점프 착지로 스스로 넘어지는 것을 막습니다.")]
    [SerializeField] private float landingKnockdownSpeedMargin = 2f;
    [SerializeField] private float landingTorqueMultiplier = 0.2f;
    [SerializeField] private float externalImpactForceMultiplier = 1f;
    [Tooltip("피격 시 휘청(기울기 토크) 배율. 피격 피드백의 체감을 좌우합니다.")]
    [SerializeField] private float externalImpactTorqueMultiplier = 0.55f;
    [SerializeField] private float knockdownImpactSpeedThreshold = 7f;
    [SerializeField] private float knockdownThrownObjectSpeedThreshold = 5.5f;
    [SerializeField] private float knockdownFallingObjectSpeedThreshold = 5f;

    [Header("Slope")]
    [SerializeField] private float slideSlopeAngle = 32f;
    [SerializeField] private float slideForce = 18f;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    public Vector2 MoveInput => moveInput;
    public bool IsKnockedDown => isKnockedDown;
    // 소유자(또는 네트워크 세션이 없는 단일 플레이) 인스턴스에서만 입력을 처리해야 하는지 여부.
    // PlayerInteractor / PlayerClimber 등 비-NetworkBehaviour 입력 스크립트가 공유한다.
    public bool HasInputAuthority => CanProcessInput();
    private Vector2 moveInput;

    private float currentCarrySpeedMultiplier = 1f;

    public Vector3? OverrideFacingDirection { get; set; } = null;

    private Vector3 currentHorizontalVelocity;
    private Vector3 currentMoveDirection;
    private Vector3 groundNormal = Vector3.up;
    private bool isGrounded;
    private float jumpBufferTimer; // >0이면 최근에 누른 점프 입력이 아직 유효함(버퍼)
    private float coyoteTimer;     // >0이면 최근까지 접지 상태였음(코요테 타임)
    private bool isPhysicsOwner;
    private bool gameplayInputEnabled = true;
    private bool isKnockedDown;
    private float groundedContactTimer;
    private float lastVerticalVelocity;
    private float timeSinceLargeTilt;
    private float knockdownTimer;
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

        // 찌부 비주얼은 소유권과 무관하게 모든 인스턴스에서 갱신한다.(원격 플레이어도 눌린 모습이 보여야 함)
        UpdateSquashVisual();

        // 애니메이션도 모든 인스턴스에서 구동한다.(소유자가 상태 판정 → 동기화 → 전 클라 재생)
        DriveAnimation();

        // 붙잡힌 플레이어는 모든 머신이 각자 로컬에서 공격자에 부착한다(공격자 화면 지연 0).
        UpdateGrabbedFollow();

        if (!CanProcessInput())
        {
            return;
        }

        if (!gameplayInputEnabled)
        {
            ClearGameplayInputState();
            return;
        }

        if (isKnockedDown)
        {
            ClearGameplayInputState();
            return;
        }

        if (IsStunned)
        {
            // 머리를 밟혀 찌부/스턴된 동안에는 조작 불가.
            ClearGameplayInputState();
            return;
        }

        if (IsGrabbed)
        {
            // 붙잡힌 동안에는 조작 불가(소유자가 공격자 앞으로 끌려감).
            ClearGameplayInputState();
            return;
        }

        if (playerInput == null || physicsBody == null)
        {
            return;
        }

        UpdateGrappleInput();
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

        if (IsGrabbed)
        {
            // 붙잡힌 피해자: 공격자 몸 앞으로 이동시키고 일반 이동/물리는 생략.
            UpdateGrabbedVictimMotion();
            return;
        }

        UpdateGroundedState();
        UpdateColliderSurfaceMaterial();
        ApplyCustomGravity();

        if (isKnockedDown)
        {
            ApplySlopeSlide();
            ApplyBalanceTorques();
            UpdateKnockdownRecovery();
            ClampVerticalVelocity();
            return;
        }

        if (IsStunned)
        {
            // 스턴 중에는 이동/점프/돌진 입력을 반영하지 않되, 물리(중력·균형)는 유지한다.
            ApplySlopeSlide();
            ApplyBalanceTorques();
            ClampVerticalVelocity();
            return;
        }

        ApplyJump();
        ApplyDash();
        UpdateMovement();
        ApplyStepAssist(currentMoveDirection);
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
        jumpBufferTime = Mathf.Max(0f, jumpBufferTime);
        coyoteTime = Mathf.Max(0f, coyoteTime);
        landingSpeedPreserveDuration = Mathf.Max(0f, landingSpeedPreserveDuration);
        landingSpeedPreserveRatio = Mathf.Clamp01(landingSpeedPreserveRatio);
        landingTiltAngularDamping = Mathf.Max(0f, landingTiltAngularDamping);
        maxStepHeight = Mathf.Max(0f, maxStepHeight);
        stepCheckDistance = Mathf.Max(0.01f, stepCheckDistance);
        stepLiftSpeed = Mathf.Max(0f, stepLiftSpeed);
        stepProbeRadius = Mathf.Max(0.01f, stepProbeRadius);
        uprightTorque = Mathf.Max(0f, uprightTorque);
        uprightDamping = Mathf.Max(0f, uprightDamping);
        fallenTiltAngle = Mathf.Clamp(fallenTiltAngle, 1f, 89f);
        recoveryDelay = Mathf.Max(0f, recoveryDelay);
        recoveryTorqueMultiplier = Mathf.Max(1f, recoveryTorqueMultiplier);
        knockdownMinimumDuration = Mathf.Max(0f, knockdownMinimumDuration);
        knockdownUprightAngle = Mathf.Clamp(knockdownUprightAngle, 1f, fallenTiltAngle);
        knockdownRecoveryAngularSpeed = Mathf.Max(0f, knockdownRecoveryAngularSpeed);
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
        knockdownImpactSpeedThreshold = Mathf.Max(0f, knockdownImpactSpeedThreshold);
        knockdownThrownObjectSpeedThreshold = Mathf.Max(0f, knockdownThrownObjectSpeedThreshold);
        knockdownFallingObjectSpeedThreshold = Mathf.Max(0f, knockdownFallingObjectSpeedThreshold);
        slideSlopeAngle = Mathf.Clamp(slideSlopeAngle, 1f, 89f);
        slideForce = Mathf.Max(0f, slideForce);

        stompMinDownSpeed = Mathf.Max(0f, stompMinDownSpeed);
        stompBounceHeight = Mathf.Max(0.1f, stompBounceHeight);
        stompHeadHeight = Mathf.Max(0f, stompHeadHeight);
        stompDownImpulse = Mathf.Max(0f, stompDownImpulse);
        stompStunDuration = Mathf.Max(0.05f, stompStunDuration);
        stompCooldown = Mathf.Max(0f, stompCooldown);
        squashScaleY = Mathf.Clamp(squashScaleY, 0.05f, 1f);
        squashScaleXZ = Mathf.Max(1f, squashScaleXZ);
        squashLerpSpeed = Mathf.Max(0.01f, squashLerpSpeed);
        dashForce = Mathf.Max(0f, dashForce);
        dashCooldown = Mathf.Max(0f, dashCooldown);
        dashWindow = Mathf.Max(0f, dashWindow);
        dashShoveStrength = Mathf.Max(0f, dashShoveStrength);

        // 착지 넉다운 임계값은 '일반 점프/스톰프 바운스 착지 속도'보다 항상 높게 강제한다.
        // (그렇지 않으면 코드 기본값 기준 제자리 점프 착지 속도 ≈ 11m/s가 임계값 10을 넘겨 매 점프마다 스스로 넘어진다.)
        landingKnockdownSpeedMargin = Mathf.Max(0f, landingKnockdownSpeedMargin);
        float jumpLandingSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity * fallGravityMultiplier);
        float stompLandingSpeed = Mathf.Sqrt(stompBounceHeight * -2f * gravity * fallGravityMultiplier);
        float minLandingKnockdownSpeed = Mathf.Max(jumpLandingSpeed, stompLandingSpeed) + landingKnockdownSpeedMargin;
        landingImpactThreshold = Mathf.Max(landingImpactThreshold, minLandingKnockdownSpeed);

        animWalkThreshold = Mathf.Max(0f, animWalkThreshold);
        animRunThreshold = Mathf.Max(animWalkThreshold, animRunThreshold);
        animCrossFadeDuration = Mathf.Max(0f, animCrossFadeDuration);
        eyeReactionWeightLerpSpeed = Mathf.Max(0.01f, eyeReactionWeightLerpSpeed);
        blinkIntervalMin = Mathf.Max(0.1f, blinkIntervalMin);
        blinkIntervalMax = Mathf.Max(blinkIntervalMin, blinkIntervalMax);
        blinkDuration = Mathf.Max(0.05f, blinkDuration);
    }

    private void EnsureLayerMasksConfigured()
    {
        if (groundLayers.value == 0)
        {
            groundLayers = ~0;
        }

        if (stepAssistLayers.value == 0)
        {
            stepAssistLayers = groundLayers;
        }

        int selfLayerMask = 1 << gameObject.layer;
        groundLayers &= ~selfLayerMask;
        stepAssistLayers &= ~selfLayerMask;
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
            // Quirky 동물 캐릭터(키 ~0.5–1.0m, 4족 체형) 기준 공통 캡슐.
            // (과거 오뚝이 시절 1.75m 인간형 캡슐은 동물 머리 위 허공에 판정이 뜨는 문제가 있었음)
            physicsCollider = gameObject.AddComponent<CapsuleCollider>();
            physicsCollider.height = 0.9f;
            physicsCollider.radius = 0.35f;
            physicsCollider.center = new Vector3(0f, 0.45f, 0f);
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
        ResolveDashAction();
        ResolveGrappleActions();
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
