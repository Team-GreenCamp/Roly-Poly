using UnityEngine;

public partial class PlayerController
{
    public void SetCarrySpeedMultiplier(float multiplier)
    {
        currentCarrySpeedMultiplier = multiplier;
    }

    public void ResetCarrySpeedMultiplier()
    {
        currentCarrySpeedMultiplier = 1f;
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        if (gameplayInputEnabled == enabled)
        {
            return;
        }

        gameplayInputEnabled = enabled;
        if (!gameplayInputEnabled)
        {
            ClearGameplayInputState();
            StopGameplayMotion();
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

    private void ClearGameplayInputState()
    {
        moveInput = Vector2.zero;
        jumpQueued = false;
        landingSpeedPreserveTimer = 0f;
        currentHorizontalVelocity = Vector3.zero;
    }

    private void StopGameplayMotion()
    {
        if (physicsBody == null || physicsBody.isKinematic)
        {
            return;
        }

        // 로비 대기 중에는 남아있는 이동/회전 속도를 제거해서 키 입력으로 캐릭터가 돌아가지 않게 한다.
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
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
        nextPlanarVelocity = PreserveLandingPlanarSpeed(nextPlanarVelocity, moveDirection, targetSpeed);

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
}
