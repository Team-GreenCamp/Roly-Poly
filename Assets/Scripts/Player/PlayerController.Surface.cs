using UnityEngine;

public partial class PlayerController
{
    private void EnsureLowFrictionColliderMaterial()
    {
        if (!useLowFrictionColliderMaterial || physicsCollider == null)
        {
            return;
        }

        if (lowFrictionColliderMaterial == null)
        {
            lowFrictionColliderMaterial = new PhysicsMaterial("Player Low Friction Runtime")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        if (groundGripColliderMaterial == null)
        {
            groundGripColliderMaterial = new PhysicsMaterial("Player Ground Grip Runtime")
            {
                dynamicFriction = idleGroundFriction,
                staticFriction = idleGroundFriction,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        UpdateColliderSurfaceMaterial();
    }

    private void UpdateColliderSurfaceMaterial()
    {
        if (!useLowFrictionColliderMaterial || physicsCollider == null || lowFrictionColliderMaterial == null)
        {
            return;
        }

        bool isTryingToMove = moveInput.sqrMagnitude > idleFrictionInputThreshold * idleFrictionInputThreshold;
        bool shouldGripGround = isGrounded && !isTryingToMove && groundGripColliderMaterial != null;

        // 이동 중에는 무마찰로 출발성을 살리고, 정지 중에는 grip 재질로 바꿔 얕은 경사 미끄러짐을 막는다.
        physicsCollider.material = shouldGripGround ? groundGripColliderMaterial : lowFrictionColliderMaterial;
    }
}
