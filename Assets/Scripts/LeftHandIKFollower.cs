using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class LeftHandIKFollower : MonoBehaviour
{
    private static readonly string[] GripNameCandidates =
    {
        "L_Grip IK",
        "LeftHandGrip",
        "Left Grip",
        "L_Grip",
        "LeftHandIK",
    };

    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform weaponHolder;
    [SerializeField] private TwoBoneIKConstraint leftHandConstraint;
    [SerializeField] private RigBuilder rigBuilder;
    [SerializeField] private Transform currentGrip;

    private Transform trackedWeaponRoot;
    private Transform defaultConstraintTarget;
    private bool forceRefresh = true;

    private void Awake()
    {
        CacheReferences();
        ForceRefresh();
    }

    private IEnumerator Start()
    {
        yield return null;
        ForceRigRebuild("Start");
    }

    private void OnEnable()
    {
        CacheReferences();

        if (rigBuilder != null)
        {
            rigBuilder.enabled = true;
        }

        ForceRefresh();
    }

    private void OnValidate()
    {
        CacheReferences();
        forceRefresh = true;
    }

    private void LateUpdate()
    {
        CacheReferences();

        Transform activeWeaponRoot = GetActiveWeaponRoot();
        if (ShouldRefreshGrip(activeWeaponRoot))
        {
            trackedWeaponRoot = activeWeaponRoot;
            currentGrip = FindGrip(activeWeaponRoot);
            forceRefresh = false;
            ApplyConstraintTarget(currentGrip);
        }
    }

    public void SetGrip(Transform grip)
    {
        currentGrip = grip;
        trackedWeaponRoot = GetWeaponRootForGrip(grip);
        forceRefresh = false;
        ApplyConstraintTarget(grip);
    }

    public void SyncToWeapon(Transform weaponRoot)
    {
        trackedWeaponRoot = weaponRoot;
        currentGrip = FindGrip(weaponRoot);
        forceRefresh = false;
        ApplyConstraintTarget(currentGrip);
        ForceRigRebuild("SyncToWeapon");
    }

    public void ForceRefresh()
    {
        trackedWeaponRoot = null;
        currentGrip = null;
        forceRefresh = true;
        ForceRigRebuild("ForceRefresh");
    }

    private void CacheReferences()
    {
        if (leftHandTarget == null)
        {
            leftHandTarget = FindChildByName("LeftHand Target", "LeftHand IK Target");
        }

        if (weaponHolder == null)
        {
            weaponHolder = FindChildByName("WeaponHolder");
        }

        if (rigBuilder == null)
        {
            rigBuilder = GetComponent<RigBuilder>();
        }

        if (leftHandConstraint == null)
        {
            leftHandConstraint = GetComponentInChildren<TwoBoneIKConstraint>(true);
        }

        if (defaultConstraintTarget == null && leftHandConstraint != null)
        {
            defaultConstraintTarget = leftHandConstraint.data.target != null
                ? leftHandConstraint.data.target
                : leftHandTarget;
        }
    }

    private Transform FindChildByName(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Transform candidate = transform.Find(names[i]);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool ShouldRefreshGrip(Transform activeWeaponRoot)
    {
        if (forceRefresh)
        {
            return true;
        }

        if (activeWeaponRoot != trackedWeaponRoot)
        {
            return true;
        }

        if (activeWeaponRoot == null)
        {
            return currentGrip != null;
        }

        if (currentGrip == null)
        {
            return true;
        }

        return !currentGrip.IsChildOf(activeWeaponRoot);
    }

    private Transform GetActiveWeaponRoot()
    {
        if (weaponHolder == null || weaponHolder.childCount == 0)
        {
            return null;
        }

        for (int i = 0; i < weaponHolder.childCount; i++)
        {
            Transform child = weaponHolder.GetChild(i);
            if (child.gameObject.activeInHierarchy)
            {
                return child;
            }
        }

        return weaponHolder.GetChild(0);
    }

    private static Transform FindGrip(Transform weaponRoot)
    {
        if (weaponRoot == null)
        {
            return null;
        }

        Transform[] children = weaponRoot.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < GripNameCandidates.Length; i++)
        {
            string candidateName = GripNameCandidates[i];
            for (int j = 0; j < children.Length; j++)
            {
                if (string.Equals(children[j].name, candidateName, StringComparison.OrdinalIgnoreCase))
                {
                    return children[j];
                }
            }
        }

        for (int i = 0; i < children.Length; i++)
        {
            string childName = children[i].name;
            bool looksLikeLeftGrip =
                childName.IndexOf("grip", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (childName.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 childName.IndexOf("l_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 childName.IndexOf("_l", StringComparison.OrdinalIgnoreCase) >= 0);

            if (looksLikeLeftGrip)
            {
                return children[i];
            }
        }

        return null;
    }

    private Transform GetWeaponRootForGrip(Transform grip)
    {
        if (grip == null || weaponHolder == null)
        {
            return null;
        }

        Transform current = grip;
        while (current != null && current.parent != weaponHolder)
        {
            current = current.parent;
        }

        return current;
    }

    private void ApplyConstraintTarget(Transform grip)
    {
        if (leftHandConstraint == null)
        {
            return;
        }

        TwoBoneIKConstraintData data = leftHandConstraint.data;
        data.target = grip != null ? grip : defaultConstraintTarget;
        leftHandConstraint.data = data;
    }

    private void ForceRigRebuild(string reason)
    {
        if (rigBuilder == null)
        {
            Debug.LogWarning($"[{nameof(LeftHandIKFollower)}] Rig rebuild skipped. reason={reason}, rigBuilder=null", this);
            return;
        }

        string weaponName = trackedWeaponRoot != null ? trackedWeaponRoot.name : "null";
        string gripName = currentGrip != null ? currentGrip.name : "null";
        string targetName = leftHandConstraint != null && leftHandConstraint.data.target != null
            ? leftHandConstraint.data.target.name
            : "null";

        Debug.Log(
            $"[{nameof(LeftHandIKFollower)}] Rebuild rig. reason={reason}, weapon={weaponName}, grip={gripName}, target={targetName}, rigEnabledBefore={rigBuilder.enabled}",
            this);

        rigBuilder.enabled = false;
        rigBuilder.Clear();
        rigBuilder.enabled = true;

        Debug.Log(
            $"[{nameof(LeftHandIKFollower)}] Rebuild complete. reason={reason}, rigEnabledAfter={rigBuilder.enabled}",
            this);
    }
}
