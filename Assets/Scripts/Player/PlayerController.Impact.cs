using Unity.Netcode;
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

            // 상대가 플레이어이면 여기서 종료. (아래 Rigidbody 블록으로 흘러가 임펄스가 이중 적용되는 것을 방지)
            return;
        }

        Rigidbody otherBody = collision.rigidbody;
        if (otherBody == null)
        {
            return;
        }

        // 내가 빠르게 물체에 부딪힌 경우의 가벼운 자기 반동(소유자가 자기 자신에게 적용이라 항상 안전).
        if (relativeSpeed >= rigidbodyImpactSpeedThreshold)
        {
            ApplyExternalImpulse(collision.relativeVelocity * 0.08f, contact.point);
        }

        // '물체가 나를 맞혀서' 넘어지는 판정은 물체의 실제 속도가 필요하다.
        // 네트워크에서는 원격 물체가 서버 권한이라 각 클라에서 kinematic(속도 0)이므로 피해자 로컬 감지가 불가.
        // → 물리 권한(서버)이 GrabbableObject.OnCollisionEnter에서 감지해 피해자 소유자에게 릴레이한다.
        //   (ServerHandleObjectImpact) 따라서 여기서는 오프라인(비네트워크)일 때만 직접 판정한다.
        if (networkObject != null && networkObject.IsSpawned)
        {
            return;
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
            HitVignette.Show(); // 오프라인 폴백 경로의 피격 화면 연출 (네트워크는 서버 릴레이 ClientRpc에서 처리)
            Vector3 knockdownForce = collision.relativeVelocity.sqrMagnitude > 0.01f
                ? collision.relativeVelocity * 0.12f
                : impactDirection.normalized * Mathf.Max(1f, relativeSpeed);
            ApplyExternalImpulse(knockdownForce, contact.point);
            StartKnockdown();
        }
    }

    // GrabbableObject(물리 권한=서버/오프라인)가 던져지거나 떨어진 물체로 이 플레이어를 맞혔을 때 호출한다.
    // 원격 물체는 서버에서만 실제 속도를 가지므로 넉다운 판정은 여기(서버)에서 하고,
    // 실제 임펄스/넘어짐은 전투와 동일하게 피해자 '소유자'에게 ClientRpc로 릴레이한다.
    public void ServerHandleObjectImpact(Rigidbody objectBody, bool isGrabbable, float relativeSpeed, Vector3 contactPoint)
    {
        if (!IsServer || objectBody == null)
        {
            return;
        }

        bool isThrownOrFastObject =
            isGrabbable && relativeSpeed >= knockdownThrownObjectSpeedThreshold;

        bool isHeavyDownwardImpact =
            objectBody.mass >= heavyObjectMassThreshold &&
            objectBody.linearVelocity.y <= -heavyObjectDownwardSpeedThreshold &&
            objectBody.worldCenterOfMass.y > BodyCenter.y; // 물체가 나보다 위에서 내려옴

        bool isFallingObjectImpact =
            objectBody.linearVelocity.y <= -knockdownFallingObjectSpeedThreshold &&
            relativeSpeed >= knockdownImpactSpeedThreshold;

        if (!(isThrownOrFastObject || isHeavyDownwardImpact || isFallingObjectImpact))
        {
            return;
        }

        // 밀어내는 방향은 물체의 실제 이동 방향(던진/낙하 방향)으로. 세기는 상대속도에 비례.
        Vector3 pushDirection = objectBody.linearVelocity.sqrMagnitude > 0.0001f
            ? objectBody.linearVelocity.normalized
            : (BodyCenter - contactPoint).normalized;
        Vector3 knockdownForce = pushDirection * (Mathf.Max(1f, relativeSpeed) * 0.12f);
        if (isHeavyDownwardImpact)
        {
            knockdownForce += Vector3.down * (objectBody.mass * 0.1f);
        }

        // 임펄스/넉다운은 피해자 소유자만 로컬로 적용(그 머신에서만 dynamic). 전투 릴레이 경로 재사용.
        ClientRpcParams target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
        ApplyCombatHitOwnerClientRpc(knockdownForce, contactPoint, CombatEffectKnockdown, target);
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
