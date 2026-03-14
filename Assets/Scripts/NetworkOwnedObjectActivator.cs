using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkOwnedObjectActivator : NetworkBehaviour
{
    [Header("Owner Only")]
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;
    [SerializeField] private GameObject[] ownerOnlyObjects;
    [SerializeField] private bool lockCursorForOwner = true;
    [SerializeField] private string[] cursorLockSceneNames;

    private void Reset()
    {
        AutoAssignOwnerBehaviours();
    }

    private void OnValidate()
    {
        if (ownerOnlyBehaviours == null || ownerOnlyBehaviours.Length == 0)
        {
            AutoAssignOwnerBehaviours();
        }
    }

    public override void OnNetworkSpawn()
    {
        ApplyOwnershipState(IsOwner);
    }

    public override void OnGainedOwnership()
    {
        ApplyOwnershipState(true);
    }

    public override void OnLostOwnership()
    {
        ApplyOwnershipState(false);
    }

    public override void OnNetworkDespawn()
    {
        if (lockCursorForOwner && IsOwner && ShouldLockCursorInCurrentScene())
        {
            MouseController.SetCursorLock(false);
        }
    }

    private void ApplyOwnershipState(bool isOwner)
    {
        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                if (ownerOnlyBehaviours[i] != null)
                {
                    ownerOnlyBehaviours[i].enabled = isOwner;
                }
            }
        }

        if (ownerOnlyObjects != null)
        {
            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                if (ownerOnlyObjects[i] != null)
                {
                    ownerOnlyObjects[i].SetActive(isOwner);
                }
            }
        }

        if (lockCursorForOwner && ShouldLockCursorInCurrentScene())
        {
            MouseController.SetCursorLock(isOwner);
        }
    }

    private bool ShouldLockCursorInCurrentScene()
    {
        if (cursorLockSceneNames == null || cursorLockSceneNames.Length == 0)
        {
            return false;
        }

        string activeSceneName = SceneManager.GetActiveScene().name;
        for (int i = 0; i < cursorLockSceneNames.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(cursorLockSceneNames[i]) &&
                cursorLockSceneNames[i] == activeSceneName)
            {
                return true;
            }
        }

        return false;
    }

    private void AutoAssignOwnerBehaviours()
    {
        List<Behaviour> behaviours = new List<Behaviour>();

        AddIfPresent(GetComponent<PlayerInput>(), behaviours);
        AddIfPresent(GetComponent<PlayerController>(), behaviours);
        AddIfPresent(GetComponent<MouseController>(), behaviours);

        ownerOnlyBehaviours = behaviours.ToArray();
    }

    private static void AddIfPresent(Behaviour behaviour, ICollection<Behaviour> target)
    {
        if (behaviour != null)
        {
            target.Add(behaviour);
        }
    }
}
