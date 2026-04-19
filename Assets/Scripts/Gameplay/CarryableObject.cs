using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class CarryableObject : MonoBehaviour, IPlayerInteractable
{
    public enum CarryMode
    {
        OnePlayer,
        TwoPlayers
    }

    [Header("Carry Rule")]
    [SerializeField] private CarryMode carryMode = CarryMode.OnePlayer;
    [SerializeField] private float maxSingleCarryMass = 20f;
    [SerializeField] private float maxCooperativeCarryMass = 80f;
    [SerializeField] private float moveSpeedMultiplier = 0.65f;
    [SerializeField] private bool blockJumpWhileCarrying = true;

    [Header("Cooperative Carry")]
    [SerializeField] private float cooperativeHeightOffset = 1.1f;
    [SerializeField] private float cooperativeReleaseDistance = 3.5f;

    private readonly List<PlayerController> carriers = new List<PlayerController>(2);
    private Rigidbody attachedRigidbody;
    private Collider[] objectColliders;
    private Transform originalParent;
    private bool originalUseGravity;
    private bool originalIsKinematic;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;
    private bool cooperativeCarryActive;

    public Rigidbody AttachedRigidbody
    {
        get
        {
            if (attachedRigidbody == null)
            {
                attachedRigidbody = GetComponent<Rigidbody>();
            }

            return attachedRigidbody;
        }
    }

    public float MoveSpeedMultiplier => moveSpeedMultiplier;
    public bool BlockJumpWhileCarrying => blockJumpWhileCarrying;

    private int RequiredCarrierCount => carryMode == CarryMode.TwoPlayers ? 2 : 1;

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
        ApplySafeRanges();
    }

    private void LateUpdate()
    {
        if (!cooperativeCarryActive)
        {
            return;
        }

        RemoveMissingCarriers();
        if (carriers.Count < RequiredCarrierCount)
        {
            StopCooperativeCarry();
            ReleaseAllCarriers();
            return;
        }

        if (ShouldReleaseForDistance())
        {
            StopCooperativeCarry();
            ReleaseAllCarriers();
            return;
        }

        UpdateCooperativePose();
    }

    public bool CanInteract(PlayerController controller)
    {
        if (controller == null)
        {
            return false;
        }

        if (carriers.Contains(controller))
        {
            return true;
        }

        if (carriers.Count >= RequiredCarrierCount)
        {
            return false;
        }

        if (carryMode == CarryMode.OnePlayer)
        {
            return controller.CanCarryObject(this) && AttachedRigidbody.mass <= maxSingleCarryMass;
        }

        return controller.CanCarryObject(this) && AttachedRigidbody.mass <= maxCooperativeCarryMass;
    }

    public void Interact(PlayerController controller)
    {
        if (controller == null)
        {
            return;
        }

        if (carriers.Contains(controller))
        {
            ReleaseCarrier(controller);
            return;
        }

        if (!CanInteract(controller))
        {
            return;
        }

        if (carryMode == CarryMode.OnePlayer)
        {
            StartSingleCarry(controller);
        }
        else
        {
            JoinCooperativeCarry(controller);
        }
    }

    public void ReleaseCarrier(PlayerController controller)
    {
        if (controller == null || !carriers.Remove(controller))
        {
            return;
        }

        controller.ClearCarryRule(this);
        SetCarrierCollisionIgnored(controller, false);

        if (carryMode == CarryMode.TwoPlayers && carriers.Count < RequiredCarrierCount)
        {
            StopCooperativeCarry();
            ReleaseAllCarriers();
        }
    }

    public void NotifyCarrierReleased(PlayerController controller)
    {
        if (controller == null)
        {
            return;
        }

        carriers.Remove(controller);
        SetCarrierCollisionIgnored(controller, false);
    }

    private void StartSingleCarry(PlayerController controller)
    {
        if (!controller.TryStartCarryObject(this))
        {
            return;
        }

        carriers.Add(controller);
    }

    private void JoinCooperativeCarry(PlayerController controller)
    {
        carriers.Add(controller);
        controller.ApplyCarryRule(this);
        SetCarrierCollisionIgnored(controller, true);

        // 두 명이 모두 잡으면 오브젝트를 두 플레이어 중간 지점에 고정한다.
        if (carriers.Count >= RequiredCarrierCount)
        {
            StartCooperativeCarry();
        }
    }

    private void StartCooperativeCarry()
    {
        if (cooperativeCarryActive)
        {
            return;
        }

        StoreOriginalPhysicsState();
        AttachedRigidbody.linearVelocity = Vector3.zero;
        AttachedRigidbody.angularVelocity = Vector3.zero;
        AttachedRigidbody.useGravity = false;
        AttachedRigidbody.isKinematic = true;
        AttachedRigidbody.interpolation = RigidbodyInterpolation.None;
        AttachedRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        cooperativeCarryActive = true;
        UpdateCooperativePose();
    }

    private void StopCooperativeCarry()
    {
        if (!cooperativeCarryActive)
        {
            return;
        }

        RestoreOriginalPhysicsState();
        cooperativeCarryActive = false;
    }

    private void ReleaseAllCarriers()
    {
        for (int i = carriers.Count - 1; i >= 0; i--)
        {
            PlayerController carrier = carriers[i];
            if (carrier != null)
            {
                carrier.ClearCarryRule(this);
                SetCarrierCollisionIgnored(carrier, false);
            }
        }

        carriers.Clear();
    }

    private void UpdateCooperativePose()
    {
        Vector3 center = Vector3.zero;
        Vector3 forward = Vector3.zero;

        for (int i = 0; i < RequiredCarrierCount; i++)
        {
            Transform carrierTransform = carriers[i].transform;
            center += carrierTransform.position;
            forward += Vector3.ProjectOnPlane(carrierTransform.forward, Vector3.up);
        }

        center /= RequiredCarrierCount;
        center += Vector3.up * cooperativeHeightOffset;

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = transform.forward;
        }

        AttachedRigidbody.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward.normalized, Vector3.up));
    }

    private bool ShouldReleaseForDistance()
    {
        if (carriers.Count < RequiredCarrierCount)
        {
            return true;
        }

        float maxDistanceSqr = cooperativeReleaseDistance * cooperativeReleaseDistance;
        for (int i = 0; i < carriers.Count; i++)
        {
            PlayerController carrier = carriers[i];
            if (carrier == null)
            {
                return true;
            }

            if ((carrier.transform.position - transform.position).sqrMagnitude > maxDistanceSqr)
            {
                return true;
            }
        }

        return false;
    }

    private void StoreOriginalPhysicsState()
    {
        originalParent = transform.parent;
        originalUseGravity = AttachedRigidbody.useGravity;
        originalIsKinematic = AttachedRigidbody.isKinematic;
        originalInterpolation = AttachedRigidbody.interpolation;
        originalCollisionMode = AttachedRigidbody.collisionDetectionMode;
    }

    private void RestoreOriginalPhysicsState()
    {
        transform.SetParent(originalParent, true);
        AttachedRigidbody.useGravity = originalUseGravity;
        AttachedRigidbody.isKinematic = originalIsKinematic;
        AttachedRigidbody.interpolation = originalInterpolation;
        AttachedRigidbody.collisionDetectionMode = originalCollisionMode;
    }

    private void SetCarrierCollisionIgnored(PlayerController controller, bool shouldIgnore)
    {
        Collider playerCollider = controller.GetCarryCollisionCollider();
        if (playerCollider == null || objectColliders == null)
        {
            return;
        }

        for (int i = 0; i < objectColliders.Length; i++)
        {
            Collider objectCollider = objectColliders[i];
            if (objectCollider != null)
            {
                Physics.IgnoreCollision(playerCollider, objectCollider, shouldIgnore);
            }
        }
    }

    private void RemoveMissingCarriers()
    {
        for (int i = carriers.Count - 1; i >= 0; i--)
        {
            if (carriers[i] == null)
            {
                carriers.RemoveAt(i);
            }
        }
    }

    private void CacheReferences()
    {
        if (attachedRigidbody == null)
        {
            attachedRigidbody = GetComponent<Rigidbody>();
        }

        objectColliders = GetComponentsInChildren<Collider>(true);
    }

    private void ApplySafeRanges()
    {
        maxSingleCarryMass = Mathf.Max(0.1f, maxSingleCarryMass);
        maxCooperativeCarryMass = Mathf.Max(maxSingleCarryMass, maxCooperativeCarryMass);
        moveSpeedMultiplier = Mathf.Clamp(moveSpeedMultiplier, 0.1f, 1f);
        cooperativeHeightOffset = Mathf.Max(0f, cooperativeHeightOffset);
        cooperativeReleaseDistance = Mathf.Max(0.5f, cooperativeReleaseDistance);
    }
}
