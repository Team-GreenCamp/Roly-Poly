using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class WeaponPickup : NetworkBehaviour
{
    [SerializeField] private WeaponDefinition weaponDefinition;
    [SerializeField] private Collider pickupCollider;
    [SerializeField] private Renderer[] visualRenderers;

    private readonly NetworkVariable<bool> isAvailable =
        new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool CanBePickedUp => weaponDefinition != null && isAvailable.Value;
    public WeaponDefinition WeaponDefinition => weaponDefinition;

    private void Reset()
    {
        CacheReferences();
    }

    private void OnValidate()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        isAvailable.OnValueChanged += HandleAvailabilityChanged;
        ApplyAvailabilityState(isAvailable.Value);
    }

    public override void OnNetworkDespawn()
    {
        isAvailable.OnValueChanged -= HandleAvailabilityChanged;
    }

    public bool TryConsume(out WeaponDefinition definition)
    {
        definition = null;

        if (!IsServer || !CanBePickedUp)
        {
            return false;
        }

        definition = weaponDefinition;
        isAvailable.Value = false;
        return true;
    }

    private void CacheReferences()
    {
        if (pickupCollider == null)
        {
            pickupCollider = GetComponent<Collider>();
        }

        if (visualRenderers == null || visualRenderers.Length == 0)
        {
            visualRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void HandleAvailabilityChanged(bool previousValue, bool newValue)
    {
        ApplyAvailabilityState(newValue);
    }

    private void ApplyAvailabilityState(bool available)
    {
        if (pickupCollider != null)
        {
            pickupCollider.enabled = available;
        }

        if (visualRenderers == null)
        {
            return;
        }

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            if (visualRenderers[i] != null)
            {
                visualRenderers[i].enabled = available;
            }
        }
    }
}
