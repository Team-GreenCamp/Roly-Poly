using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkOwnedObjectActivator : NetworkBehaviour
{
    [Header("Owner Only")]
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;
    [SerializeField] private GameObject[] ownerOnlyObjects;
    [SerializeField] private NetworkTransform networkTransform;
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private bool lockCursorForOwner = true;
    [SerializeField] private string[] cursorLockSceneNames;

    [Header("Spawn")]
    [SerializeField] private bool separatePlayerSpawnPositions = true;
    [SerializeField] private float spawnRadius = 3f;
    [SerializeField] private Vector3 spawnCenterOffset;

    [Header("Transform Sync")]
    [SerializeField] private bool syncTransformState = true;
    [SerializeField] private float remotePositionLerpSpeed = 20f;
    [SerializeField] private float remoteRotationLerpSpeed = 20f;

    [Header("Camera")]
    [SerializeField] private string runtimeVirtualCameraName = "Runtime Cinemachine Camera";
    [SerializeField] private Vector3 followOffset = new Vector3(0.65f, 1.6f, -3.5f);
    [SerializeField] private string[] runtimeCameraSceneNames = { "GameScene", "Network Test" };

    [Header("Name Label")]
    [SerializeField] private bool showNameLabel = true;
    [SerializeField] private Vector3 nameLabelOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] private float nameLabelFontSize = 4f;

    private readonly NetworkVariable<Vector3> syncedPosition =
        new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> syncedRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private CinemachineCamera boundCamera;
    private TextMeshPro nameLabel;

    private void Reset()
    {
        AutoAssignOwnerBehaviours();
        CacheReferences();
    }

    private void OnValidate()
    {
        CacheReferences();

        if (ownerOnlyBehaviours == null || ownerOnlyBehaviours.Length == 0)
        {
            AutoAssignOwnerBehaviours();
        }
    }

    public override void OnNetworkSpawn()
    {
        ConfigureOwnershipAuthority();
        SceneManager.sceneLoaded += HandleSceneLoaded;

        EnsureNameLabel();
        UpdateNameLabel();

        if (IsServer)
        {
            ApplySpawnPosition();
        }

        ApplyOwnershipState(IsOwner);

        if (IsOwner)
        {
            BindLocalCamera();
        }
    }

    public override void OnGainedOwnership()
    {
        ApplyOwnershipState(true);
        UpdateNameLabel();
        BindLocalCamera();
    }

    public override void OnLostOwnership()
    {
        ClearLocalCameraBinding();
        ApplyOwnershipState(false);
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        ClearLocalCameraBinding();

        if (lockCursorForOwner && IsOwner && ShouldLockCursorInCurrentScene())
        {
            MouseController.SetCursorLock(false);
        }
    }

    private void LateUpdate()
    {
        UpdateTransformSync();
        UpdateNameLabelFacing();
    }

    private void ApplyOwnershipState(bool isOwner)
    {
        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                if (ownerOnlyBehaviours[i] != null && ShouldToggleBehaviourOwnership(ownerOnlyBehaviours[i]))
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

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (IsServer)
        {
            ApplySpawnPosition();
        }

        if (IsOwner)
        {
            UpdateNameLabel();
            BindLocalCamera();
        }
    }

    private void CacheReferences()
    {
        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

        if (cameraRoot == null)
        {
            Transform candidate = transform.Find("CameraRoot");
            if (candidate != null)
            {
                cameraRoot = candidate;
            }
        }

    }

    private void ConfigureOwnershipAuthority()
    {
        if (networkTransform != null)
        {
            networkTransform.enabled = !syncTransformState;

            if (!syncTransformState)
            {
                networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            }
        }
    }

    private void ApplySpawnPosition()
    {
        if (!separatePlayerSpawnPositions)
        {
            return;
        }

        Vector3 targetPosition = spawnCenterOffset + GetSpawnOffset(OwnerClientId);

        if (TryGetComponent(out Rigidbody body))
        {
            Vector3 previousVelocity = body.linearVelocity;
            Vector3 previousAngularVelocity = body.angularVelocity;
            bool wasKinematic = body.isKinematic;

            body.isKinematic = true;
            body.position = targetPosition;
            body.rotation = transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = wasKinematic;

            if (!wasKinematic)
            {
                body.linearVelocity = previousVelocity;
                body.angularVelocity = previousAngularVelocity;
            }
        }
        else
        {
            transform.position = targetPosition;
        }
    }

    private Vector3 GetSpawnOffset(ulong ownerClientId)
    {
        if (ownerClientId == 0)
        {
            return Vector3.zero;
        }

        float angleStep = 360f / 8f;
        float angle = (ownerClientId - 1) * angleStep;
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * Mathf.Max(0.5f, spawnRadius);
    }

    private void BindLocalCamera()
    {
        if (!IsOwner || cameraRoot == null)
        {
            return;
        }

        Camera sceneCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (sceneCamera == null)
        {
            return;
        }

        if (sceneCamera.GetComponent<CinemachineBrain>() == null)
        {
            sceneCamera.gameObject.AddComponent<CinemachineBrain>();
        }

        CinemachineCamera cinemachineCamera = FindSceneCinemachineCamera(SceneManager.GetActiveScene());
        if (cinemachineCamera == null && ShouldCreateRuntimeCameraInCurrentScene())
        {
            cinemachineCamera = CreateRuntimeCinemachineCamera();
        }

        if (cinemachineCamera == null)
        {
            return;
        }

        cinemachineCamera.Follow = cameraRoot;

        // Pan Tilt 카메라는 LookAt을 강제로 잡으면 수동 회전 입력이 꼬일 수 있다.
        if (cinemachineCamera.GetComponent<CinemachinePanTilt>() == null)
        {
            cinemachineCamera.LookAt = cameraRoot;
        }
        boundCamera = cinemachineCamera;
    }

    private void ClearLocalCameraBinding()
    {
        if (!IsOwner)
        {
            return;
        }

        CinemachineCamera cinemachineCamera = boundCamera != null
            ? boundCamera
            : FindSceneCinemachineCamera(SceneManager.GetActiveScene());
        if (cinemachineCamera == null)
        {
            return;
        }

        if (cinemachineCamera.Follow == cameraRoot)
        {
            cinemachineCamera.Follow = null;
        }

        if (cinemachineCamera.GetComponent<CinemachinePanTilt>() == null &&
            cinemachineCamera.LookAt == cameraRoot)
        {
            cinemachineCamera.LookAt = null;
        }

        boundCamera = null;
    }

    private CinemachineCamera CreateRuntimeCinemachineCamera()
    {
        GameObject cameraObject = new GameObject(runtimeVirtualCameraName);
        CinemachineCamera cinemachineCamera = cameraObject.AddComponent<CinemachineCamera>();
        CinemachineThirdPersonFollow thirdPersonFollow = cameraObject.AddComponent<CinemachineThirdPersonFollow>();

        thirdPersonFollow.ShoulderOffset = new Vector3(followOffset.x, followOffset.y, 0f);
        thirdPersonFollow.CameraDistance = Mathf.Abs(followOffset.z);
        thirdPersonFollow.VerticalArmLength = 0f;
        cinemachineCamera.Priority.Value = 100;

        return cinemachineCamera;
    }

    private bool ShouldCreateRuntimeCameraInCurrentScene()
    {
        if (runtimeCameraSceneNames == null || runtimeCameraSceneNames.Length == 0)
        {
            return false;
        }

        string activeSceneName = SceneManager.GetActiveScene().name;
        for (int i = 0; i < runtimeCameraSceneNames.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(runtimeCameraSceneNames[i]) &&
                runtimeCameraSceneNames[i] == activeSceneName)
            {
                return true;
            }
        }

        return false;
    }

    private static CinemachineCamera FindSceneCinemachineCamera(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        GameObject[] rootObjects = scene.GetRootGameObjects();
        for (int i = 0; i < rootObjects.Length; i++)
        {
            CinemachineCamera camera = rootObjects[i].GetComponentInChildren<CinemachineCamera>(true);
            if (camera != null)
            {
                return camera;
            }
        }

        return null;
    }

    private void AutoAssignOwnerBehaviours()
    {
        List<Behaviour> behaviours = new List<Behaviour>();

        AddIfPresent(GetComponent<PlayerInput>(), behaviours);
        AddIfPresent(GetComponent<MouseController>(), behaviours);

        ownerOnlyBehaviours = behaviours.ToArray();
    }

    private static bool ShouldToggleBehaviourOwnership(Behaviour behaviour)
    {
        return behaviour is not PlayerController;
    }

    private static void AddIfPresent(Behaviour behaviour, ICollection<Behaviour> target)
    {
        if (behaviour != null)
        {
            target.Add(behaviour);
        }
    }

    private void EnsureNameLabel()
    {
        if (!showNameLabel || nameLabel != null)
        {
            return;
        }

        Transform existing = transform.Find("PlayerNameLabel");
        if (existing != null)
        {
            nameLabel = existing.GetComponent<TextMeshPro>();
        }

        if (nameLabel != null)
        {
            return;
        }

        GameObject labelObject = new GameObject("PlayerNameLabel");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = nameLabelOffset;
        labelObject.transform.localRotation = Quaternion.identity;

        nameLabel = labelObject.AddComponent<TextMeshPro>();
        nameLabel.alignment = TextAlignmentOptions.Center;
        nameLabel.fontSize = nameLabelFontSize;
        nameLabel.text = string.Empty;
        nameLabel.color = Color.white;
        nameLabel.outlineWidth = 0.2f;
    }

    private void UpdateNameLabel()
    {
        if (!showNameLabel)
        {
            return;
        }

        EnsureNameLabel();
        if (nameLabel == null)
        {
            return;
        }

        nameLabel.transform.localPosition = nameLabelOffset;
        nameLabel.text = $"Player {OwnerClientId + 1}";
    }

    private void UpdateNameLabelFacing()
    {
        if (nameLabel == null)
        {
            return;
        }

        Camera sceneCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (sceneCamera == null)
        {
            return;
        }

        nameLabel.transform.rotation = sceneCamera.transform.rotation;
    }

    private void UpdateTransformSync()
    {
        if (!syncTransformState || !IsSpawned)
        {
            return;
        }

        if (IsOwner)
        {
            syncedPosition.Value = transform.position;
            syncedRotation.Value = transform.rotation;
            return;
        }

        float positionLerpFactor = 1f - Mathf.Exp(-remotePositionLerpSpeed * Time.deltaTime);
        float rotationLerpFactor = 1f - Mathf.Exp(-remoteRotationLerpSpeed * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, syncedPosition.Value, positionLerpFactor);
        transform.rotation = Quaternion.Slerp(transform.rotation, syncedRotation.Value, rotationLerpFactor);
    }
}
