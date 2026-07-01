using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
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
    [SerializeField] private PlayerCharacterView characterView;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private bool lockCursorForOwner = true;
    [SerializeField] private string[] cursorLockSceneNames;

    [Header("Spawn")]
    [SerializeField] private bool separatePlayerSpawnPositions = true;
    [SerializeField] private float spawnRadius = 3f;
    [SerializeField] private Vector3 spawnCenterOffset;
    [SerializeField] private string lobbySceneName = "Lobby Scene";
    [SerializeField] private bool useLobbySpawnPoints = true;
    [SerializeField] private bool useSceneSpawnPoints = true;
    [SerializeField] private string[] sceneSpawnPointNames = { "Spawn Point", "SpawnPoint", "Player Spawn", "PlayerSpawn" };

    [Header("Lobby Spawn Presentation")]
    [SerializeField] private bool playLobbySpawnScaleIn = true;
    [SerializeField] private Transform spawnVisualRoot;
    [SerializeField] private float spawnScaleInDuration = 0.35f;
    [SerializeField] private float spawnStartScale = 0.1f;

    [Header("Lobby Character Outline")]
    [SerializeField] private bool enableLobbyCharacterOutline = true;
    [SerializeField] private Color localLobbyOutlineColor = new Color(0.45f, 0.95f, 1f, 1f);
    [SerializeField] private Color remoteLobbyOutlineColor = new Color(1f, 0.92f, 0.45f, 1f);
    [SerializeField] private float lobbyOutlineWidth = 2.5f;
    [SerializeField] private Outline.Mode lobbyOutlineMode = Outline.Mode.OutlineVisible;

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
    [SerializeField] private bool showNameLabelOnlyInLobby = true;
    [SerializeField] private Vector3 nameLabelOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] private float nameLabelFontSize = 4f;

    [Header("Ready Check Indicator")]
    [SerializeField] private Sprite readyCheckSprite;
    [SerializeField] private Color readyCheckColor = new Color(0.2f, 1f, 0.35f, 1f);
    [SerializeField] private bool showReadyCheckOnlyInLobby = true;
    [SerializeField] private Vector3 readyCheckOffset = new Vector3(0f, 2.75f, 0f);
    [SerializeField] private Vector3 readyCheckScale = new Vector3(0.35f, 0.35f, 0.35f);
    [SerializeField] private int readyCheckSortingOrder = 20;

    private readonly NetworkVariable<Vector3> syncedPosition =
        new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> syncedRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> syncedCharacterIndex =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> syncedReadyState =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private CinemachineCamera boundCamera;
    private TextMeshPro nameLabel;
    private SpriteRenderer readyCheckRenderer;
    private Coroutine spawnPresentationCoroutine;
    private Outline lobbyCharacterOutline;
    private Transform lobbyCharacterOutlineRoot;

    // 로비에서 소유자가 캐릭터를 순환 선택할 때 쓰는 입력(Previous=←/dpad←, Next=→/dpad→).
    private PlayerInput ownerPlayerInput;
    [SerializeField] private bool logCharacterSelectDebug = true; // 문제 진단용. 확인 후 끄면 됩니다.

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
        CacheReferences();
        ConfigureOwnershipAuthority();
        syncedCharacterIndex.OnValueChanged += HandleCharacterIndexChanged;
        syncedReadyState.OnValueChanged += HandleReadyStateChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        UpdateNameLabel();
        UpdateReadyCheckIndicator();

        if (IsServer)
        {
            AssignCharacterIndexFromSession();
            ApplySpawnPosition();
        }

        ApplyCharacterIndex(syncedCharacterIndex.Value);
        PlayLobbySpawnPresentation();
        ApplyLobbyCharacterOutlineState();

        ApplyOwnershipState(IsOwner);

        if (IsOwner)
        {
            BindLocalCamera();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        // 액션 참조는 캐싱하지 않고 매 프레임 현재 PlayerInput에서 새로 읽는다.
        // (PlayerInput이 활성/씬 전환 시 액션 인스턴스를 다시 만들면 캐싱한 참조가 죽기 때문)
        if (ownerPlayerInput == null)
        {
            ownerPlayerInput = GetComponent<PlayerInput>();
        }
        if (ownerPlayerInput == null || ownerPlayerInput.actions == null)
        {
            return;
        }

        InputAction next = ownerPlayerInput.actions.FindAction("Next", false);
        InputAction prev = ownerPlayerInput.actions.FindAction("Previous", false);
        bool nextPressed = next != null && next.WasPressedThisFrame();
        bool prevPressed = prev != null && prev.WasPressedThisFrame();

        if (!nextPressed && !prevPressed)
        {
            return;
        }

        if (logCharacterSelectDebug)
        {
            Debug.Log($"[CharSelect] key={(nextPressed ? "Next(→)" : "Prev(←)")} " +
                      $"inLobby={IsInLobbyScene()} scene='{SceneManager.GetActiveScene().name}' " +
                      $"count={CharacterCount} cur={CurrentCharacterIndex} isServer={IsServer}");
        }

        // 실제 캐릭터 순환은 로비에서만. (게임 씬에서는 방향키가 이동에 쓰임)
        if (!IsInLobbyScene())
        {
            return;
        }

        CycleCharacter(nextPressed ? 1 : -1);
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
        syncedCharacterIndex.OnValueChanged -= HandleCharacterIndexChanged;
        syncedReadyState.OnValueChanged -= HandleReadyStateChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        ClearLocalCameraBinding();
        ClearLobbyCharacterOutline();

        if (lockCursorForOwner && IsOwner && ShouldLockCursorInCurrentScene())
        {
            MouseController.SetCursorLock(false);
        }
    }

    private void LateUpdate()
    {
        UpdateTransformSync();
        UpdateNameLabelFacing();
        UpdateReadyCheckFacing();
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

        ApplyGameplayInputState(isOwner);
        ApplyLobbyCharacterOutlineState();
    }

    private void ApplyGameplayInputState(bool isOwner)
    {
        if (playerController == null)
        {
            return;
        }

        // 로비에서는 대기용 캐릭터만 보여주고, 실제 이동/회전 입력은 게임 씬에서만 허용한다.
        bool shouldEnableGameplayInput = isOwner && !IsInLobbyScene();
        playerController.SetGameplayInputEnabled(shouldEnableGameplayInput);
    }

    private bool ShouldLockCursorInCurrentScene()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (cursorLockSceneNames != null)
        {
            for (int i = 0; i < cursorLockSceneNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(cursorLockSceneNames[i]) &&
                    cursorLockSceneNames[i] == activeSceneName)
                {
                    return true;
                }
            }
        }

        // Stage 1처럼 Inspector 목록에 빠진 실제 게임 씬도 커서 잠금을 적용합니다.
        return !IsInLobbyScene();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (IsServer)
        {
            ApplySpawnPosition();
        }

        PlayLobbySpawnPresentation();
        ApplyOwnershipState(IsOwner);
        ApplyLobbyCharacterOutlineState();
        UpdateNameLabel();
        UpdateReadyCheckIndicator();

        if (IsOwner)
        {
            BindLocalCamera();
        }
    }

    private void CacheReferences()
    {
        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (cameraRoot == null)
        {
            Transform candidate = transform.Find("CameraRoot");
            if (candidate != null)
            {
                cameraRoot = candidate;
            }
        }

        if (characterView == null)
        {
            characterView = GetComponent<PlayerCharacterView>();
            if (characterView == null)
            {
                characterView = gameObject.AddComponent<PlayerCharacterView>();
            }
        }

        if (spawnVisualRoot == null)
        {
            spawnVisualRoot = FindDefaultSpawnVisualRoot();
        }
    }

    private void AssignCharacterIndexFromSession()
    {
        NetworkSessionManager sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        if (sessionManager != null)
        {
            syncedCharacterIndex.Value = sessionManager.GetOrAssignCharacterIndex(OwnerClientId);
        }
    }

    private void HandleCharacterIndexChanged(int previousValue, int newValue)
    {
        if (logCharacterSelectDebug)
        {
            Debug.Log($"[CharSelect] index changed {previousValue} → {newValue} (owner={IsOwner}) → 모델 적용");
        }
        ApplyCharacterIndex(newValue);
    }

    // ─────────────────────────────────────────────────────────────
    // 캐릭터 선택(로비). 랜덤 배정 대신 소유자가 직접 고른다. 중복 허용.
    // ─────────────────────────────────────────────────────────────
    public int CharacterCount => characterView != null ? characterView.CharacterCount : 0;
    public int CurrentCharacterIndex => syncedCharacterIndex.Value;

    // 로비 UI 버튼/키에서 호출: 현재 캐릭터에서 delta만큼 순환 선택.
    public void CycleCharacter(int delta)
    {
        int count = CharacterCount;
        if (count <= 0)
        {
            return;
        }

        int next = (((syncedCharacterIndex.Value + delta) % count) + count) % count;
        SelectCharacter(next);
    }

    // 특정 인덱스 선택 요청(소유자 → 서버 검증 → 전 클라 동기화).
    public void SelectCharacter(int index)
    {
        if (!IsOwner)
        {
            return;
        }

        int count = CharacterCount;
        if (count <= 0)
        {
            return;
        }

        index = Mathf.Clamp(index, 0, count - 1);
        if (index == syncedCharacterIndex.Value)
        {
            return;
        }

        if (IsServer)
        {
            SetCharacterOnServer(index);
        }
        else
        {
            RequestSetCharacterServerRpc(index);
        }
    }

    [ServerRpc]
    private void RequestSetCharacterServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        SetCharacterOnServer(index);
    }

    private void SetCharacterOnServer(int index)
    {
        if (!IsServer)
        {
            return;
        }

        int count = CharacterCount;
        if (count <= 0)
        {
            return;
        }

        // 서버가 범위를 검증하고 확정한다. (중복은 허용 → 다른 플레이어와 겹쳐도 OK)
        syncedCharacterIndex.Value = Mathf.Clamp(index, 0, count - 1);
    }

    private void HandleReadyStateChanged(bool previousValue, bool newValue)
    {
        UpdateReadyCheckIndicator();
    }

    private void ApplyCharacterIndex(int characterIndex)
    {
        if (characterView != null)
        {
            characterView.ApplyCharacter(characterIndex);
        }

        ApplyLobbyCharacterOutlineState();
    }

    public void SubmitReadyState(bool isReady)
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            ApplyReadyStateOnServer(OwnerClientId, isReady);
            return;
        }

        SubmitReadyStateServerRpc(isReady);
    }

    public bool BroadcastGameStart(string sceneName)
    {
        if (!IsSpawned || !IsServer || string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        // Ready가 통과하는 Player NetworkObject 경로로 Start 상태도 클라이언트에 직접 전달합니다.
        BroadcastGameStartClientRpc(new FixedString128Bytes(sceneName.Trim()));
        return true;
    }

    [ClientRpc]
    private void BroadcastGameStartClientRpc(FixedString128Bytes sceneName)
    {
        string targetSceneName = sceneName.ToString();
        if (string.IsNullOrWhiteSpace(targetSceneName) || IsServer)
        {
            return;
        }

        Debug.Log($"[ReadyFlow][GameStartRpc] received from player object. scene={targetSceneName}");
        // 실제 씬 로드는 Netcode SceneManager가 처리해야 Player NetworkObject spawn 순서가 보장됩니다.
    }

    [ServerRpc]
    private void SubmitReadyStateServerRpc(bool isReady, ServerRpcParams rpcParams = default)
    {
        // 클라이언트가 소유한 Player 오브젝트를 통해 서버에 준비 상태를 전달합니다.
        ApplyReadyStateOnServer(rpcParams.Receive.SenderClientId, isReady);
    }

    private void ApplyReadyStateOnServer(ulong clientId, bool isReady)
    {
        syncedReadyState.Value = isReady;

        NetworkSessionManager sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        if (sessionManager != null)
        {
            sessionManager.SetClientReadyFromNetwork(clientId, isReady);
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
        Quaternion targetRotation = transform.rotation;

        if (TryGetLobbySpawnPose(out Vector3 lobbyPosition, out Quaternion lobbyRotation))
        {
            targetPosition = lobbyPosition;
            targetRotation = lobbyRotation;
        }
        else if (TryGetSceneSpawnPose(out Vector3 scenePosition, out Quaternion sceneRotation))
        {
            targetPosition = scenePosition;
            targetRotation = sceneRotation;
        }

        ApplySpawnPose(targetPosition, targetRotation);

        if (IsSpawned)
        {
            ApplySpawnPoseClientRpc(targetPosition, targetRotation);
        }
    }

    [ClientRpc]
    private void ApplySpawnPoseClientRpc(Vector3 targetPosition, Quaternion targetRotation)
    {
        ApplySpawnPose(targetPosition, targetRotation);
    }

    private void ApplySpawnPose(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (TryGetComponent(out Rigidbody body))
        {
            Vector3 previousVelocity = body.linearVelocity;
            Vector3 previousAngularVelocity = body.angularVelocity;
            bool wasKinematic = body.isKinematic;

            body.isKinematic = true;
            body.position = targetPosition;
            body.rotation = targetRotation;
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
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }
    }

    private bool TryGetLobbySpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!useLobbySpawnPoints || !IsInLobbyScene())
        {
            return false;
        }

        LobbySpawnPointGroup spawnPointGroup = FindLobbySpawnPointGroup(SceneManager.GetActiveScene());
        return spawnPointGroup != null && spawnPointGroup.TryGetSpawnPose(OwnerClientId, out position, out rotation);
    }

    private bool TryGetSceneSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!useSceneSpawnPoints || IsInLobbyScene())
        {
            return false;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        LobbySpawnPointGroup spawnPointGroup = FindLobbySpawnPointGroup(activeScene);
        if (spawnPointGroup != null && spawnPointGroup.TryGetSpawnPose(OwnerClientId, out position, out rotation))
        {
            return true;
        }

        Transform spawnPoint = FindNamedSceneSpawnPoint(activeScene, OwnerClientId);
        if (spawnPoint == null)
        {
            return false;
        }

        // 맵 씬에 별도 Spawn Point 오브젝트만 둔 경우에도 해당 위치로 배치합니다.
        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        return true;
    }

    public bool IsInLobbyScene()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        return !string.IsNullOrWhiteSpace(lobbySceneName) && activeSceneName == lobbySceneName;
    }

    private void PlayLobbySpawnPresentation()
    {
        if (!playLobbySpawnScaleIn || !IsInLobbyScene())
        {
            return;
        }

        Transform visualRoot = characterView != null ? characterView.GetActiveCharacterRoot() : null;
        if (visualRoot == null)
        {
            visualRoot = spawnVisualRoot != null ? spawnVisualRoot : FindDefaultSpawnVisualRoot();
        }

        if (visualRoot == null)
        {
            return;
        }

        if (spawnPresentationCoroutine != null)
        {
            StopCoroutine(spawnPresentationCoroutine);
        }

        spawnPresentationCoroutine = StartCoroutine(PlayScaleInRoutine(visualRoot));
    }

    private void ApplyLobbyCharacterOutlineState()
    {
        if (!enableLobbyCharacterOutline || !IsInLobbyScene())
        {
            ClearLobbyCharacterOutline();
            return;
        }

        Transform visualRoot = characterView != null ? characterView.GetActiveCharacterRoot() : null;
        if (visualRoot == null)
        {
            visualRoot = spawnVisualRoot != null ? spawnVisualRoot : FindDefaultSpawnVisualRoot();
        }

        if (visualRoot == null)
        {
            ClearLobbyCharacterOutline();
            return;
        }

        if (lobbyCharacterOutlineRoot != null && lobbyCharacterOutlineRoot != visualRoot)
        {
            ClearLobbyCharacterOutline();
        }

        lobbyCharacterOutlineRoot = visualRoot;
        lobbyCharacterOutline = visualRoot.GetComponent<Outline>();
        if (lobbyCharacterOutline == null)
        {
            lobbyCharacterOutline = visualRoot.gameObject.AddComponent<Outline>();
        }

        // 로비에서는 배경과 캐릭터가 섞이지 않도록 현재 활성 모델에만 얇은 테두리를 준다.
        lobbyCharacterOutline.OutlineMode = lobbyOutlineMode;
        lobbyCharacterOutline.OutlineColor = IsOwner ? localLobbyOutlineColor : remoteLobbyOutlineColor;
        lobbyCharacterOutline.OutlineWidth = Mathf.Max(0f, lobbyOutlineWidth);
        lobbyCharacterOutline.enabled = true;
    }

    private void ClearLobbyCharacterOutline()
    {
        if (lobbyCharacterOutline != null)
        {
            lobbyCharacterOutline.enabled = false;
        }

        lobbyCharacterOutline = null;
        lobbyCharacterOutlineRoot = null;
    }

    private IEnumerator PlayScaleInRoutine(Transform visualRoot)
    {
        Vector3 targetScale = visualRoot.localScale;
        Vector3 startScale = targetScale * Mathf.Clamp(spawnStartScale, 0.01f, 1f);
        float duration = Mathf.Max(0.01f, spawnScaleInDuration);
        float elapsed = 0f;

        // Collider 대신 캐릭터 모델만 키워서 대기 화면 등장 연출을 보여줍니다.
        visualRoot.localScale = startScale;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            visualRoot.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);
            yield return null;
        }

        visualRoot.localScale = targetScale;
        spawnPresentationCoroutine = null;
    }

    private Transform FindDefaultSpawnVisualRoot()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == cameraRoot || child == null || child.name == "HoldPoint" || child.name == "PlayerNameLabel")
            {
                continue;
            }

            return child;
        }

        return null;
    }

    private static LobbySpawnPointGroup FindLobbySpawnPointGroup(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        GameObject[] rootObjects = scene.GetRootGameObjects();
        for (int i = 0; i < rootObjects.Length; i++)
        {
            LobbySpawnPointGroup spawnPointGroup = rootObjects[i].GetComponentInChildren<LobbySpawnPointGroup>(true);
            if (spawnPointGroup != null)
            {
                return spawnPointGroup;
            }
        }

        return null;
    }

    private Transform FindNamedSceneSpawnPoint(Scene scene, ulong ownerClientId)
    {
        if (!scene.IsValid() || !scene.isLoaded || sceneSpawnPointNames == null || sceneSpawnPointNames.Length == 0)
        {
            return null;
        }

        List<Transform> spawnPoints = new List<Transform>();
        GameObject[] rootObjects = scene.GetRootGameObjects();
        for (int i = 0; i < rootObjects.Length; i++)
        {
            CollectNamedSpawnPoints(rootObjects[i].transform, spawnPoints);
        }

        if (spawnPoints.Count == 0)
        {
            return null;
        }

        int index = (int)(ownerClientId % (ulong)spawnPoints.Count);
        return spawnPoints[index];
    }

    private void CollectNamedSpawnPoints(Transform root, ICollection<Transform> spawnPoints)
    {
        if (root == null)
        {
            return;
        }

        if (IsSceneSpawnPointName(root.name))
        {
            spawnPoints.Add(root);
        }

        for (int i = 0; i < root.childCount; i++)
        {
            CollectNamedSpawnPoints(root.GetChild(i), spawnPoints);
        }
    }

    private bool IsSceneSpawnPointName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        for (int i = 0; i < sceneSpawnPointNames.Length; i++)
        {
            string spawnPointName = sceneSpawnPointNames[i];
            if (!string.IsNullOrWhiteSpace(spawnPointName) && objectName.StartsWith(spawnPointName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (runtimeCameraSceneNames != null)
        {
            for (int i = 0; i < runtimeCameraSceneNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(runtimeCameraSceneNames[i]) &&
                    runtimeCameraSceneNames[i] == activeSceneName)
                {
                    return true;
                }
            }
        }

        // 목록에 등록되지 않은 게임 씬에서도 카메라가 없으면 런타임 카메라를 보강합니다.
        return !IsInLobbyScene();
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
            SetNameLabelVisible(false);
            return;
        }

        if (showNameLabelOnlyInLobby && !IsInLobbyScene())
        {
            // 게임 씬에서는 화면을 가리지 않도록 플레이어 닉네임 라벨을 숨긴다.
            SetNameLabelVisible(false);
            return;
        }

        EnsureNameLabel();
        if (nameLabel == null)
        {
            return;
        }

        nameLabel.transform.localPosition = nameLabelOffset;
        nameLabel.text = $"Player {OwnerClientId + 1}";
        SetNameLabelVisible(true);
    }

    private void UpdateNameLabelFacing()
    {
        if (nameLabel == null || !nameLabel.gameObject.activeSelf)
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

    private void UpdateReadyCheckIndicator()
    {
        if (readyCheckSprite == null)
        {
            SetReadyCheckVisible(false);
            return;
        }

        if (showReadyCheckOnlyInLobby && !IsInLobbyScene())
        {
            SetReadyCheckVisible(false);
            return;
        }

        EnsureReadyCheckIndicator();
        if (readyCheckRenderer == null)
        {
            return;
        }

        readyCheckRenderer.transform.localPosition = readyCheckOffset;
        readyCheckRenderer.transform.localScale = readyCheckScale;
        readyCheckRenderer.sprite = readyCheckSprite;
        readyCheckRenderer.color = readyCheckColor;
        readyCheckRenderer.sortingOrder = readyCheckSortingOrder;
        SetReadyCheckVisible(syncedReadyState.Value);
    }

    private void EnsureReadyCheckIndicator()
    {
        if (readyCheckRenderer != null)
        {
            return;
        }

        Transform existing = transform.Find("Ready Check Indicator");
        GameObject indicatorObject = existing != null ? existing.gameObject : new GameObject("Ready Check Indicator");
        indicatorObject.transform.SetParent(transform, false);

        readyCheckRenderer = indicatorObject.GetComponent<SpriteRenderer>();
        if (readyCheckRenderer == null)
        {
            readyCheckRenderer = indicatorObject.AddComponent<SpriteRenderer>();
        }

        // 스프라이트만 할당하면 런타임에 머리 위 체크 표시가 자동으로 생성됩니다.
        readyCheckRenderer.sprite = readyCheckSprite;
        readyCheckRenderer.color = readyCheckColor;
        readyCheckRenderer.sortingOrder = readyCheckSortingOrder;
    }

    private void UpdateReadyCheckFacing()
    {
        if (readyCheckRenderer == null || !readyCheckRenderer.gameObject.activeSelf)
        {
            return;
        }

        Camera sceneCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (sceneCamera == null)
        {
            return;
        }

        readyCheckRenderer.transform.rotation = sceneCamera.transform.rotation;
    }

    private void SetReadyCheckVisible(bool visible)
    {
        if (readyCheckRenderer != null)
        {
            readyCheckRenderer.gameObject.SetActive(visible);
        }
    }

    private void SetNameLabelVisible(bool visible)
    {
        if (nameLabel != null)
        {
            nameLabel.gameObject.SetActive(visible);
        }
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
