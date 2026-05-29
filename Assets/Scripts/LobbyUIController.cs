using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanvasButton = UnityEngine.UI.Button;
using CanvasImage = UnityEngine.UI.Image;
using HeatBoxButtonManager = Michsky.UI.Heat.BoxButtonManager;
using HeatButtonManager = Michsky.UI.Heat.ButtonManager;
using HeatChapterManager = Michsky.UI.Heat.ChapterManager;
using HeatPanelManager = Michsky.UI.Heat.PanelManager;

[DisallowMultipleComponent]
public class LobbyUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;

    [Header("External UI Controls")]
    [SerializeField] private TMP_InputField addressInputField;
    [SerializeField] private CanvasButton leaveButton;
    [SerializeField] private CanvasButton readyButton;
    [SerializeField] private GameObject roomEntryPanel;
    [SerializeField] private TMP_Text roomCodeDisplayText;
    [SerializeField] private string readyButtonObjectName = "Ready Button";
    [SerializeField] private string startButtonObjectName = "Start Button";

    [Header("Map UI")]
    [SerializeField] private string mapButtonObjectName = "Map Button";
    [SerializeField] private string chaptersPanelObjectName = "Chapters Panel";
    [SerializeField] private bool hideChaptersPanelOnStart = true;
    [SerializeField] private bool hideChaptersPanelAfterConfirm = true;
    [SerializeField] private List<MapSelection> mapSelections = new List<MapSelection>();

    [Header("Loading UI")]
    [SerializeField] private GameObject loadingRoot;
    [SerializeField] private TMP_Text loadingMessageText;

    [Header("Canvas Room List UI")]
    [SerializeField] private TMP_InputField roomNameInputField;
    [SerializeField] private Transform roomListContentRoot;
    [SerializeField] private GameObject roomListItemTemplate;
    [SerializeField] private List<GameObject> roomListPanelsToCloseOnRoomClick = new List<GameObject>();
    [SerializeField, HideInInspector] private GameObject roomListPanelToCloseOnRoomClick;

    [Header("Room List API")]
    [SerializeField] private string backendBaseUrl = "http://localhost:3000";
    [SerializeField] private string defaultRoomName = "Puzzle Room";
    [SerializeField] private string defaultMapId = "local-test";
    [SerializeField] private bool useRelayForRoomList;
    [SerializeField] private float roomJoinConnectionTimeout = 8f;
    [SerializeField] private float roomHeartbeatInterval = 10f;

    private bool listenersRegistered;
    private bool isRefreshingRooms;
    private bool isJoiningRoom;
    private bool isSyncingBackendPlayerCount;
    private bool isReady;
    private bool wasOnline;
    private bool activeBackendRoomOwnedByHost;
    private int loadingRequestCount;
    private float nextRoomHeartbeatTime;
    private long activeBackendRoomId;
    private int backendConnectedPlayerCount = -1;
    private bool mapPanelListenersRegistered;
    private bool readyButtonRuntimeListenerRegistered;
    private string selectedMapChapterId = string.Empty;
    private string selectedMapSceneName = string.Empty;

    private readonly List<GameObject> generatedRoomRows = new List<GameObject>();
    private RoomApiClient roomApiClient;
    private GameObject mapPanelRoot;
    private GameObject chaptersPanelRoot;
    private GameObject readyButtonRoot;
    private GameObject startButtonRoot;
    private HeatButtonManager readyHeatButton;
    private HeatButtonManager startHeatButton;
    private CanvasButton startButton;
    private HeatBoxButtonManager mapBoxButton;
    private HeatButtonManager mapHeatButton;
    private CanvasButton mapCanvasButton;
    private HeatChapterManager chapterManager;

    [System.Serializable]
    public class MapSelection
    {
        public string chapterId;
        public string mapId;
        public string sceneName;
        public Sprite buttonBackground;
    }

    private void Reset()
    {
        sessionManager = FindFirstObjectByType<NetworkSessionManager>();
    }

    private void Awake()
    {
        if (sessionManager == null)
        {
            sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        }

        ApplyDefaultValues();
        ResolveLobbyActionButtons();
        ResolveMapPanelReferences();
        HideChaptersPanelForLobbyStart();
        roomApiClient = new RoomApiClient(backendBaseUrl);
    }

    private void OnEnable()
    {
        RegisterListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged += HandleSessionStateChanged;
            sessionManager.MapSelectionChanged += HandleMapSelectionChanged;
        }

        RefreshUI();

        if (HasRoomListUI())
        {
            _ = RefreshRoomsAsync();
        }
    }

    private void OnDisable()
    {
        UnregisterListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged -= HandleSessionStateChanged;
            sessionManager.MapSelectionChanged -= HandleMapSelectionChanged;
        }
    }

    private void Update()
    {
        if (activeBackendRoomId <= 0 || !activeBackendRoomOwnedByHost || sessionManager == null || !sessionManager.IsServer)
        {
            return;
        }

        if (Time.unscaledTime < nextRoomHeartbeatTime)
        {
            return;
        }

        nextRoomHeartbeatTime = Time.unscaledTime + Mathf.Max(1f, roomHeartbeatInterval);
        _ = SyncHostedRoomPlayerCountAsync(true);
    }

    private void RegisterListeners()
    {
        if (listenersRegistered)
        {
            return;
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.AddListener(HandleLeaveClicked);
        }

        if (ShouldRegisterReadyButtonRuntimeListener())
        {
            readyButton.onClick.AddListener(HandleReadyClicked);
            readyButtonRuntimeListenerRegistered = true;
        }

        RegisterMapPanelListeners();
        listenersRegistered = true;
    }

    private void UnregisterListeners()
    {
        if (!listenersRegistered)
        {
            return;
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.RemoveListener(HandleLeaveClicked);
        }

        if (readyButton != null && readyButtonRuntimeListenerRegistered)
        {
            readyButton.onClick.RemoveListener(HandleReadyClicked);
            readyButtonRuntimeListenerRegistered = false;
        }

        UnregisterMapPanelListeners();
        listenersRegistered = false;
    }

    private bool ShouldRegisterReadyButtonRuntimeListener()
    {
        ResolveLobbyActionButtons();

        if (readyButton == null)
        {
            return false;
        }

        if (readyHeatButton != null && readyHeatButton.onClick.GetPersistentEventCount() > 0)
        {
            return false;
        }

        return readyButton.onClick.GetPersistentEventCount() == 0;
    }

    private void ResolveMapPanelReferences()
    {
        if (chaptersPanelRoot == null)
        {
            chaptersPanelRoot = FindSceneGameObjectByName(chaptersPanelObjectName);
        }

        if (mapPanelRoot == null && chaptersPanelRoot != null)
        {
            mapPanelRoot = ResolveMapPanelRoot(chaptersPanelRoot);
        }

        if (chapterManager == null && chaptersPanelRoot != null)
        {
            chapterManager = chaptersPanelRoot.GetComponent<HeatChapterManager>();
        }

        GameObject mapButtonObject = FindSceneGameObjectByName(mapButtonObjectName);
        if (mapButtonObject == null)
        {
            return;
        }

        if (mapHeatButton == null)
        {
            mapHeatButton = mapButtonObject.GetComponent<HeatButtonManager>();
        }

        if (mapBoxButton == null)
        {
            mapBoxButton = mapButtonObject.GetComponent<HeatBoxButtonManager>();
        }

        if (mapCanvasButton == null && mapHeatButton == null && mapBoxButton == null)
        {
            mapCanvasButton = mapButtonObject.GetComponent<CanvasButton>();
        }
    }

    private void ResolveLobbyActionButtons()
    {
        if (readyButtonRoot == null)
        {
            readyButtonRoot = FindSceneGameObjectByName(readyButtonObjectName);
        }

        if (startButtonRoot == null)
        {
            startButtonRoot = FindSceneGameObjectByName(startButtonObjectName);
        }

        if (readyHeatButton == null && readyButtonRoot != null)
        {
            readyHeatButton = readyButtonRoot.GetComponent<HeatButtonManager>();
        }

        if (readyButton == null && readyButtonRoot != null)
        {
            readyButton = readyButtonRoot.GetComponent<CanvasButton>();
        }

        if (readyButtonRoot == null && readyButton != null)
        {
            readyButtonRoot = readyButton.gameObject;
        }

        if (startHeatButton == null && startButtonRoot != null)
        {
            startHeatButton = startButtonRoot.GetComponent<HeatButtonManager>();
        }

        if (startButton == null && startButtonRoot != null)
        {
            startButton = startButtonRoot.GetComponent<CanvasButton>();
        }

        if (startButtonRoot == null && startButton != null)
        {
            startButtonRoot = startButton.gameObject;
        }
    }

    private void RegisterMapPanelListeners()
    {
        if (mapPanelListenersRegistered)
        {
            return;
        }

        ResolveMapPanelReferences();

        if (mapHeatButton != null)
        {
            mapHeatButton.onClick.AddListener(HandleMapButtonClicked);
        }

        if (mapBoxButton != null)
        {
            mapBoxButton.onClick.AddListener(HandleMapButtonClicked);
        }

        if (mapCanvasButton != null)
        {
            mapCanvasButton.onClick.AddListener(HandleMapButtonClicked);
        }

        mapPanelListenersRegistered = mapHeatButton != null || mapBoxButton != null || mapCanvasButton != null;
        UpdateMapButtonInteractable();
    }

    private void UnregisterMapPanelListeners()
    {
        if (!mapPanelListenersRegistered)
        {
            return;
        }

        if (mapHeatButton != null)
        {
            mapHeatButton.onClick.RemoveListener(HandleMapButtonClicked);
        }

        if (mapBoxButton != null)
        {
            mapBoxButton.onClick.RemoveListener(HandleMapButtonClicked);
        }

        if (mapCanvasButton != null)
        {
            mapCanvasButton.onClick.RemoveListener(HandleMapButtonClicked);
        }

        mapPanelListenersRegistered = false;
    }

    private void HideChaptersPanelForLobbyStart()
    {
        if (!hideChaptersPanelOnStart)
        {
            return;
        }

        SetMapPanelVisible(false);
    }

    private void HandleMapButtonClicked()
    {
        if (!CanInteractWithMapButton())
        {
            return;
        }

        ResolveMapPanelReferences();

        if (chaptersPanelRoot == null)
        {
            Debug.LogWarning("Chapters Panel을 찾을 수 없어 Map Button 클릭을 처리하지 못했습니다.", this);
            return;
        }

        SetMapPanelVisible(true);

        if (chapterManager != null)
        {
            chapterManager.InitializeChapters();
        }
    }

    private void UpdateMapButtonInteractable()
    {
        ResolveMapPanelReferences();

        bool canInteract = CanInteractWithMapButton();
        if (mapHeatButton != null)
        {
            mapHeatButton.Interactable(canInteract);
        }

        if (mapBoxButton != null)
        {
            mapBoxButton.Interactable(canInteract);
        }

        if (mapCanvasButton != null)
        {
            mapCanvasButton.interactable = canInteract;
        }

        if (!canInteract)
        {
            // 클라이언트는 맵 선택 UI를 열거나 유지할 수 없도록 닫아둔다.
            SetMapPanelVisible(false);
        }
    }

    private bool CanInteractWithMapButton()
    {
        if (sessionManager == null)
        {
            return true;
        }

        return !sessionManager.IsOnline || sessionManager.IsHost;
    }

    // ChapterManager의 Confirm 버튼 OnClick에서 연결해서 현재 챕터를 선택 맵으로 확정합니다.
    public void ConfirmSelectedMap()
    {
        ResolveMapPanelReferences();

        if (chapterManager == null || chapterManager.chapters == null || chapterManager.chapters.Count == 0)
        {
            Debug.LogWarning("선택할 ChapterManager가 없어 맵을 확정하지 못했습니다.", this);
            return;
        }

        int chapterIndex = Mathf.Clamp(chapterManager.currentChapterIndex, 0, chapterManager.chapters.Count - 1);
        string chapterId = chapterManager.chapters[chapterIndex].chapterID;
        ApplyMapSelection(chapterId);
    }

    public void SelectMap(string mapId)
    {
        ApplyMapSelection(mapId, mapId, null);
    }

    public void SelectMapByChapterId(string chapterId)
    {
        ApplyMapSelection(chapterId);
    }

    private void ApplyMapSelection(string chapterId)
    {
        MapSelection selection = FindMapSelection(chapterId);
        string fallbackMapId = NormalizeMapId(chapterId);
        string selectedMapId = selection != null && !string.IsNullOrWhiteSpace(selection.mapId)
            ? selection.mapId.Trim()
            : fallbackMapId;
        string selectedSceneName = selection != null ? selection.sceneName : null;

        ApplyMapSelection(chapterId, selectedMapId, selectedSceneName);
        ApplyMapButtonBackground(selection, chapterId);
    }

    private void ApplyMapSelection(string chapterId, string mapId, string sceneName)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            Debug.LogWarning("맵 ID가 비어 있어 맵 선택을 적용하지 않았습니다.", this);
            return;
        }

        defaultMapId = mapId.Trim();
        selectedMapChapterId = string.IsNullOrWhiteSpace(chapterId) ? string.Empty : chapterId.Trim();
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            selectedMapSceneName = sceneName.Trim();
        }

        if (sessionManager != null && sessionManager.IsServer)
        {
            sessionManager.SetSelectedMap(selectedMapChapterId, defaultMapId, selectedMapSceneName);
        }
        else if (sessionManager != null && !sessionManager.IsOnline && !string.IsNullOrWhiteSpace(sceneName))
        {
            sessionManager.SetGameSceneName(selectedMapSceneName);
        }

        if (hideChaptersPanelAfterConfirm)
        {
            SetMapPanelVisible(false);
        }

        if (activeBackendRoomOwnedByHost && activeBackendRoomId > 0)
        {
            _ = UpdateHostedRoomMapAsync(defaultMapId);
        }

        Debug.Log($"맵 선택 완료: chapter={chapterId}, mapId={defaultMapId}, scene={(sessionManager != null ? sessionManager.CurrentGameSceneName : "-")}");
    }

    private MapSelection FindMapSelection(string chapterId)
    {
        return FindMapSelection(chapterId, null);
    }

    private MapSelection FindMapSelection(string chapterId, string mapId)
    {
        if (mapSelections == null)
        {
            return null;
        }

        for (int i = 0; i < mapSelections.Count; i++)
        {
            MapSelection selection = mapSelections[i];
            if (selection == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(chapterId) && selection.chapterId == chapterId)
            {
                return selection;
            }

            if (!string.IsNullOrWhiteSpace(mapId) && selection.mapId == mapId)
            {
                return selection;
            }
        }

        return null;
    }

    private void ApplyMapButtonBackground(MapSelection selection, string chapterId)
    {
        Sprite background = selection != null ? selection.buttonBackground : null;
        if (background == null)
        {
            background = GetChapterBackground(chapterId);
        }

        if (background == null || mapBoxButton == null)
        {
            return;
        }

        // 선택 확정 후 로비의 Map Button 배경을 선택한 맵 이미지로 갱신합니다.
        mapBoxButton.SetBackground(background);
    }

    private Sprite GetChapterBackground(string chapterId)
    {
        if (chapterManager == null || chapterManager.chapters == null)
        {
            return null;
        }

        for (int i = 0; i < chapterManager.chapters.Count; i++)
        {
            HeatChapterManager.ChapterItem chapter = chapterManager.chapters[i];
            if (chapter != null && chapter.chapterID == chapterId)
            {
                return chapter.background;
            }
        }

        return null;
    }

    private static string NormalizeMapId(string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
        {
            return "local-test";
        }

        return chapterId.Trim().ToLowerInvariant().Replace(" ", "-");
    }

    private async Task UpdateHostedRoomMapAsync(string mapId)
    {
        if (activeBackendRoomId <= 0 || string.IsNullOrWhiteSpace(mapId))
        {
            return;
        }

        try
        {
            await GetRoomApiClient().SetRoomMapIdAsync(activeBackendRoomId, mapId);
            await RefreshRoomsAsync();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"방 맵 정보 갱신 실패: {exception.Message}");
        }
    }

    private void SetMapPanelVisible(bool visible)
    {
        ResolveMapPanelReferences();

        GameObject targetRoot = mapPanelRoot != null ? mapPanelRoot : chaptersPanelRoot;
        if (targetRoot == null)
        {
            return;
        }

        // Chapters Panel의 부모 Map Panel에 PanelManager가 있으면 자식만 꺼도 런타임에 다시 켜질 수 있다.
        targetRoot.SetActive(visible);
    }

    private static GameObject ResolveMapPanelRoot(GameObject chaptersPanel)
    {
        if (chaptersPanel == null || chaptersPanel.transform.parent == null)
        {
            return chaptersPanel;
        }

        Transform parent = chaptersPanel.transform.parent;
        return parent.GetComponent<HeatPanelManager>() != null ? parent.gameObject : chaptersPanel;
    }

    private static GameObject FindSceneGameObjectByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject candidate = transforms[i].gameObject;
            if (candidate.name == objectName && candidate.scene.IsValid())
            {
                return candidate;
            }
        }

        return null;
    }

    private void ApplyDefaultValues()
    {
        if (addressInputField != null && string.IsNullOrWhiteSpace(addressInputField.text))
        {
            addressInputField.text = string.Empty;
        }

        if (roomNameInputField != null && string.IsNullOrWhiteSpace(roomNameInputField.text))
        {
            roomNameInputField.text = GetRoomName();
        }

        if (roomListItemTemplate != null)
        {
            roomListItemTemplate.SetActive(false);
        }

        HideLoadingImmediate();
    }

    // Heat UI 버튼의 OnClick 이벤트에서도 직접 연결할 수 있도록 public으로 둡니다.
    public async void HandleHostClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        ShowLoading("방을 만드는 중입니다...");

        try
        {
            if (useRelayForRoomList)
            {
                await RunAsync(sessionManager.StartHostAsync());
            }
            else
            {
                await RunAsync(sessionManager.StartLocalHostAsync());
            }

            if (sessionManager.IsHost && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
            {
                PublishCurrentMapSelectionIfHost();
                ShowWaitingRoomAfterHostStarted();
                await RegisterHostedRoomAsync();
            }
        }
        finally
        {
            HideLoading();
        }
    }

    public async void HandleJoinClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        if (isJoiningRoom || sessionManager.IsOnline)
        {
            return;
        }

        string joinCode = GetJoinCode();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            SetRoomListStatus("입장할 룸 코드를 입력해주세요.");
            return;
        }

        try
        {
            ShowLoading("방 정보를 확인하는 중입니다...");
            RoomApiClient.RoomDto room = await FindOpenRoomByCodeAsync(joinCode);
            if (room != null)
            {
                await JoinBackendRoomAsync(room, "룸 코드로 방에 접속하는 중입니다...");
                return;
            }

            isJoiningRoom = true;
            RefreshUI();
            ShowLoading("방에 접속하는 중입니다...");
            SetRoomListStatus("일치하는 공개 방이 없어 직접 접속을 시도합니다...");

            if (useRelayForRoomList)
            {
                await RunAsync(sessionManager.StartClientAsync(joinCode));
            }
            else
            {
                await RunAsync(sessionManager.StartLocalClientAsync(joinCode));
            }

            if (sessionManager.IsOnline)
            {
                await RefreshRoomsAsync();
            }
        }
        catch (System.Exception exception)
        {
            SetRoomListStatus($"룸 코드 입장 실패: {exception.Message}");
        }
        finally
        {
            isJoiningRoom = false;
            HideLoading();
            RefreshUI();
        }
    }

    public async void HandleRefreshRoomsClicked()
    {
        await RefreshRoomsAsync(true);
    }

    private async void HandleRoomJoinClicked(RoomApiClient.RoomDto room)
    {
        if (sessionManager == null || room == null)
        {
            return;
        }

        if (isJoiningRoom || sessionManager.IsOnline)
        {
            return;
        }

        await JoinBackendRoomAsync(room, "방에 접속하는 중입니다...");
    }

    private async Task JoinBackendRoomAsync(RoomApiClient.RoomDto room, string loadingMessage)
    {
        if (sessionManager == null || room == null)
        {
            return;
        }

        try
        {
            isJoiningRoom = true;
            RefreshUI();
            ShowLoading(loadingMessage);
            SetRoomListStatus(loadingMessage);

            if (room.connectionType == "local")
            {
                await RunAsync(sessionManager.StartLocalClientAsync(room.connectionValue));
            }
            else if (room.connectionType == "relay")
            {
                await RunAsync(sessionManager.StartClientAsync(room.connectionValue));
            }
            else
            {
                SetRoomListStatus($"지원하지 않는 접속 방식입니다: {room.connectionType}");
                return;
            }

            if (!await WaitForClientConnectionAsync())
            {
                SetRoomListStatus("네트워크 방 접속에 실패했습니다.");
                if (sessionManager.IsOnline && !sessionManager.IsHost)
                {
                    sessionManager.Shutdown();
                }

                return;
            }

            // 실제 네트워크 접속을 시작한 뒤에만 백엔드 인원을 올립니다.
            RoomApiClient.RoomDto joinedRoom = await GetRoomApiClient().JoinRoomAsync(room.id);
            activeBackendRoomId = joinedRoom.id;
            activeBackendRoomOwnedByHost = false;
            backendConnectedPlayerCount = joinedRoom.currentPlayers;
            RefreshUI();
            await RefreshRoomsAsync();
        }
        catch (System.Exception exception)
        {
            SetRoomListStatus($"방 입장 실패: {exception.Message}");

            if (sessionManager.IsOnline && !sessionManager.IsHost)
            {
                sessionManager.Shutdown();
            }
        }
        finally
        {
            isJoiningRoom = false;
            HideLoading();
            RefreshUI();
        }
    }

    public async void HandleLeaveClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        await ReleaseBackendRoomAsync();
        sessionManager.Shutdown();
        backendConnectedPlayerCount = -1;
        isReady = false;
        RefreshUI();
        await RefreshRoomsAsync(true);
    }

    public void HandleReadyClicked()
    {
        if (sessionManager != null && sessionManager.IsHost)
        {
            return;
        }

        isReady = !isReady;
        if (sessionManager != null)
        {
            sessionManager.SetLocalReady(isReady);
        }

        RefreshUI();
    }

    public async void HandleStartGameClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        if (!sessionManager.CanHostStartGame)
        {
            return;
        }

        bool didStartGame = sessionManager.StartGame();

        if (didStartGame && activeBackendRoomId > 0 && sessionManager.IsHost)
        {
            try
            {
                await GetRoomApiClient().SetRoomStatusAsync(activeBackendRoomId, "in_game");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"방 상태 변경 실패: {exception.Message}");
            }
        }
    }

    private string GetJoinCode()
    {
        string joinCode = addressInputField != null ? addressInputField.text : string.Empty;

        return string.IsNullOrWhiteSpace(joinCode) ? string.Empty : joinCode.Trim();
    }

    private async Task RunAsync(Task task)
    {
        if (task == null)
        {
            return;
        }

        await task;
    }

    private async Task<bool> WaitForClientConnectionAsync()
    {
        float startTime = Time.realtimeSinceStartup;
        float timeout = Mathf.Max(0.5f, roomJoinConnectionTimeout);

        while (Time.realtimeSinceStartup - startTime < timeout)
        {
            if (sessionManager == null || !sessionManager.IsOnline)
            {
                return false;
            }

            if (sessionManager.IsConnectedClient)
            {
                return true;
            }

            await Task.Yield();
        }

        return false;
    }

    private async Task RegisterHostedRoomAsync()
    {
        try
        {
            // 방 목록에서 선택 입장할 수 있도록 현재 접속 정보를 백엔드에 저장합니다.
            RoomApiClient.RoomDto room = await GetRoomApiClient().CreateRoomAsync(new RoomApiClient.CreateRoomRequest
            {
                name = GetRoomName(),
                connectionType = useRelayForRoomList ? "relay" : "local",
                connectionValue = useRelayForRoomList ? sessionManager.CurrentJoinCode : sessionManager.LocalConnectionValue,
                mapId = defaultMapId,
                isPublic = true,
                maxPlayers = GetMaxPlayers(),
                currentPlayers = Mathf.Max(1, sessionManager.ConnectedPlayerCount)
            });

            activeBackendRoomId = room.id;
            activeBackendRoomOwnedByHost = true;
            nextRoomHeartbeatTime = Time.unscaledTime + Mathf.Max(1f, roomHeartbeatInterval);
            backendConnectedPlayerCount = room.currentPlayers;
            Debug.Log($"방이 등록되었습니다: {room.name}");
            RefreshUI();
            await RefreshRoomsAsync();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"방 등록 실패: {exception.Message}");
        }
    }

    private async Task ReleaseBackendRoomAsync()
    {
        if (activeBackendRoomId <= 0)
        {
            return;
        }

        long roomId = activeBackendRoomId;
        bool releaseAsHost = activeBackendRoomOwnedByHost;
        activeBackendRoomId = 0;
        activeBackendRoomOwnedByHost = false;

        try
        {
            if (releaseAsHost)
            {
                // 호스트 세션이 끝나면 남은 클라이언트도 유지될 수 없으므로 방을 0명 처리합니다.
                await GetRoomApiClient().SetRoomPlayerCountAsync(roomId, 0);
            }
            else
            {
                // 마지막 인원이 나가면 백엔드에서 방을 자동 삭제합니다.
                await GetRoomApiClient().LeaveRoomAsync(roomId);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"방 상태 업데이트 실패: {exception.Message}");
        }
    }

    private void HandleSessionStateChanged()
    {
        RefreshUI();

        if (activeBackendRoomId > 0 && activeBackendRoomOwnedByHost && sessionManager != null && sessionManager.IsServer)
        {
            _ = SyncHostedRoomPlayerCountAsync(false);
        }
    }

    private void HandleMapSelectionChanged(string chapterId, string mapId, string sceneName)
    {
        selectedMapChapterId = string.IsNullOrWhiteSpace(chapterId) ? string.Empty : chapterId.Trim();
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            selectedMapSceneName = sceneName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapId))
        {
            defaultMapId = mapId.Trim();
        }

        MapSelection selection = FindMapSelection(chapterId, mapId);
        string backgroundChapterId = selection != null && !string.IsNullOrWhiteSpace(selection.chapterId)
            ? selection.chapterId
            : chapterId;

        // 호스트가 확정한 맵 정보를 받은 클라이언트도 같은 Map Button 배경을 적용합니다.
        ApplyMapButtonBackground(selection, backgroundChapterId);
    }

    private void PublishCurrentMapSelectionIfHost()
    {
        if (sessionManager == null || !sessionManager.IsServer || string.IsNullOrWhiteSpace(defaultMapId))
        {
            return;
        }

        string sceneName = !string.IsNullOrWhiteSpace(selectedMapSceneName)
            ? selectedMapSceneName
            : sessionManager.CurrentGameSceneName;

        // 호스트가 방 생성 전에 고른 맵도 이후 접속하는 클라이언트에게 전달되도록 서버 상태에 올립니다.
        sessionManager.SetSelectedMap(selectedMapChapterId, defaultMapId, sceneName);
    }

    private async Task SyncHostedRoomPlayerCountAsync(bool force)
    {
        if (isSyncingBackendPlayerCount || activeBackendRoomId <= 0 || !activeBackendRoomOwnedByHost || sessionManager == null || !sessionManager.IsServer)
        {
            return;
        }

        int playerCount = Mathf.Max(1, sessionManager.ConnectedPlayerCount);
        if (!force && backendConnectedPlayerCount == playerCount)
        {
            return;
        }

        isSyncingBackendPlayerCount = true;

        try
        {
            // Netcode의 실제 서버 접속자 수를 방 목록 인원으로 맞춥니다.
            RoomApiClient.RoomDto room = await GetRoomApiClient().SetRoomPlayerCountAsync(activeBackendRoomId, playerCount);
            if (room != null)
            {
                backendConnectedPlayerCount = room.currentPlayers;
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"방 인원 동기화 실패: {exception.Message}");
        }
        finally
        {
            isSyncingBackendPlayerCount = false;
        }
    }

    private async Task RefreshRoomsAsync(bool showLoading = false)
    {
        if (isRefreshingRooms)
        {
            return;
        }

        isRefreshingRooms = true;
        try
        {
            if (showLoading)
            {
                ShowLoading("방 목록을 불러오는 중입니다...");
            }

            SetRoomListStatus("방 목록을 불러오는 중입니다...");
            RoomApiClient.RoomDto[] rooms = await GetRoomApiClient().GetRoomsAsync();
            SyncBackendPlayerCount(rooms);
            RebuildRoomList(rooms);
            SetRoomListStatus(rooms.Length == 0 ? "표시할 공개 방이 없습니다." : $"공개 방 {rooms.Length}개");
        }
        catch (System.Exception exception)
        {
            RebuildRoomList(System.Array.Empty<RoomApiClient.RoomDto>());
            Debug.LogWarning($"방 목록 로드 실패: {exception.Message}");
            SetRoomListStatus($"방 목록 로드 실패: {exception.Message}");
        }
        finally
        {
            if (showLoading)
            {
                HideLoading();
            }

            isRefreshingRooms = false;
            RefreshUI();
        }
    }

    private void RefreshUI()
    {
        bool isOnline = sessionManager != null && sessionManager.IsOnline;
        bool isBusy = sessionManager != null && sessionManager.IsBusy;
        bool isHost = sessionManager != null && sessionManager.IsHost;

        if (leaveButton != null)
        {
            leaveButton.interactable = isOnline && !isBusy;
        }

        UpdateLobbyActionButtons(isOnline, isHost, isBusy);

        if (roomNameInputField != null)
        {
            roomNameInputField.interactable = !isOnline && !isBusy;
        }

        if (addressInputField != null)
        {
            if (sessionManager != null && sessionManager.IsHost && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
            {
                addressInputField.text = sessionManager.CurrentJoinCode;
            }

            addressInputField.readOnly = isOnline;
            addressInputField.interactable = !isBusy;
        }

        if (roomCodeDisplayText != null)
        {
            // Relay 접속 코드를 대기 패널에서 바로 확인할 수 있게 표시합니다.
            roomCodeDisplayText.text = GetDisplayedRoomCode();
        }

        UpdateMapButtonInteractable();

        if (roomEntryPanel != null)
        {
            // 방에 들어간 상태에서만 입장 패널을 표시합니다.
            roomEntryPanel.SetActive(isOnline);
        }

        if (wasOnline && !isOnline && activeBackendRoomId > 0)
        {
            _ = ReleaseBackendRoomAsync();
            backendConnectedPlayerCount = -1;
            isReady = false;
        }

        wasOnline = isOnline;
    }

    private void UpdateLobbyActionButtons(bool isOnline, bool isHost, bool isBusy)
    {
        ResolveLobbyActionButtons();

        bool showReadyButton = isOnline && !isHost;
        bool showStartButton = isOnline && isHost;
        bool canStartGame = showStartButton && !isBusy && sessionManager != null && sessionManager.CanHostStartGame;

        // 호스트는 Start만, 클라이언트는 Ready만 보이도록 로비 액션 버튼을 분리합니다.
        SetLobbyActionButtonVisible(readyButtonRoot, showReadyButton);
        SetLobbyActionButtonVisible(startButtonRoot, showStartButton);
        SetLobbyActionButtonInteractable(readyButton, readyHeatButton, showReadyButton && !isBusy);
        SetLobbyActionButtonInteractable(startButton, startHeatButton, canStartGame);
    }

    private static void SetLobbyActionButtonVisible(GameObject buttonRoot, bool visible)
    {
        if (buttonRoot != null && buttonRoot.activeSelf != visible)
        {
            buttonRoot.SetActive(visible);
        }
    }

    private static void SetLobbyActionButtonInteractable(CanvasButton canvasButton, HeatButtonManager heatButton, bool interactable)
    {
        if (canvasButton != null)
        {
            canvasButton.interactable = interactable;
        }

        if (heatButton != null)
        {
            heatButton.Interactable(interactable);
        }
    }

    private void ShowWaitingRoomAfterHostStarted()
    {
        if (roomEntryPanel != null)
        {
            // 외부 UI 에셋의 패널 전환은 Inspector 이벤트가 맡고, 입장 정보 영역만 동기화합니다.
            roomEntryPanel.SetActive(true);
        }
    }

    private void ShowLoading(string message)
    {
        loadingRequestCount++;

        if (loadingMessageText != null)
        {
            loadingMessageText.text = message;
        }

        if (loadingRoot == null)
        {
            return;
        }

        // Heat UI의 UIPopup이 붙어 있으면 PlayIn을 쓰고, 없으면 단순 활성화로 처리합니다.
        loadingRoot.SetActive(true);
        if (HasLoadingPopup())
        {
            loadingRoot.SendMessage("PlayIn", SendMessageOptions.DontRequireReceiver);
        }
    }

    private void HideLoading()
    {
        if (loadingRequestCount > 0)
        {
            loadingRequestCount--;
        }

        if (loadingRequestCount > 0 || loadingRoot == null)
        {
            return;
        }

        if (HasLoadingPopup())
        {
            // Heat UI 팝업이면 닫힘 애니메이션을 우선 호출합니다.
            loadingRoot.SendMessage("PlayOut", SendMessageOptions.DontRequireReceiver);
            return;
        }

        loadingRoot.SetActive(false);
    }

    private void HideLoadingImmediate()
    {
        loadingRequestCount = 0;

        if (loadingRoot != null)
        {
            loadingRoot.SetActive(false);
        }
    }

    private bool HasLoadingPopup()
    {
        return loadingRoot != null && loadingRoot.GetComponent("UIPopup") != null;
    }

    private int GetMaxPlayers()
    {
        return sessionManager != null ? sessionManager.MaxPlayers : 0;
    }

    private string GetRoomName()
    {
        if (roomNameInputField != null && !string.IsNullOrWhiteSpace(roomNameInputField.text))
        {
            return roomNameInputField.text.Trim();
        }

        return string.IsNullOrWhiteSpace(defaultRoomName) ? "Puzzle Room" : defaultRoomName.Trim();
    }

    private RoomApiClient GetRoomApiClient()
    {
        if (roomApiClient == null)
        {
            roomApiClient = new RoomApiClient(backendBaseUrl);
        }

        return roomApiClient;
    }

    private void RebuildRoomList(RoomApiClient.RoomDto[] rooms)
    {
        ClearRoomRows();

        if (roomListContentRoot == null || rooms == null)
        {
            return;
        }

        for (int i = 0; i < rooms.Length; i++)
        {
            GameObject row = CreateRoomRow(rooms[i]);
            if (row != null)
            {
                generatedRoomRows.Add(row);
            }
        }
    }

    private GameObject CreateRoomRow(RoomApiClient.RoomDto room)
    {
        if (roomListItemTemplate == null)
        {
            Debug.LogWarning("방 목록 템플릿이 연결되지 않았습니다.", this);
            return null;
        }

        // Heat UI 템플릿을 복제해서 외부 UI 에셋의 레이아웃을 그대로 사용합니다.
        GameObject row = Instantiate(roomListItemTemplate, roomListContentRoot);

        row.name = $"Room Row - {room.name}";
        row.SetActive(true);

        RoomListItemView itemView = row.GetComponent<RoomListItemView>();
        if (itemView != null)
        {
            itemView.Bind(room, () => HandleRoomJoinClicked(room));
            BindRoomListPanelsToClose(itemView);
            return row;
        }

        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length > 0)
        {
            texts[0].text = string.IsNullOrWhiteSpace(room.name) ? "이름 없는 방" : room.name;
        }

        if (texts.Length > 1)
        {
            string mapText = string.IsNullOrWhiteSpace(room.mapId) ? "맵 미지정" : room.mapId;
            texts[1].text = $"{room.currentPlayers} / {room.maxPlayers}  {GetRoomStatusText(room.status)}  {mapText}";
        }

        bool canJoin = !isJoiningRoom && room.status == "open" && room.currentPlayers < room.maxPlayers;
        HeatBoxButtonManager heatRowButton = row.GetComponent<HeatBoxButtonManager>();
        if (heatRowButton != null)
        {
            // RoomContainer가 Heat BoxButton만 가진 경우에도 룸 입장 클릭을 연결합니다.
            heatRowButton.onClick.RemoveAllListeners();
            heatRowButton.onClick.AddListener(() => HandleRoomJoinClicked(room));
            heatRowButton.Interactable(canJoin);
            return row;
        }

        CanvasButton rowButton = row.GetComponentInChildren<CanvasButton>(true);
        if (rowButton == null)
        {
            rowButton = row.AddComponent<CanvasButton>();
            CanvasImage rowImage = row.GetComponent<CanvasImage>();
            rowButton.targetGraphic = rowImage;
        }

        rowButton.onClick.RemoveAllListeners();
        rowButton.onClick.AddListener(() => HandleRoomJoinClicked(room));
        rowButton.interactable = canJoin;

        return row;
    }

    private void BindRoomListPanelsToClose(RoomListItemView itemView)
    {
        if (itemView == null)
        {
            return;
        }

        if (HasRoomListPanelsToClose())
        {
            // 새 리스트 필드를 우선 사용해서 여러 Heat UI 패널을 순서대로 닫습니다.
            itemView.BindPanelsToClose(roomListPanelsToCloseOnRoomClick);
            return;
        }

        itemView.BindPanelToClose(roomListPanelToCloseOnRoomClick);
    }

    private bool HasRoomListPanelsToClose()
    {
        if (roomListPanelsToCloseOnRoomClick == null)
        {
            return false;
        }

        for (int i = 0; i < roomListPanelsToCloseOnRoomClick.Count; i++)
        {
            if (roomListPanelsToCloseOnRoomClick[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void SetButtonLabel(CanvasButton button, string text)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text;
        }
    }

    private void ClearRoomRows()
    {
        for (int i = 0; i < generatedRoomRows.Count; i++)
        {
            if (generatedRoomRows[i] != null)
            {
                Destroy(generatedRoomRows[i]);
            }
        }

        generatedRoomRows.Clear();
    }

    private void SetRoomListStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Debug.Log(message, this);
        }
    }

    private bool HasRoomListUI()
    {
        return roomListContentRoot != null;
    }

    private void SyncBackendPlayerCount(RoomApiClient.RoomDto[] rooms)
    {
        if (rooms == null || rooms.Length == 0)
        {
            return;
        }

        string connectionValue = GetBackendConnectionValue();

        for (int i = 0; i < rooms.Length; i++)
        {
            if ((activeBackendRoomId > 0 && rooms[i].id == activeBackendRoomId)
                || (!string.IsNullOrWhiteSpace(connectionValue) && rooms[i].connectionValue == connectionValue))
            {
                activeBackendRoomId = rooms[i].id;
                backendConnectedPlayerCount = rooms[i].currentPlayers;
                return;
            }
        }
    }

    private async Task<RoomApiClient.RoomDto> FindOpenRoomByCodeAsync(string joinCode)
    {
        string normalizedCode = NormalizeRoomCode(joinCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return null;
        }

        RoomApiClient.RoomDto[] rooms = await GetRoomApiClient().GetRoomsAsync();
        for (int i = 0; i < rooms.Length; i++)
        {
            if (NormalizeRoomCode(rooms[i].connectionValue) == normalizedCode)
            {
                return rooms[i];
            }
        }

        return null;
    }

    private string GetBackendConnectionValue()
    {
        if (sessionManager != null && sessionManager.IsOnline && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
        {
            return sessionManager.CurrentJoinCode.Trim();
        }

        return GetJoinCode();
    }

    private string GetDisplayedRoomCode()
    {
        if (sessionManager != null && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
        {
            return sessionManager.CurrentJoinCode.Trim();
        }

        string joinCode = GetJoinCode();
        return string.IsNullOrWhiteSpace(joinCode) ? "-" : joinCode;
    }

    private static string NormalizeRoomCode(string joinCode)
    {
        return string.IsNullOrWhiteSpace(joinCode) ? string.Empty : joinCode.Trim().ToUpperInvariant();
    }

    private static string GetRoomStatusText(string status)
    {
        return status switch
        {
            "open" => "대기중",
            "in_game" => "진행중",
            "closed" => "닫힘",
            _ => string.IsNullOrWhiteSpace(status) ? "알 수 없음" : status
        };
    }

}
