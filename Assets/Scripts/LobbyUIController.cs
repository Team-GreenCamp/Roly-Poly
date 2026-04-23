using UnityEngine;
using UnityEngine.UIElements;
using TMPro;
using System.Threading.Tasks;
using CanvasButton = UnityEngine.UI.Button;

[DisallowMultipleComponent]
public class LobbyUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private UIDocument uiDocument;

    [Header("Canvas Connection UI")]
    [SerializeField] private TMP_InputField addressInputField;
    [SerializeField] private TMP_InputField portInputField;
    [SerializeField] private CanvasButton hostButton;
    [SerializeField] private CanvasButton joinButton;
    [SerializeField] private CanvasButton leaveButton;
    [SerializeField] private CanvasButton startGameButton;

    [Header("Canvas Status UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private GameObject offlineRoot;
    [SerializeField] private GameObject onlineRoot;

    [Header("Room List API")]
    [SerializeField] private string backendBaseUrl = "http://localhost:3000";
    [SerializeField] private string defaultRoomName = "Puzzle Room";
    [SerializeField] private string defaultMapId = "local-test";
    [SerializeField] private bool useRelayForRoomList;

    private bool listenersRegistered;
    private bool toolkitListenersRegistered;
    private bool isRefreshingRooms;
    private long activeBackendRoomId;
    private int backendConnectedPlayerCount = -1;

    private TextField joinCodeField;
    private TextField roomNameField;
    private Button toolkitHostButton;
    private Button toolkitJoinButton;
    private Button toolkitRefreshRoomsButton;
    private Button toolkitLeaveButton;
    private Button toolkitStartGameButton;
    private Label toolkitStatusLabel;
    private Label toolkitPlayerCountLabel;
    private Label toolkitJoinCodeLabel;
    private Label toolkitRoomListStatusLabel;
    private ScrollView toolkitRoomListView;
    private VisualElement toolkitOfflineRoot;
    private VisualElement toolkitOnlineRoot;
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

        ApplyDefaultValues();
        roomApiClient = new RoomApiClient(backendBaseUrl);
    }

    private void OnEnable()
    {
        RegisterListeners();
        BindToolkitUI();
        RegisterToolkitListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged += RefreshUI;
        }

        RefreshUI();
        _ = RefreshRoomsAsync();
    }

    private void OnDisable()
    {
        UnregisterListeners();
        UnregisterToolkitListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged -= RefreshUI;
        }
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

        listenersRegistered = false;
    }

    private void BindToolkitUI()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        // UXML의 name 값을 기준으로 UI Toolkit 요소를 한 번만 찾아둡니다.
        joinCodeField = root.Q<TextField>("join-code-field");
        roomNameField = root.Q<TextField>("room-name-field");
        toolkitHostButton = root.Q<Button>("host-button");
        toolkitJoinButton = root.Q<Button>("join-button");
        toolkitRefreshRoomsButton = root.Q<Button>("refresh-rooms-button");
        toolkitLeaveButton = root.Q<Button>("leave-button");
        toolkitStartGameButton = root.Q<Button>("start-game-button");
        toolkitStatusLabel = root.Q<Label>("status-label");
        toolkitPlayerCountLabel = root.Q<Label>("player-count-label");
        toolkitJoinCodeLabel = root.Q<Label>("join-code-label");
        toolkitRoomListStatusLabel = root.Q<Label>("room-list-status-label");
        toolkitRoomListView = root.Q<ScrollView>("room-list-view");
        toolkitOfflineRoot = root.Q<VisualElement>("offline-root");
        toolkitOnlineRoot = root.Q<VisualElement>("online-root");
    }

    private void RegisterToolkitListeners()
    {
        if (toolkitListenersRegistered)
        {
            return;
        }

        if (toolkitHostButton != null)
        {
            toolkitHostButton.clicked += HandleHostClicked;
        }

        if (toolkitJoinButton != null)
        {
            toolkitJoinButton.clicked += HandleJoinClicked;
        }

        if (toolkitRefreshRoomsButton != null)
        {
            toolkitRefreshRoomsButton.clicked += HandleRefreshRoomsClicked;
        }

        if (toolkitLeaveButton != null)
        {
            toolkitLeaveButton.clicked += HandleLeaveClicked;
        }

        if (toolkitStartGameButton != null)
        {
            toolkitStartGameButton.clicked += HandleStartGameClicked;
        }

        toolkitListenersRegistered = true;
    }

    private void UnregisterToolkitListeners()
    {
        if (!toolkitListenersRegistered)
        {
            return;
        }

        if (toolkitHostButton != null)
        {
            toolkitHostButton.clicked -= HandleHostClicked;
        }

        if (toolkitJoinButton != null)
        {
            toolkitJoinButton.clicked -= HandleJoinClicked;
        }

        if (toolkitRefreshRoomsButton != null)
        {
            toolkitRefreshRoomsButton.clicked -= HandleRefreshRoomsClicked;
        }

        if (toolkitLeaveButton != null)
        {
            toolkitLeaveButton.clicked -= HandleLeaveClicked;
        }

        if (toolkitStartGameButton != null)
        {
            toolkitStartGameButton.clicked -= HandleStartGameClicked;
        }

        toolkitListenersRegistered = false;
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
                SetRoomListStatus($"방 상태 변경 실패: {exception.Message}");
            }
        }
    }

    private string GetJoinCode()
    {
        string joinCode = string.Empty;

        if (joinCodeField != null)
        {
            joinCode = joinCodeField.value;
        }
        else if (addressInputField != null)
        {
            joinCode = addressInputField.text;
        }

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
            if (sessionManager != null && sessionManager.IsHost)
            {
                await GetRoomApiClient().CloseRoomAsync(roomId);
            }
            else
            {
                await GetRoomApiClient().LeaveRoomAsync(roomId);
            }
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
        SetToolkitButtonEnabled(toolkitRefreshRoomsButton, false);

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
        string statusMessage = GetStatusMessage();

        if (hostButton != null)
        {
            hostButton.interactable = !isOnline && !isBusy;
        }

        if (joinButton != null)
        {
            joinButton.interactable = !isOnline && !isBusy;
        }

        if (leaveButton != null)
        {
            leaveButton.interactable = isOnline && !isBusy;
        }

        if (startGameButton != null)
        {
            startGameButton.interactable = isServer && playerCount > 0 && !isBusy;
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

        RefreshToolkitUI(isOnline, isServer, isBusy, playerCount, statusMessage);
    }

    private void RefreshToolkitUI(bool isOnline, bool isServer, bool isBusy, int playerCount, string statusMessage)
    {
        // Canvas와 같은 세션 상태를 UI Toolkit 화면에도 반영합니다.
        SetToolkitButtonEnabled(toolkitHostButton, !isOnline && !isBusy);
        SetToolkitButtonEnabled(toolkitJoinButton, !isOnline && !isBusy);
        SetToolkitButtonEnabled(toolkitRefreshRoomsButton, !isOnline && !isBusy && !isRefreshingRooms);
        SetToolkitButtonEnabled(toolkitLeaveButton, isOnline && !isBusy);
        SetToolkitButtonEnabled(toolkitStartGameButton, isServer && playerCount > 0 && !isBusy);

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

        if (toolkitStatusLabel != null)
        {
            toolkitStatusLabel.text = statusMessage;
        }

        if (toolkitPlayerCountLabel != null)
        {
            toolkitPlayerCountLabel.text = $"{playerCount} / {GetMaxPlayers()}";
        }

        if (toolkitJoinCodeLabel != null)
        {
            string joinCode = sessionManager != null ? sessionManager.CurrentJoinCode : string.Empty;
            toolkitJoinCodeLabel.text = string.IsNullOrWhiteSpace(joinCode) ? "-" : joinCode;
        }

        SetToolkitDisplay(toolkitOfflineRoot, !isOnline);
        SetToolkitDisplay(toolkitOnlineRoot, isOnline);
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
        if (backendConnectedPlayerCount > 0)
        {
            return backendConnectedPlayerCount;
        }

        return sessionManager != null ? sessionManager.ConnectedPlayerCount : 0;
    }

    private string GetRoomName()
    {
        if (roomNameField != null && !string.IsNullOrWhiteSpace(roomNameField.value))
        {
            return roomNameField.value.Trim();
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
        if (toolkitRoomListView == null)
        {
            return;
        }

        toolkitRoomListView.Clear();

        foreach (RoomApiClient.RoomDto room in rooms)
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
            toolkitRoomListView.Add(row);
        }
    }

    private void SetRoomListStatus(string message)
    {
        if (toolkitRoomListStatusLabel != null)
        {
            toolkitRoomListStatusLabel.text = message;
        }
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

    private static void SetToolkitButtonEnabled(Button button, bool enabled)
    {
        if (button != null)
        {
            button.SetEnabled(enabled);
        }
    }

    private static void SetToolkitDisplay(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
