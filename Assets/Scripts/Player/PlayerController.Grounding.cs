using UnityEngine;

public partial class PlayerController
{
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
        bool hasGroundContact = groundedContactTimer > 0f;
        isGrounded = (hitGround || hasGroundContact) && physicsBody.linearVelocity.y <= 1f;

        if (!wasGrounded && isGrounded)
        {
            StartLandingSpeedPreservation();
        }

        if (!wasGrounded && isGrounded && previousVerticalVelocity <= -landingImpactThreshold)
        {
            Vector3 landingDirection = currentHorizontalVelocity.sqrMagnitude > 0.01f
                ? currentHorizontalVelocity.normalized
                : transform.forward;
            Vector3 landingForce = landingDirection * (Mathf.Abs(previousVerticalVelocity) * landingTorqueMultiplier);
            ApplyExternalImpulse(landingForce, center + (Vector3.up * 0.5f));
        }

        lastVerticalVelocity = physicsBody.linearVelocity.y;
        groundedContactTimer = Mathf.Max(0f, groundedContactTimer - Time.fixedDeltaTime);
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

    private void OnCollisionStay(Collision collision)
    {
        UpdateGroundedContact(collision);
    }

    private void UpdateGroundedContact(Collision collision)
    {
        if (collision == null || collision.contactCount == 0)
        {
            return;
        }

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.normal.y < minGroundContactNormalY)
            {
                continue;
            }

            // Ground Layer가 빠진 발판 위에서도 접촉 법선이 충분히 위를 향하면 접지로 인정한다.
            groundNormal = contact.normal;
            groundedContactTimer = groundedContactGraceTime;
            return;
        }
    }
}
