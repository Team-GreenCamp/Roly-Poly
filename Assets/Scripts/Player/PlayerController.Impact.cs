using UnityEngine;

public partial class PlayerController
{
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

    private void OnCollisionEnter(Collision collision)
    {
        UpdateGroundedContact(collision);
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

        GrabbableObject grabbable = otherBody.GetComponentInParent<GrabbableObject>();
        bool isThrownOrFastObject =
            grabbable != null &&
            relativeSpeed >= knockdownThrownObjectSpeedThreshold;

        bool isHeavyDownwardImpact =
            contact.normal.y < -0.2f &&
            otherBody.mass >= heavyObjectMassThreshold &&
            otherBody.linearVelocity.y <= -heavyObjectDownwardSpeedThreshold;

        if (isHeavyDownwardImpact)
        {
            ApplyExternalImpulse(Vector3.down * (otherBody.mass * 0.1f), contact.point);
        }

        bool isFallingObjectImpact =
            otherBody.linearVelocity.y <= -knockdownFallingObjectSpeedThreshold &&
            relativeSpeed >= knockdownImpactSpeedThreshold;

        if (isThrownOrFastObject || isHeavyDownwardImpact || isFallingObjectImpact)
        {
            // 다른 플레이어가 던진 물체나 높은 곳에서 떨어진 물체에 맞으면 잠시 조작 불가 상태로 넘어집니다.
            Vector3 knockdownForce = collision.relativeVelocity.sqrMagnitude > 0.01f
                ? collision.relativeVelocity * 0.12f
                : impactDirection.normalized * Mathf.Max(1f, relativeSpeed);
            ApplyExternalImpulse(knockdownForce, contact.point);
            StartKnockdown();
        }
    }
}
