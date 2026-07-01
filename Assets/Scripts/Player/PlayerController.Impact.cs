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
        if (otherPlayer != null && otherPlayer != this)
        {
            // 1) 머리 밟기(스톰프) 우선 판정 — 성립하면 자기 바운스 + 상대 찌부 처리 후 종료.
            if (TryStomp(otherPlayer, contact))
            {
                return;
            }

            // 2) 돌진 중 정면 충돌이면 상대를 넘어뜨린다(밀치기).
            if (Time.time < dashActiveUntil)
            {
                Vector3 shoveDir = otherPlayer.BodyCenter - BodyCenter;
                shoveDir.y = 0f;
                if (shoveDir.sqrMagnitude < 0.0001f)
                {
                    shoveDir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                }
                SendCombatHit(otherPlayer, shoveDir.normalized * dashShoveStrength, contact.point, CombatEffectKnockdown, 0f);
                return;
            }

            // 3) 일반 플레이어 간 충돌: 기존처럼 가벼운 밀림만.
            if (relativeSpeed > 0.5f)
            {
                ApplyExternalImpulse(impactDirection.normalized * playerCollisionImpact, contact.point);
            }
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

    // 위에서 하강하며 상대의 머리 영역을 밟았는지 판정한다.(공격자=자기 머신, self 속도는 신뢰 가능)
    // 성립 시: 자기 바운스(로컬 즉시) + 상대 찌부 스턴(서버 경유). true면 일반 충돌 처리를 건너뛴다.
    private bool TryStomp(PlayerController victim, ContactPoint contact)
    {
        if (victim == null || physicsBody == null)
        {
            return false;
        }

        if (Time.time - lastStompTime <= stompCooldown)
        {
            return false;
        }

        // 충분히 하강 중인가.
        if (physicsBody.linearVelocity.y > -stompMinDownSpeed)
        {
            return false;
        }

        // 내가 상대보다 위에 있고, 접촉점이 상대의 머리 영역인가.
        if (BodyCenter.y <= victim.BodyCenter.y)
        {
            return false;
        }
        if (contact.point.y < victim.transform.position.y + stompHeadHeight)
        {
            return false;
        }

        lastStompTime = Time.time;

        // 밟은 쪽: 즉시 로컬 바운스(자기 소유이므로 직접 속도 설정).
        Vector3 velocity = physicsBody.linearVelocity;
        velocity.y = Mathf.Sqrt(stompBounceHeight * -2f * gravity);
        physicsBody.linearVelocity = velocity;

        // 밟힌 쪽: 서버 경유로 찌부 스턴 + 아래 방향 임펄스.
        SendCombatHit(victim, Vector3.down * stompDownImpulse, contact.point, CombatEffectStun, stompStunDuration);
        return true;
    }
}
