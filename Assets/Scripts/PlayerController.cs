using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerController : MonoBehaviour
{
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");
    private static readonly int JumpHash = Animator.StringToHash("Jump");

    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform cameraRoot;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float terminalVelocity = -50f;
    [SerializeField] private float fallGravityMultiplier = 2f;

    [Header("Animation")]
    [SerializeField] private float animationDampTime = 0.15f;

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 0.12f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Ground Check")]
    [SerializeField] private float groundedOffset = 0.1f;
    [SerializeField] private float groundedRadius = 0.25f;
    [SerializeField] private LayerMask groundLayers = 1; // Default layer only

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    private Vector2 moveInput;
    private Vector2 lookInput;
    private float verticalVelocity;
    private float targetYaw;
    private float cameraPitch;
    private bool isGrounded;
    private bool jumpQueued;

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
        ResolveInputActions();

        groundLayers &= ~(1 << gameObject.layer);

        targetYaw = transform.eulerAngles.y;
        cameraPitch = cameraRoot != null ? NormalizeAngle(cameraRoot.localEulerAngles.x) : 0f;
    }

    private void OnEnable()
    {
        ResolveInputActions();
    }

    private void Update()
    {
        if (characterController == null || playerInput == null)
        {
            return;
        }

        ReadInput();
        UpdateGroundedState();
        UpdateLookRotation();
        UpdateJumpAndGravity();
        UpdateMovement();
        UpdateAnimator();
    }

    private void CacheReferences()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInput>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (cameraRoot == null)
        {
            Transform candidate = transform.Find("CameraRoot");
            if (candidate != null)
            {
                cameraRoot = candidate;
            }
        }
    }

    private void ApplySafeRanges()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        jumpHeight = Mathf.Max(0.1f, jumpHeight);
        gravity = Mathf.Min(-0.01f, gravity);
        terminalVelocity = Mathf.Min(-1f, terminalVelocity);
        fallGravityMultiplier = Mathf.Max(1f, fallGravityMultiplier);
        groundedRadius = Mathf.Max(0.01f, groundedRadius);
        maxPitch = Mathf.Max(minPitch, maxPitch);
    }

    private void ResolveInputActions()
    {
        if (playerInput == null || playerInput.actions == null)
        {
            return;
        }

        moveAction = playerInput.actions["Move"];
        lookAction = playerInput.actions["Look"];
        jumpAction = playerInput.actions["Jump"];
        sprintAction = playerInput.actions["Sprint"];
    }

    private void ReadInput()
    {
        moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

        if (jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            jumpQueued = true;
        }
    }

    private void UpdateGroundedState()
    {
        Vector3 spherePosition = transform.position + Vector3.up * groundedOffset;
        bool groundedCheck = Physics.CheckSphere(
            spherePosition,
            groundedRadius,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        // While the character is still moving upward after a jump, don't snap back
        // to grounded just because the ground check sphere still overlaps the floor.
        isGrounded = groundedCheck && verticalVelocity <= 0f;

        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
    }

    private void UpdateLookRotation()
    {
        targetYaw += lookInput.x * lookSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch - (lookInput.y * lookSensitivity), minPitch, maxPitch);

        float currentYaw = transform.eulerAngles.y;
        float nextYaw = rotationSpeed > 0f
            ? Mathf.MoveTowardsAngle(currentYaw, targetYaw, rotationSpeed * Time.deltaTime)
            : targetYaw;

        transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);

        if (cameraRoot != null)
        {
            cameraRoot.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }

    private void UpdateJumpAndGravity()
    {
        if (isGrounded && jumpQueued)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            isGrounded = false;

            if (animator != null)
            {
                animator.SetTrigger(JumpHash);
            }
        }

        if (!isGrounded)
        {
            float appliedGravity = verticalVelocity < 0f
                ? gravity * fallGravityMultiplier
                : gravity;

            verticalVelocity = Mathf.Max(verticalVelocity + (appliedGravity * Time.deltaTime), terminalVelocity);
        }

        jumpQueued = false;
    }

    private void UpdateMovement()
    {
        Vector3 worldMove = GetCameraRelativeMove(moveInput);
        float inputMagnitude = Mathf.Clamp01(moveInput.magnitude);
        float speed = (CanSprint() ? sprintSpeed : walkSpeed) * inputMagnitude;

        Vector3 horizontalVelocity = worldMove * speed;
        Vector3 frameVelocity = horizontalVelocity + (Vector3.up * verticalVelocity);

        characterController.Move(frameVelocity * Time.deltaTime);
    }

    private Vector3 GetCameraRelativeMove(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        if (cameraRoot != null)
        {
            forward = Vector3.ProjectOnPlane(cameraRoot.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraRoot.right, Vector3.up).normalized;
        }

        Vector3 move = (forward * input.y) + (right * input.x);
        return move.sqrMagnitude > 1f ? move.normalized : move;
    }

    private bool CanSprint()
    {
        if (sprintAction == null || !sprintAction.IsPressed())
        {
            return false;
        }

        return isGrounded && moveInput.y > 0.1f && moveInput.sqrMagnitude > 0.01f;
    }

    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        Vector2 animationInput = moveInput;
        animationInput.x *= 0.5f;
        animationInput.y *= CanSprint() ? 1f : 0.5f;

        float speedValue = moveInput.sqrMagnitude > 0.001f
            ? (CanSprint() ? 1f : Mathf.Clamp01(moveInput.magnitude) * 0.5f)
            : 0f;

        animator.SetFloat(MoveXHash, animationInput.x, animationDampTime, Time.deltaTime);
        animator.SetFloat(MoveYHash, animationInput.y, animationDampTime, Time.deltaTime);
        animator.SetFloat(SpeedHash, speedValue, animationDampTime, Time.deltaTime);
        animator.SetBool(IsGroundedHash, isGrounded);
        animator.SetFloat(VerticalVelocityHash, verticalVelocity);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f)
        {
            angle -= 360f;
        }

        while (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector3 spherePosition = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawWireSphere(spherePosition, groundedRadius);
    }
}
