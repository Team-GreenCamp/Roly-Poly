using UnityEngine;

public partial class PlayerController
{
    private void StartLandingSpeedPreservation()
    {
        landingPreservedPlanarVelocity = currentHorizontalVelocity;

        if (landingPreservedPlanarVelocity.sqrMagnitude <= 0.01f || landingSpeedPreserveDuration <= 0f)
        {
            landingSpeedPreserveTimer = 0f;
            return;
        }

        landingSpeedPreserveTimer = landingSpeedPreserveDuration;
    }

    private Vector3 PreserveLandingPlanarSpeed(Vector3 nextPlanarVelocity, Vector3 moveDirection, float targetSpeed)
    {
        if (landingSpeedPreserveTimer <= 0f || targetSpeed <= 0f || moveDirection.sqrMagnitude <= 0.0001f)
        {
            return nextPlanarVelocity;
        }

        Vector3 inputDirection = moveDirection.normalized;
        Vector3 preservedDirection = landingPreservedPlanarVelocity.normalized;
        if (Vector3.Dot(inputDirection, preservedDirection) < 0.25f)
        {
            return nextPlanarVelocity;
        }

        float preservedSpeed = Mathf.Min(targetSpeed, landingPreservedPlanarVelocity.magnitude);
        float minimumInputSpeed = preservedSpeed * landingSpeedPreserveRatio;
        float currentInputSpeed = Vector3.Dot(nextPlanarVelocity, inputDirection);
        if (currentInputSpeed >= minimumInputSpeed)
        {
            return nextPlanarVelocity;
        }

        // 착지 접촉 마찰로 입력 방향 속도가 갑자기 깎이면 짧게 보존해서 턱에 걸린 듯한 멈춤을 줄인다.
        Vector3 lateralVelocity = nextPlanarVelocity - (inputDirection * currentInputSpeed);
        Vector3 preservedVelocity = lateralVelocity + (inputDirection * minimumInputSpeed);
        return preservedVelocity.magnitude > targetSpeed ? preservedVelocity.normalized * targetSpeed : preservedVelocity;
    }

    private void ApplyLandingTiltDamping()
    {
        if (landingSpeedPreserveTimer <= 0f || landingTiltAngularDamping <= 0f || physicsBody == null)
        {
            return;
        }

        Vector3 angularVelocity = physicsBody.angularVelocity;
        Vector3 yawAngularVelocity = Vector3.Project(angularVelocity, Vector3.up);
        Vector3 tiltAngularVelocity = angularVelocity - yawAngularVelocity;
        float damping = Mathf.Clamp01(landingTiltAngularDamping * Time.fixedDeltaTime);

        // 착지 직후 비스듬한 캡슐이 바닥에 박히지 않도록 pitch/roll 회전만 빠르게 줄인다.
        physicsBody.angularVelocity = yawAngularVelocity + Vector3.Lerp(tiltAngularVelocity, Vector3.zero, damping);
    }

    private void UpdateLandingStabilityTimers()
    {
        if (landingSpeedPreserveTimer <= 0f)
        {
            return;
        }

        landingSpeedPreserveTimer = Mathf.Max(0f, landingSpeedPreserveTimer - Time.fixedDeltaTime);
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

        // 충격으로 흔들리는 동안에도 균형 토크를 즉시 적용한다. 무게중심이 낮은 오뚝이처럼
        // 곧바로 세워지면서 좌우로 출렁이게 하기 위함이다. (예전에는 knockdown 동안 토크를 멈춰
        // 그냥 쓰러져 있다가 일어나는 느낌이라 오뚝이 흔들림이 보이지 않았다.)
        // 부스트는 크게 기운 채 오래 누워 있을 때만 켜서, 작은 충격은 부드럽게 출렁이게 둔다.
        float recoveryMultiplier = timeSinceLargeTilt >= recoveryDelay ? recoveryTorqueMultiplier : 1f;

        Vector3 uprightAxis = Vector3.Cross(transform.up, Vector3.up);
        Vector3 tiltAngularVelocity = Vector3.ProjectOnPlane(physicsBody.angularVelocity, Vector3.up);

        Vector3 correctiveTorque =
            (uprightAxis * (uprightTorque * recoveryMultiplier)) -
            (tiltAngularVelocity * (uprightDamping * recoveryMultiplier));

        physicsBody.AddTorque(correctiveTorque, ForceMode.Acceleration);
    }

    private void StartKnockdown()
    {
        if (isKnockedDown)
        {
            knockdownTimer = 0f;
            return;
        }

        // 넘어짐 중에는 입력만 막고 Rigidbody는 계속 물리/회전하도록 둡니다.
        isKnockedDown = true;
        knockdownTimer = 0f;
        timeSinceLargeTilt = recoveryDelay;
        ClearGameplayInputState();
    }

    private void UpdateKnockdownRecovery()
    {
        if (!isKnockedDown)
        {
            return;
        }

        knockdownTimer += Time.fixedDeltaTime;
        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        float tiltAngularSpeed = Vector3.ProjectOnPlane(physicsBody.angularVelocity, Vector3.up).magnitude;

        if (knockdownTimer < knockdownMinimumDuration)
        {
            return;
        }

        if (tiltAngle > knockdownUprightAngle || tiltAngularSpeed > knockdownRecoveryAngularSpeed)
        {
            return;
        }

        isKnockedDown = false;
        knockdownTimer = 0f;
        ClearGameplayInputState();
    }
}
