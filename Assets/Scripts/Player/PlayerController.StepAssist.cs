using UnityEngine;

public partial class PlayerController
{
    private void ApplyStepAssist(Vector3 moveDirection)
    {
        if (!useStepAssist || !isGrounded || maxStepHeight <= 0f || stepLiftSpeed <= 0f || physicsCollider == null)
        {
            return;
        }

        Vector3 flatDirection = Vector3.ProjectOnPlane(moveDirection, Vector3.up);
        if (flatDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        flatDirection.Normalize();

        Vector3 center = transform.TransformPoint(physicsCollider.center);
        float worldRadius = GetWorldRadius();
        float probeRadius = Mathf.Min(stepProbeRadius, worldRadius * 0.6f);
        float footY = center.y - GetWorldHalfHeight();
        Vector3 lowerOrigin = new Vector3(center.x, footY + probeRadius + 0.03f, center.z);
        Vector3 upperOrigin = lowerOrigin + Vector3.up * Mathf.Max(probeRadius * 2f, maxStepHeight);

        if (!Physics.SphereCast(lowerOrigin, probeRadius, flatDirection, out RaycastHit lowerHit, stepCheckDistance, stepAssistLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        if (lowerHit.normal.y > 0.2f)
        {
            return;
        }

        if (Physics.SphereCast(upperOrigin, probeRadius, flatDirection, out RaycastHit upperHit, stepCheckDistance, stepAssistLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Vector3 topProbeOrigin = center + flatDirection * (stepCheckDistance + worldRadius * 0.5f) + Vector3.up * (maxStepHeight + 0.08f);
        float topProbeDistance = maxStepHeight + 0.18f;
        if (!Physics.SphereCast(topProbeOrigin, probeRadius, Vector3.down, out RaycastHit topHit, topProbeDistance, stepAssistLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        float stepHeight = topHit.point.y - footY;
        if (stepHeight <= 0.01f || stepHeight > maxStepHeight)
        {
            return;
        }

        // 낮은 턱의 윗면이 확인된 경우에만 물리 위치를 조금 올려 캡슐이 수직 모서리에 걸리지 않게 합니다.
        float liftAmount = Mathf.Min(stepHeight + 0.02f, stepLiftSpeed * Time.fixedDeltaTime);
        physicsBody.MovePosition(physicsBody.position + Vector3.up * liftAmount);
    }
}
