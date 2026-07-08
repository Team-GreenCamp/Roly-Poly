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
            // 입력은 가변 프레임(Update)에서 받고, 실제 점프는 FixedUpdate에서 처리하므로
            // 누른 사실을 버퍼 타이머로 기억해 둔다. (착지 직전 입력 유실 방지)
            jumpBufferTimer = jumpBufferTime;
        }

        // 돌진 입력도 FixedUpdate에서 임펄스로 처리하므로 눌림만 큐잉한다.
        if (dashAction != null && dashAction.WasPressedThisFrame() && Time.time - lastDashTime > dashCooldown)
        {
            dashQueued = true;
        }
    }

    private void ClearGameplayInputState()
    {
        moveInput = Vector2.zero;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        dashQueued = false;
        landingSpeedPreserveTimer = 0f;
        currentHorizontalVelocity = Vector3.zero;
        currentMoveDirection = Vector3.zero;

        // 조작 불가 상태(넉다운/스턴/탈락/로비)에서는 잡기 대상 아웃라인도 정리한다.
        ClearGrappleOutline();
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

    private Camera cachedMovementCamera;
    private Transform cachedCameraRoot;

    private Transform GetMovementReference()
    {
        // 카메라가 실제로 바라보는 방향 기준으로 이동한다.
        // Camera.main(내부 태그 검색)과 FindFirstObjectByType는 비싸므로, 캐시가 비었을 때(파괴/씬 전환)만 다시 찾는다.
        if (cachedMovementCamera == null)
        {
            cachedMovementCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }

        if (cachedMovementCamera != null)
        {
            return cachedMovementCamera.transform;
        }

        if (cachedCameraRoot == null)
        {
            cachedCameraRoot = transform.Find("CameraRoot");
        }

        return cachedCameraRoot != null ? cachedCameraRoot : transform;
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
        // 버퍼된 점프 입력이 살아 있고(jumpBufferTimer), 접지/코요테 윈도우 안일 때만 점프한다.
        if (jumpBufferTimer <= 0f || coyoteTimer <= 0f)
        {
            return;
        }

        Vector3 velocity = physicsBody.linearVelocity;
        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        physicsBody.linearVelocity = velocity;

        isGrounded = false;
        jumpBufferTimer = 0f; // 입력 버퍼 소모
        coyoteTimer = 0f;     // 코요테 소모(공중 더블 점프 방지)
    }

    private void UpdateMovement()
    {
        Vector3 moveDirection = GetMoveDirection(moveInput);
        currentMoveDirection = moveDirection;
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

    // yaw는 직접 회전(MoveRotation)으로 제어한다. 토크 PD 방식은 게인이 낮으면 몸이 이동 방향보다
    // 늦게 돌아 미끄러지듯 보이고, 높이면 목표 주변에서 진동해서 어느 쪽으로 튜닝해도 어색했다.
    // 스윙-트위스트 분해로 yaw만 교체하므로 오뚝이 기울기(밸런스 물리)는 그대로 유지된다.
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

        // 현재 회전에서 yaw(트위스트) 성분만 목표 yaw로 바꾸고 기울기(스윙)는 보존한다.
        Quaternion currentTwist = Quaternion.Euler(0f, currentYaw, 0f);
        Quaternion swing = physicsBody.rotation * Quaternion.Inverse(currentTwist);
        physicsBody.MoveRotation(swing * Quaternion.Euler(0f, desiredYaw, 0f));

        // 직접 제어와 싸우지 않도록 물리 각속도의 yaw 성분은 제거한다(기울기 각속도는 유지).
        RemoveYawAngularVelocity(1f);
    }

    private void StabilizeIdleYaw()
    {
        // 정지 중 외부 충격으로 생긴 yaw 회전은 감쇠로 서서히 멈춘다(즉시 죽이면 밀치기 맞았을 때 뻣뻣해 보임).
        RemoveYawAngularVelocity(1f - Mathf.Exp(-turnDamping * Time.fixedDeltaTime));
    }

    private void RemoveYawAngularVelocity(float fraction)
    {
        Vector3 angularVelocity = physicsBody.angularVelocity;
        float yawVelocity = Vector3.Dot(angularVelocity, Vector3.up);
        angularVelocity -= Vector3.up * (yawVelocity * Mathf.Clamp01(fraction));
        physicsBody.angularVelocity = angularVelocity;
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
