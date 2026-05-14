using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanvasButton = UnityEngine.UI.Button;
using CanvasImage = UnityEngine.UI.Image;
using HorizontalLayoutGroup = UnityEngine.UI.HorizontalLayoutGroup;
using LayoutElement = UnityEngine.UI.LayoutElement;
using VerticalLayoutGroup = UnityEngine.UI.VerticalLayoutGroup;

[DisallowMultipleComponent]
public class LobbyUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;

    [Header("Canvas Connection UI")]
    [SerializeField] private TMP_InputField addressInputField;
    [SerializeField] private TMP_InputField portInputField;
    [SerializeField] private CanvasButton hostButton;
    [SerializeField] private CanvasButton joinButton;
    [SerializeField] private CanvasButton leaveButton;
    [SerializeField] private CanvasButton startGameButton;
    [SerializeField] private CanvasButton readyButton;

    [Header("Canvas Status UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private GameObject offlineRoot;
    [SerializeField] private GameObject onlineRoot;
    [SerializeField] private GameObject roomSearchPanel;
    [SerializeField] private GameObject waitingRoot;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject roomEntryPanel;
    [SerializeField] private TMP_Text roomCodeDisplayText;

    [Header("Canvas Room List UI")]
    [SerializeField] private TMP_InputField roomNameInputField;
    [SerializeField] private CanvasButton refreshRoomsButton;
    [SerializeField] private TMP_Text roomListStatusText;
    [SerializeField] private Transform roomListContentRoot;
    [SerializeField] private GameObject roomListItemTemplate;

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
    private float nextRoomHeartbeatTime;
    private long activeBackendRoomId;
    private int backendConnectedPlayerCount = -1;

    private readonly List<GameObject> generatedRoomRows = new List<GameObject>();
    private RoomApiClient roomApiClient;

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
        EnsureCanvasWaitingLayout();
        roomApiClient = new RoomApiClient(backendBaseUrl);
    }

    private void OnEnable()
    {
        RegisterListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged += HandleSessionStateChanged;
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

        if (hostButton != null)
        {
            hostButton.onClick.AddListener(HandleHostClicked);
        }

        if (joinButton != null)
        {
            joinButton.onClick.AddListener(HandleJoinClicked);
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.AddListener(HandleLeaveClicked);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(HandleStartGameClicked);
        }

        if (readyButton != null)
        {
            readyButton.onClick.AddListener(HandleReadyClicked);
        }

        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.onClick.AddListener(HandleRefreshRoomsClicked);
        }

        listenersRegistered = true;
    }

    private void UnregisterListeners()
    {
        if (!listenersRegistered)
        {
            return;
        }

        if (hostButton != null)
        {
            hostButton.onClick.RemoveListener(HandleHostClicked);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(HandleJoinClicked);
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.RemoveListener(HandleLeaveClicked);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(HandleStartGameClicked);
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(HandleReadyClicked);
        }

        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.onClick.RemoveListener(HandleRefreshRoomsClicked);
        }

        listenersRegistered = false;
    }

    private void ApplyDefaultValues()
    {
        if (addressInputField != null && string.IsNullOrWhiteSpace(addressInputField.text))
        {
            addressInputField.text = string.Empty;
        }

        if (portInputField != null)
        {
            portInputField.gameObject.SetActive(false);
        }

        if (roomNameInputField != null && string.IsNullOrWhiteSpace(roomNameInputField.text))
        {
            roomNameInputField.text = GetRoomName();
        }

        if (roomListItemTemplate != null)
        {
            roomListItemTemplate.SetActive(false);
        }

        if (waitingRoot == null)
        {
            waitingRoot = onlineRoot;
        }

        if (roomSearchPanel == null)
        {
            roomSearchPanel = ResolveRoomSearchPanel();
        }
    }

    private async void HandleHostClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

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
            ShowWaitingRoomAfterHostStarted();
            await RegisterHostedRoomAsync();
        }
    }

    private async void HandleJoinClicked()
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
            RoomApiClient.RoomDto room = await FindOpenRoomByCodeAsync(joinCode);
            if (room != null)
            {
                await JoinBackendRoomAsync(room, "룸 코드로 방에 접속하는 중입니다...");
                return;
            }

            isJoiningRoom = true;
            RefreshUI();
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
            RefreshUI();
        }
    }

    private async void HandleRefreshRoomsClicked()
    {
        await RefreshRoomsAsync();
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
            RefreshUI();
        }
    }

    private async void HandleLeaveClicked()
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
        await RefreshRoomsAsync();
    }

    private void HandleReadyClicked()
    {
        isReady = !isReady;
        if (sessionManager != null)
        {
            sessionManager.SetLocalReady(isReady);
        }

        RefreshUI();
    }

    private async void HandleStartGameClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        sessionManager.StartGame();

        if (activeBackendRoomId > 0 && sessionManager.IsHost)
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

    private async Task RefreshRoomsAsync()
    {
        if (isRefreshingRooms)
        {
            return;
        }

        isRefreshingRooms = true;
        SetCanvasButtonInteractable(refreshRoomsButton, false);

        try
        {
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
            isRefreshingRooms = false;
            RefreshUI();
        }
    }

    private void RefreshUI()
    {
        bool isOnline = sessionManager != null && sessionManager.IsOnline;
        bool isServer = sessionManager != null && sessionManager.IsServer;
        bool isBusy = sessionManager != null && sessionManager.IsBusy;
        int playerCount = GetDisplayedPlayerCount();
        string statusMessage = GetStatusMessage();

        if (hostButton != null)
        {
            hostButton.interactable = !isOnline && !isBusy;
        }

        if (joinButton != null)
        {
            joinButton.interactable = !isOnline && !isBusy && !isJoiningRoom;
        }

        if (leaveButton != null)
        {
            leaveButton.interactable = isOnline && !isBusy;
        }

        if (startGameButton != null)
        {
            startGameButton.interactable = isServer && playerCount > 0 && !isBusy;
        }

        if (readyButton != null)
        {
            readyButton.interactable = isOnline && !isBusy;
            SetButtonLabel(readyButton, isReady ? "준비 완료" : "준비");
        }

        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.interactable = !isOnline && !isBusy && !isRefreshingRooms && !isJoiningRoom;
        }

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
            roomCodeDisplayText.text = $"접속 코드: {GetDisplayedRoomCode()}";
        }

        if (portInputField != null)
        {
            portInputField.interactable = false;
        }

        if (statusText != null)
        {
            statusText.text = statusMessage;
        }

        if (playerCountText != null)
        {
            playerCountText.text = $"현재 인원: {playerCount}";
        }

        if (offlineRoot != null)
        {
            offlineRoot.SetActive(!isOnline);
        }

        if (onlineRoot != null)
        {
            onlineRoot.SetActive(isOnline);
        }

        if (roomSearchPanel != null)
        {
            // 방에 들어간 뒤에는 방 찾기 목록을 닫고 대기 화면만 보여줍니다.
            roomSearchPanel.SetActive(!isOnline);
        }

        if (waitingRoot != null && waitingRoot != onlineRoot)
        {
            waitingRoot.SetActive(isOnline);
        }

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

    private void ShowWaitingRoomAfterHostStarted()
    {
        // 호스트 시작이 확인된 뒤에만 방 만들기 패널을 닫고 대기 UI를 보여줍니다.
        if (createRoomPanel != null)
        {
            createRoomPanel.SetActive(false);
        }

        if (roomSearchPanel != null)
        {
            roomSearchPanel.SetActive(false);
        }

        if (offlineRoot != null)
        {
            offlineRoot.SetActive(false);
        }

        if (onlineRoot != null)
        {
            onlineRoot.SetActive(true);
        }

        if (waitingRoot != null && waitingRoot != onlineRoot)
        {
            waitingRoot.SetActive(true);
        }

        if (roomEntryPanel != null)
        {
            roomEntryPanel.SetActive(true);
        }
    }

    private string GetStatusMessage()
    {
        if (sessionManager == null)
        {
            return "NetworkSessionManager가 연결되지 않았습니다.";
        }

        if (sessionManager.IsHost && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
        {
            return $"{sessionManager.StatusMessage}\nJoin Code: {sessionManager.CurrentJoinCode}";
        }

        return sessionManager.StatusMessage;
    }

    private int GetMaxPlayers()
    {
        return sessionManager != null ? sessionManager.MaxPlayers : 0;
    }

    private int GetDisplayedPlayerCount()
    {
        if (sessionManager != null && sessionManager.IsServer)
        {
            return sessionManager.ConnectedPlayerCount;
        }

        if (backendConnectedPlayerCount > 0)
        {
            return backendConnectedPlayerCount;
        }

        return sessionManager != null ? sessionManager.ConnectedPlayerCount : 0;
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
            generatedRoomRows.Add(row);
        }
    }

    private GameObject CreateRoomRow(RoomApiClient.RoomDto room)
    {
        // Canvas 템플릿이 있으면 복제하고, 없으면 기본 행 UI를 런타임에 만든다.
        GameObject row = roomListItemTemplate != null
            ? Instantiate(roomListItemTemplate, roomListContentRoot)
            : CreateDefaultRoomRow(roomListContentRoot);

        row.name = $"Room Row - {room.name}";
        row.SetActive(true);

        RoomListItemView itemView = row.GetComponent<RoomListItemView>();
        if (itemView != null)
        {
            itemView.Bind(room, () => HandleRoomJoinClicked(room));
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

        CanvasButton rowButton = row.GetComponentInChildren<CanvasButton>(true);
        if (rowButton == null)
        {
            rowButton = row.AddComponent<CanvasButton>();
            CanvasImage rowImage = row.GetComponent<CanvasImage>();
            rowButton.targetGraphic = rowImage;
        }

        rowButton.onClick.RemoveAllListeners();
        rowButton.onClick.AddListener(() => HandleRoomJoinClicked(room));
        rowButton.interactable = !isJoiningRoom && room.status == "open" && room.currentPlayers < room.maxPlayers;

        return row;
    }

    private GameObject CreateDefaultRoomRow(Transform parent)
    {
        GameObject row = new GameObject("Room Row", typeof(RectTransform), typeof(CanvasImage), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        RectTransform rectTransform = row.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.sizeDelta = new Vector2(0f, 64f);

        CanvasImage image = row.GetComponent<CanvasImage>();
        image.color = new Color(1f, 1f, 1f, 0.86f);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.minHeight = 64f;
        rowLayout.preferredHeight = 64f;

        RoomListItemView itemView = row.AddComponent<RoomListItemView>();

        GameObject textRoot = new GameObject("Room Text", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textRoot.transform.SetParent(row.transform, false);

        VerticalLayoutGroup textLayout = textRoot.GetComponent<VerticalLayoutGroup>();
        textLayout.spacing = 2f;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childForceExpandHeight = false;
        textLayout.childForceExpandWidth = true;

        LayoutElement textRootLayout = textRoot.GetComponent<LayoutElement>();
        textRootLayout.flexibleWidth = 1f;

        TMP_Text roomNameText = CreateRoomRowText("Room Name", textRoot.transform, 17f, FontStyles.Bold);
        TMP_Text roomDetailText = CreateRoomRowText("Room Detail", textRoot.transform, 13f, FontStyles.Normal);

        GameObject buttonObject = new GameObject("Join Button", typeof(RectTransform), typeof(CanvasImage), typeof(CanvasButton), typeof(LayoutElement));
        buttonObject.transform.SetParent(row.transform, false);

        CanvasImage buttonImage = buttonObject.GetComponent<CanvasImage>();
        buttonImage.color = new Color(0.35f, 0.58f, 0.96f, 1f);

        CanvasButton button = buttonObject.GetComponent<CanvasButton>();
        button.targetGraphic = buttonImage;

        LayoutElement buttonLayout = buttonObject.GetComponent<LayoutElement>();
        buttonLayout.minWidth = 76f;
        buttonLayout.preferredWidth = 76f;
        buttonLayout.minHeight = 38f;
        buttonLayout.preferredHeight = 38f;

        TMP_Text buttonText = CreateRoomRowText("입장", buttonObject.transform, 14f, FontStyles.Bold);
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;

        itemView.BindReferences(roomNameText, roomDetailText, button);

        return row;
    }

    private void EnsureCanvasWaitingLayout()
    {
        if (readyButton != null || waitingRoot == null)
        {
            return;
        }

        Transform parent = startGameButton != null && startGameButton.transform.parent != null
            ? startGameButton.transform.parent
            : waitingRoot.transform;

        GameObject buttonObject = new GameObject("Ready Button", typeof(RectTransform), typeof(CanvasImage), typeof(CanvasButton), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);

        CanvasImage buttonImage = buttonObject.GetComponent<CanvasImage>();
        buttonImage.color = new Color(0.35f, 0.58f, 0.96f, 1f);

        readyButton = buttonObject.GetComponent<CanvasButton>();
        readyButton.targetGraphic = buttonImage;

        LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
        layoutElement.minHeight = 44f;
        layoutElement.preferredHeight = 44f;

        TMP_Text label = CreateRoomRowText("준비", buttonObject.transform, 18f, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        if (startGameButton != null)
        {
            buttonObject.transform.SetSiblingIndex(startGameButton.transform.GetSiblingIndex());
        }
    }

    private GameObject ResolveRoomSearchPanel()
    {
        if (roomListContentRoot != null)
        {
            return roomListContentRoot.gameObject;
        }

        if (roomListStatusText != null)
        {
            return roomListStatusText.gameObject;
        }

        return refreshRoomsButton != null ? refreshRoomsButton.gameObject : null;
    }

    private static void SetButtonLabel(CanvasButton button, string text)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text;
        }
    }

    private TMP_Text CreateRoomRowText(string text, Transform parent, float fontSize, FontStyles fontStyle)
    {
        GameObject textObject = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = new Color(0.28f, 0.33f, 0.43f, 1f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.sizeDelta = Vector2.zero;

        return label;
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
        if (roomListStatusText != null)
        {
            roomListStatusText.text = message;
        }
    }

    private bool HasRoomListUI()
    {
        return roomListContentRoot != null || roomListStatusText != null || refreshRoomsButton != null;
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

    private static void SetCanvasButtonInteractable(CanvasButton button, bool interactable)
    {
        if (button != null)
        {
            button.interactable = interactable;
        }
    }
}
