using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class LobbyUIToolkitController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private UIDocument uiDocument;

    [Header("Room List API")]
    [SerializeField] private string backendBaseUrl = "http://localhost:3000";
    [SerializeField] private string defaultRoomName = "Puzzle Room";
    [SerializeField] private string defaultMapId = "local-test";
    [SerializeField] private bool useRelayForRoomList;

    private TextField joinCodeField;
    private TextField roomNameField;
    private Button hostButton;
    private Button joinButton;
    private Button refreshRoomsButton;
    private Button leaveButton;
    private Button startGameButton;
    private Button confirmCreateRoomButton;
    private Button cancelCreateRoomButton;
    private Label statusLabel;
    private Label playerCountLabel;
    private Label joinCodeLabel;
    private Label roomListStatusLabel;
    private ScrollView roomListView;
    private VisualElement offlineRoot;
    private VisualElement onlineRoot;
    private VisualElement createRoomPanel;

    private bool listenersRegistered;
    private bool isRefreshingRooms;
    private bool wasOnline;
    private long activeBackendRoomId;
    private int backendConnectedPlayerCount = -1;
    private RoomApiClient roomApiClient;

    private void Reset()
    {
        sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        uiDocument = GetComponent<UIDocument>();
    }

    private void Awake()
    {
        if (sessionManager == null)
        {
            sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        }

        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        roomApiClient = new RoomApiClient(backendBaseUrl);
    }

    private void OnEnable()
    {
        BindUI();
        RegisterListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged += RefreshUI;
        }

        ApplyDefaultValues();
        RefreshUI();
        _ = RefreshRoomsAsync();
    }

    private void OnDisable()
    {
        UnregisterListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged -= RefreshUI;
        }
    }

    private void BindUI()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        // UXML name 값과 스크립트 필드를 명시적으로 연결합니다.
        joinCodeField = root.Q<TextField>("join-code-field");
        roomNameField = root.Q<TextField>("room-name-field");
        hostButton = root.Q<Button>("host-button");
        joinButton = root.Q<Button>("join-button");
        refreshRoomsButton = root.Q<Button>("refresh-rooms-button");
        leaveButton = root.Q<Button>("leave-button");
        startGameButton = root.Q<Button>("start-game-button");
        confirmCreateRoomButton = root.Q<Button>("confirm-create-room-button");
        cancelCreateRoomButton = root.Q<Button>("cancel-create-room-button");
        statusLabel = root.Q<Label>("status-label");
        playerCountLabel = root.Q<Label>("player-count-label");
        joinCodeLabel = root.Q<Label>("join-code-label");
        roomListStatusLabel = root.Q<Label>("room-list-status-label");
        roomListView = root.Q<ScrollView>("room-list-view");
        offlineRoot = root.Q<VisualElement>("offline-root");
        onlineRoot = root.Q<VisualElement>("online-root");
        createRoomPanel = root.Q<VisualElement>("create-room-panel");

        EnsureCreateRoomPanelLayout(root);
    }

    private void RegisterListeners()
    {
        if (listenersRegistered)
        {
            return;
        }

        if (hostButton != null)
        {
            hostButton.clicked += HandleCreateRoomPanelOpenClicked;
        }

        if (joinButton != null)
        {
            joinButton.clicked += HandleJoinClicked;
        }

        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.clicked += HandleRefreshRoomsClicked;
        }

        if (leaveButton != null)
        {
            leaveButton.clicked += HandleLeaveClicked;
        }

        if (startGameButton != null)
        {
            startGameButton.clicked += HandleStartGameClicked;
        }

        if (confirmCreateRoomButton != null)
        {
            confirmCreateRoomButton.clicked += HandleConfirmCreateRoomClicked;
        }

        if (cancelCreateRoomButton != null)
        {
            cancelCreateRoomButton.clicked += HandleCreateRoomPanelCloseClicked;
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
            hostButton.clicked -= HandleCreateRoomPanelOpenClicked;
        }

        if (joinButton != null)
        {
            joinButton.clicked -= HandleJoinClicked;
        }

        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.clicked -= HandleRefreshRoomsClicked;
        }

        if (leaveButton != null)
        {
            leaveButton.clicked -= HandleLeaveClicked;
        }

        if (startGameButton != null)
        {
            startGameButton.clicked -= HandleStartGameClicked;
        }

        if (confirmCreateRoomButton != null)
        {
            confirmCreateRoomButton.clicked -= HandleConfirmCreateRoomClicked;
        }

        if (cancelCreateRoomButton != null)
        {
            cancelCreateRoomButton.clicked -= HandleCreateRoomPanelCloseClicked;
        }

        listenersRegistered = false;
    }

    private void ApplyDefaultValues()
    {
        if (roomNameField != null && string.IsNullOrWhiteSpace(roomNameField.value))
        {
            roomNameField.value = GetRoomName();
        }
    }

    private void HandleCreateRoomPanelOpenClicked()
    {
        SetCreateRoomPanelVisible(true);
    }

    private void HandleCreateRoomPanelCloseClicked()
    {
        SetCreateRoomPanelVisible(false);
    }

    private async void HandleConfirmCreateRoomClicked()
    {
        SetCreateRoomPanelVisible(false);
        await StartHostAndRegisterRoomAsync();
    }

    private async Task StartHostAndRegisterRoomAsync()
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
            await RegisterHostedRoomAsync();
        }
    }

    private async void HandleJoinClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        if (useRelayForRoomList)
        {
            await RunAsync(sessionManager.StartClientAsync(GetJoinCode()));
        }
        else
        {
            await RunAsync(sessionManager.StartLocalClientAsync(GetJoinCode()));
        }

        if (sessionManager.IsOnline)
        {
            await RefreshRoomsAsync();
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

        try
        {
            SetRoomListStatus("방 입장을 확인하는 중입니다...");
            RoomApiClient.RoomDto joinedRoom = await GetRoomApiClient().JoinRoomAsync(room.id);
            activeBackendRoomId = joinedRoom.id;
            backendConnectedPlayerCount = joinedRoom.currentPlayers;

            if (joinedRoom.connectionType == "local")
            {
                await RunAsync(sessionManager.StartLocalClientAsync(joinedRoom.connectionValue));
            }
            else if (joinedRoom.connectionType == "relay")
            {
                await RunAsync(sessionManager.StartClientAsync(joinedRoom.connectionValue));
            }
            else
            {
                SetRoomListStatus($"지원하지 않는 접속 방식입니다: {joinedRoom.connectionType}");
                return;
            }

            if (!sessionManager.IsOnline)
            {
                await GetRoomApiClient().LeaveRoomAsync(joinedRoom.id);
                activeBackendRoomId = 0;
                backendConnectedPlayerCount = -1;
                return;
            }

            RefreshUI();
            await RefreshRoomsAsync();
        }
        catch (System.Exception exception)
        {
            SetRoomListStatus($"방 입장 실패: {exception.Message}");
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
        RefreshUI();
        await RefreshRoomsAsync();
    }

    private async void HandleStartGameClicked()
    {
        if (sessionManager == null)
        {
            Debug.Log("[LobbyStart] UI Toolkit start clicked, but NetworkSessionManager is null.");
            return;
        }

        Debug.Log($"[LobbyStart] UI Toolkit start clicked. isHost={sessionManager.IsHost}, connected={sessionManager.ConnectedPlayerCount}, notReady=[{string.Join(", ", sessionManager.GetNotReadyRequiredClientIds())}], scene={sessionManager.CurrentGameSceneName}");
        sessionManager.StartGame();

        if (activeBackendRoomId > 0 && sessionManager.IsHost)
        {
            try
            {
                await GetRoomApiClient().SetRoomStatusAsync(activeBackendRoomId, "in_game");
            }
            catch (System.Exception exception)
            {
                SetRoomListStatus($"방 상태 변경 실패: {exception.Message}");
            }
        }
    }

    private string GetJoinCode()
    {
        string joinCode = joinCodeField != null ? joinCodeField.value : string.Empty;
        return string.IsNullOrWhiteSpace(joinCode) ? string.Empty : joinCode.Trim();
    }

    private string GetRoomName()
    {
        if (roomNameField != null && !string.IsNullOrWhiteSpace(roomNameField.value))
        {
            return roomNameField.value.Trim();
        }

        return string.IsNullOrWhiteSpace(defaultRoomName) ? "Puzzle Room" : defaultRoomName.Trim();
    }

    private async Task RunAsync(Task task)
    {
        if (task == null)
        {
            return;
        }

        await task;
    }

    private async Task RegisterHostedRoomAsync()
    {
        try
        {
            // 공개 방 목록에서 선택 입장할 수 있도록 현재 접속 정보를 백엔드에 등록합니다.
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
            backendConnectedPlayerCount = room.currentPlayers;
            SetRoomListStatus($"방이 등록되었습니다: {room.name}");
            RefreshUI();
            await RefreshRoomsAsync();
        }
        catch (System.Exception exception)
        {
            SetRoomListStatus($"방 등록 실패: {exception.Message}");
        }
    }

    private async Task ReleaseBackendRoomAsync()
    {
        if (activeBackendRoomId <= 0)
        {
            return;
        }

        long roomId = activeBackendRoomId;
        activeBackendRoomId = 0;

        try
        {
            // 호스트도 실제 나가기 처리로 current_players 를 줄여야 마지막 인원일 때 방이 삭제됩니다.
            await GetRoomApiClient().LeaveRoomAsync(roomId);
        }
        catch (System.Exception exception)
        {
            SetRoomListStatus($"방 상태 업데이트 실패: {exception.Message}");
        }
    }

    private async Task RefreshRoomsAsync()
    {
        if (isRefreshingRooms)
        {
            return;
        }

        isRefreshingRooms = true;
        SetButtonEnabled(refreshRoomsButton, false);

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

        SetButtonEnabled(hostButton, !isOnline && !isBusy);
        SetButtonEnabled(joinButton, !isOnline && !isBusy);
        SetButtonEnabled(refreshRoomsButton, !isOnline && !isBusy && !isRefreshingRooms);
        SetButtonEnabled(leaveButton, isOnline && !isBusy);
        SetButtonEnabled(startGameButton, isServer && playerCount > 0 && !isBusy);
        SetButtonEnabled(confirmCreateRoomButton, !isOnline && !isBusy);

        if (joinCodeField != null)
        {
            if (sessionManager != null && sessionManager.IsHost && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
            {
                joinCodeField.value = sessionManager.CurrentJoinCode;
            }

            joinCodeField.isReadOnly = isOnline;
            joinCodeField.SetEnabled(!isBusy);
        }

        if (roomNameField != null)
        {
            roomNameField.SetEnabled(!isOnline && !isBusy);
        }

        if (statusLabel != null)
        {
            statusLabel.text = GetStatusMessage();
        }

        if (playerCountLabel != null)
        {
            playerCountLabel.text = $"{playerCount} / {GetMaxPlayers()}";
        }

        if (joinCodeLabel != null)
        {
            string joinCode = sessionManager != null ? sessionManager.CurrentJoinCode : string.Empty;
            joinCodeLabel.text = string.IsNullOrWhiteSpace(joinCode) ? "-" : joinCode;
        }

        SetDisplay(offlineRoot, !isOnline);
        SetDisplay(onlineRoot, isOnline);

        if (wasOnline && !isOnline && activeBackendRoomId > 0)
        {
            // 네트워크가 먼저 끊겨도 백엔드 방 정리가 누락되지 않도록 한 번 더 보정합니다.
            _ = ReleaseBackendRoomAsync();
            backendConnectedPlayerCount = -1;
        }

        wasOnline = isOnline;
    }

    private void RebuildRoomList(RoomApiClient.RoomDto[] rooms)
    {
        if (roomListView == null)
        {
            return;
        }

        roomListView.Clear();

        if (rooms == null)
        {
            return;
        }

        for (int i = 0; i < rooms.Length; i++)
        {
            roomListView.Add(CreateRoomRow(rooms[i]));
        }
    }

    private VisualElement CreateRoomRow(RoomApiClient.RoomDto room)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("room-row");

        VisualElement info = new VisualElement();
        info.AddToClassList("room-info");

        Label nameLabel = new Label(string.IsNullOrWhiteSpace(room.name) ? "이름 없는 방" : room.name);
        nameLabel.AddToClassList("room-name");

        string mapText = string.IsNullOrWhiteSpace(room.mapId) ? "맵 미지정" : room.mapId;
        Label detailLabel = new Label($"{room.currentPlayers} / {room.maxPlayers}  {GetRoomStatusText(room.status)}  {mapText}");
        detailLabel.AddToClassList("room-detail");

        info.Add(nameLabel);
        info.Add(detailLabel);

        Button joinRoomButton = new Button(() => HandleRoomJoinClicked(room))
        {
            text = "입장"
        };
        joinRoomButton.AddToClassList("room-join-button");
        joinRoomButton.SetEnabled(room.status == "open" && room.currentPlayers < room.maxPlayers);

        row.Add(info);
        row.Add(joinRoomButton);

        return row;
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

    private RoomApiClient GetRoomApiClient()
    {
        if (roomApiClient == null)
        {
            roomApiClient = new RoomApiClient(backendBaseUrl);
        }

        return roomApiClient;
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

    private string GetBackendConnectionValue()
    {
        if (sessionManager != null && sessionManager.IsOnline && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
        {
            return sessionManager.CurrentJoinCode.Trim();
        }

        return GetJoinCode();
    }

    private void SetRoomListStatus(string message)
    {
        if (roomListStatusLabel != null)
        {
            roomListStatusLabel.text = message;
        }
    }

    private void EnsureCreateRoomPanelLayout(VisualElement root)
    {
        if (root == null)
        {
            return;
        }

        if (createRoomPanel == null)
        {
            createRoomPanel = new VisualElement
            {
                name = "create-room-panel"
            };
            createRoomPanel.AddToClassList("modal-overlay");
            createRoomPanel.AddToClassList("hidden");
            root.Add(createRoomPanel);
        }

        VisualElement dialog = createRoomPanel.Q<VisualElement>(className: "create-room-dialog");
        if (dialog == null)
        {
            dialog = new VisualElement();
            dialog.AddToClassList("create-room-dialog");
            createRoomPanel.Add(dialog);
        }

        MoveRoomNameFieldToCreateDialog(dialog);
        EnsureCreateRoomButtons(dialog);
        SetCreateRoomPanelVisible(false);
    }

    private void MoveRoomNameFieldToCreateDialog(VisualElement dialog)
    {
        if (dialog == null)
        {
            return;
        }

        RemoveInlineRoomNameLabel();

        if (dialog.Q<Label>(className: "dialog-title") == null)
        {
            Label titleLabel = new Label("방 만들기");
            titleLabel.AddToClassList("dialog-title");
            dialog.Insert(0, titleLabel);
        }

        if (!HasRoomNameLabel(dialog))
        {
            Label roomNameLabel = new Label("방 이름")
            {
                name = "create-room-name-label"
            };
            roomNameLabel.AddToClassList("field-label");
            dialog.Add(roomNameLabel);
        }

        if (roomNameField == null)
        {
            roomNameField = new TextField
            {
                name = "room-name-field",
                value = GetRoomName()
            };
            roomNameField.AddToClassList("room-name-field");
        }

        // 예전 UXML에 방 이름 입력칸이 왼쪽 메뉴에 남아 있으면 생성 패널로 옮깁니다.
        if (roomNameField.parent != dialog)
        {
            roomNameField.RemoveFromHierarchy();
            dialog.Add(roomNameField);
        }

        roomNameField.AddToClassList("dialog-input");
    }

    private void EnsureCreateRoomButtons(VisualElement dialog)
    {
        if (dialog == null)
        {
            return;
        }

        VisualElement buttonRow = dialog.Q<VisualElement>(className: "dialog-button-row");
        if (buttonRow == null)
        {
            buttonRow = new VisualElement();
            buttonRow.AddToClassList("dialog-button-row");
            dialog.Add(buttonRow);
        }

        if (confirmCreateRoomButton == null)
        {
            confirmCreateRoomButton = new Button
            {
                name = "confirm-create-room-button",
                text = "확인"
            };
            confirmCreateRoomButton.AddToClassList("dialog-confirm-button");
            buttonRow.Add(confirmCreateRoomButton);
        }

        if (cancelCreateRoomButton == null)
        {
            cancelCreateRoomButton = new Button
            {
                name = "cancel-create-room-button",
                text = "취소"
            };
            cancelCreateRoomButton.AddToClassList("dialog-cancel-button");
            buttonRow.Add(cancelCreateRoomButton);
        }
    }

    private void RemoveInlineRoomNameLabel()
    {
        if (offlineRoot == null)
        {
            return;
        }

        for (int i = offlineRoot.childCount - 1; i >= 0; i--)
        {
            if (offlineRoot.ElementAt(i) is Label label && label.text == "방 이름")
            {
                label.RemoveFromHierarchy();
            }
        }
    }

    private static bool HasRoomNameLabel(VisualElement container)
    {
        if (container == null)
        {
            return false;
        }

        for (int i = 0; i < container.childCount; i++)
        {
            if (container.ElementAt(i) is Label label && label.text == "방 이름")
            {
                return true;
            }
        }

        return false;
    }

    private void SetCreateRoomPanelVisible(bool visible)
    {
        if (createRoomPanel == null)
        {
            return;
        }

        // 방 만들기 버튼을 누른 뒤에만 이름 입력 패널을 보여줍니다.
        createRoomPanel.EnableInClassList("hidden", !visible);
        SetDisplay(createRoomPanel, visible);

        if (visible && roomNameField != null)
        {
            roomNameField.Focus();
        }
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

    private static void SetButtonEnabled(Button button, bool enabled)
    {
        if (button != null)
        {
            button.SetEnabled(enabled);
        }
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
