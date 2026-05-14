using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class NetworkSessionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;

    [Header("Relay")]
    [SerializeField] private int maxPlayers = 8;
    [SerializeField] private string relayConnectionType = "dtls";

    [Header("Characters")]
    [SerializeField] private int characterSlotCount = 4;

    [Header("Local")]
    [SerializeField] private string localAdvertisedAddress = "127.0.0.1";
    [SerializeField] private string localListenAddress = "0.0.0.0";
    [SerializeField] private int localPort = 7777;

    [Header("Scene Flow")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Debug")]
    [SerializeField] private bool logReadyDebug = true;

    public event Action StateChanged;

    public string StatusMessage { get; private set; } = "오프라인";
    public string CurrentJoinCode { get; private set; } = string.Empty;
    public string LocalConnectionValue => $"{GetLocalAdvertisedAddress()}:{GetLocalPort()}";
    public int ConnectedPlayerCount { get; private set; }
    public int MaxPlayers => maxPlayers;
    public bool IsBusy { get; private set; }
    public bool IsOnline => networkManager != null && (networkManager.IsServer || networkManager.IsClient);
    public bool IsHost => networkManager != null && networkManager.IsHost;
    public bool IsServer => networkManager != null && networkManager.IsServer;
    public bool IsClient => networkManager != null && networkManager.IsClient;
    public bool IsConnectedClient => networkManager != null && networkManager.IsConnectedClient;
    public bool LocalReady { get; private set; }

    private const string ReadyStateMessageName = "ReadyState";
    private bool callbacksRegistered;
    private bool isShuttingDown;
    private bool gameStartRequested;
    private readonly Dictionary<ulong, int> assignedCharacters = new Dictionary<ulong, int>();
    private readonly HashSet<ulong> readyClientIds = new HashSet<ulong>();
    private readonly List<int> availableCharacters = new List<int>();
    private System.Random characterRandom = new System.Random();

    private void Reset()
    {
        CacheReferences();
    }

    private void OnValidate()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
        UpdateConnectedPlayerCount();
    }

    private void OnEnable()
    {
        RegisterCallbacks();
        NotifyStateChanged();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    public async Task StartHostAsync()
    {
        if (!CanStartSession())
        {
            return;
        }

        SetBusy(true);
        SetStatus("Relay 서비스를 초기화하는 중입니다...");

        try
        {
            await EnsureRelayReadyAsync();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
            CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            unityTransport.SetRelayServerData(allocation.ToRelayServerData(GetRelayConnectionType()));
            isShuttingDown = false;
            ResetCharacterAssignments();
            ResetReadyState();

            if (!networkManager.StartHost())
            {
                CurrentJoinCode = string.Empty;
                SetStatus("Relay 호스트 시작에 실패했습니다.");
                return;
            }

            SetStatus($"Relay 호스트를 시작했습니다. Join Code: {CurrentJoinCode}");
            UpdateConnectedPlayerCount();
        }
        catch (Exception exception)
        {
            CurrentJoinCode = string.Empty;
            SetStatus($"Relay 호스트 생성 실패: {exception.Message}");
            Debug.LogException(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    public Task StartLocalHostAsync()
    {
        if (!CanStartSession())
        {
            return Task.CompletedTask;
        }

        SetBusy(true);
        SetStatus("로컬 호스트를 시작하는 중입니다...");

        try
        {
            // 로컬 개발에서는 Relay 없이 Unity Transport 주소/포트만 설정합니다.
            unityTransport.SetConnectionData(GetLocalAdvertisedAddress(), GetLocalPort(), GetLocalListenAddress());
            CurrentJoinCode = LocalConnectionValue;
            isShuttingDown = false;
            ResetCharacterAssignments();
            ResetReadyState();

            if (!networkManager.StartHost())
            {
                CurrentJoinCode = string.Empty;
                SetStatus("로컬 호스트 시작에 실패했습니다.");
                return Task.CompletedTask;
            }

            SetStatus($"로컬 호스트를 시작했습니다. 주소: {CurrentJoinCode}");
            UpdateConnectedPlayerCount();
        }
        catch (Exception exception)
        {
            CurrentJoinCode = string.Empty;
            SetStatus($"로컬 호스트 생성 실패: {exception.Message}");
            Debug.LogException(exception);
        }
        finally
        {
            SetBusy(false);
        }

        return Task.CompletedTask;
    }

    public async Task StartClientAsync(string joinCode)
    {
        if (!CanStartSession())
        {
            return;
        }

        string normalizedJoinCode = NormalizeJoinCode(joinCode);
        if (string.IsNullOrWhiteSpace(normalizedJoinCode))
        {
            SetStatus("참가하려면 Join Code를 입력해야 합니다.");
            return;
        }

        SetBusy(true);
        SetStatus("Relay 세션에 접속하는 중입니다...");

        try
        {
            await EnsureRelayReadyAsync();

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(normalizedJoinCode);
            unityTransport.SetRelayServerData(joinAllocation.ToRelayServerData(GetRelayConnectionType()));
            isShuttingDown = false;
            CurrentJoinCode = normalizedJoinCode;
            ResetReadyState();

            if (!networkManager.StartClient())
            {
                CurrentJoinCode = string.Empty;
                SetStatus("Relay 클라이언트 시작에 실패했습니다.");
                return;
            }

            SetStatus($"Join Code {normalizedJoinCode} 로 접속 중입니다...");
            UpdateConnectedPlayerCount();
        }
        catch (Exception exception)
        {
            CurrentJoinCode = string.Empty;
            SetStatus($"Relay 접속 실패: {exception.Message}");
            Debug.LogException(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    public Task StartLocalClientAsync(string connectionValue)
    {
        if (!CanStartSession())
        {
            return Task.CompletedTask;
        }

        if (!TryParseLocalConnectionValue(connectionValue, out string address, out ushort port))
        {
            SetStatus("로컬 방 주소 형식이 올바르지 않습니다. 예: 127.0.0.1:7777");
            return Task.CompletedTask;
        }

        SetBusy(true);
        SetStatus("로컬 세션에 접속하는 중입니다...");

        try
        {
            unityTransport.SetConnectionData(address, port);
            isShuttingDown = false;
            CurrentJoinCode = $"{address}:{port}";
            ResetReadyState();

            if (!networkManager.StartClient())
            {
                CurrentJoinCode = string.Empty;
                SetStatus("로컬 클라이언트 시작에 실패했습니다.");
                return Task.CompletedTask;
            }

            SetStatus($"{CurrentJoinCode} 로 접속 중입니다...");
            UpdateConnectedPlayerCount();
        }
        catch (Exception exception)
        {
            CurrentJoinCode = string.Empty;
            SetStatus($"로컬 접속 실패: {exception.Message}");
            Debug.LogException(exception);
        }
        finally
        {
            SetBusy(false);
        }

        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        if (networkManager == null)
        {
            SetStatus("NetworkManager를 찾을 수 없습니다.");
            return;
        }

        if (!IsOnline)
        {
            SetStatus("현재 실행 중인 네트워크 세션이 없습니다.");
            return;
        }

        isShuttingDown = true;
        networkManager.Shutdown();
        CurrentJoinCode = string.Empty;
        ResetReadyState();
        UpdateConnectedPlayerCount();
        SetStatus("세션을 종료했습니다.");
        isShuttingDown = false;
    }

    public void SetLocalReady(bool isReady)
    {
        LocalReady = isReady;
        LogReadyDebug($"Local ready changed. clientId={(networkManager != null ? networkManager.LocalClientId.ToString() : "-")}, ready={isReady}");

        if (networkManager == null || !IsOnline)
        {
            LogReadyDebug("Local ready was not sent. Session is offline.");
            NotifyStateChanged();
            return;
        }

        if (networkManager.IsServer)
        {
            SetClientReady(networkManager.LocalClientId, isReady);
            return;
        }

        if (!networkManager.IsConnectedClient)
        {
            LogReadyDebug("Local ready was not sent. Client is not connected yet.");
            NotifyStateChanged();
            return;
        }

        if (TrySubmitReadyThroughPlayerObject(isReady))
        {
            NotifyStateChanged();
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(isReady);
        networkManager.CustomMessagingManager.SendNamedMessage(
            ReadyStateMessageName,
            NetworkManager.ServerClientId,
            writer);
        LogReadyDebug($"Ready message sent to server. ready={isReady}");

        NotifyStateChanged();
    }

    public void SetClientReadyFromNetwork(ulong clientId, bool isReady)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (!IsConnectedClientId(clientId))
        {
            LogReadyDebug($"Ready ServerRpc ignored. Unknown clientId={clientId}, ready={isReady}");
            return;
        }

        LogReadyDebug($"Ready ServerRpc received. clientId={clientId}, ready={isReady}");
        SetClientReady(clientId, isReady);
    }

    public bool StartGame()
    {
        if (networkManager == null)
        {
            SetStatus("NetworkManager를 찾을 수 없습니다.");
            LogReadyDebug("StartGame failed. NetworkManager is null.");
            return false;
        }

        if (!networkManager.IsServer)
        {
            SetStatus("게임 시작은 호스트만 할 수 있습니다.");
            LogReadyDebug("StartGame failed. This instance is not server.");
            return false;
        }

        if (!networkManager.NetworkConfig.EnableSceneManagement)
        {
            SetStatus("NetworkManager에서 Enable Scene Management를 켜야 합니다.");
            LogReadyDebug("StartGame failed. Enable Scene Management is off.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            SetStatus("전환할 게임 씬 이름이 비어 있습니다.");
            LogReadyDebug("StartGame failed. Game scene name is empty.");
            return false;
        }

        LogReadyDebug($"StartGame requested. scene={gameSceneName}, connected=[{string.Join(", ", networkManager.ConnectedClientsIds)}], ready=[{string.Join(", ", readyClientIds)}]");

        SceneEventProgressStatus progressStatus =
            networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);

        if (progressStatus != SceneEventProgressStatus.Started)
        {
            SetStatus($"게임 씬 전환을 시작하지 못했습니다. ({progressStatus})");
            LogReadyDebug($"StartGame failed. SceneEventProgressStatus={progressStatus}");
            return false;
        }

        SetStatus($"게임 씬으로 전환 중입니다: {gameSceneName}");
        LogReadyDebug($"StartGame succeeded. Scene loading started: {gameSceneName}");
        return true;
    }

    private bool CanStartSession()
    {
        if (networkManager == null)
        {
            SetStatus("NetworkManager를 찾을 수 없습니다.");
            return false;
        }

        if (IsOnline)
        {
            SetStatus("이미 네트워크 세션이 실행 중입니다.");
            return false;
        }

        if (IsBusy)
        {
            SetStatus("현재 다른 네트워크 작업이 진행 중입니다.");
            return false;
        }

        return true;
    }

    private void CacheReferences()
    {
        if (networkManager == null)
        {
            networkManager = GetComponent<NetworkManager>();
        }

        if (unityTransport == null)
        {
            unityTransport = GetComponent<UnityTransport>();
        }

        if (unityTransport == null && networkManager != null)
        {
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;
        }

        if (networkManager != null && unityTransport != null && networkManager.NetworkConfig.NetworkTransport == null)
        {
            networkManager.NetworkConfig.NetworkTransport = unityTransport;
        }

        characterSlotCount = Mathf.Clamp(characterSlotCount, 2, 4);
        maxPlayers = Mathf.Clamp(maxPlayers, 2, characterSlotCount);
        if (localPort <= 0 || localPort > ushort.MaxValue)
        {
            localPort = 7777;
        }

        if (networkManager != null)
        {
            networkManager.NetworkConfig.ConnectionApproval = true;
        }
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered || networkManager == null)
        {
            return;
        }

        networkManager.OnServerStarted += HandleServerStarted;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ReadyStateMessageName, HandleReadyStateMessage);
        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered || networkManager == null)
        {
            return;
        }

        networkManager.OnServerStarted -= HandleServerStarted;
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.ConnectionApprovalCallback = null;
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyStateMessageName);
        callbacksRegistered = false;
    }

    private async Task EnsureRelayReadyAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync();
        }
        else
        {
            while (UnityServices.State == ServicesInitializationState.Initializing)
            {
                await Task.Yield();
            }
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private string GetRelayConnectionType()
    {
        if (string.IsNullOrWhiteSpace(relayConnectionType))
        {
            return "dtls";
        }

        return relayConnectionType.Trim().ToLowerInvariant();
    }

    private static string NormalizeJoinCode(string joinCode)
    {
        return string.IsNullOrWhiteSpace(joinCode) ? string.Empty : joinCode.Trim().ToUpperInvariant();
    }

    private string GetLocalAdvertisedAddress()
    {
        return string.IsNullOrWhiteSpace(localAdvertisedAddress) ? "127.0.0.1" : localAdvertisedAddress.Trim();
    }

    private string GetLocalListenAddress()
    {
        return string.IsNullOrWhiteSpace(localListenAddress) ? "0.0.0.0" : localListenAddress.Trim();
    }

    private ushort GetLocalPort()
    {
        return localPort > 0 && localPort <= ushort.MaxValue ? (ushort)localPort : (ushort)7777;
    }

    private static bool TryParseLocalConnectionValue(string connectionValue, out string address, out ushort port)
    {
        address = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(connectionValue))
        {
            return false;
        }

        string normalizedValue = connectionValue.Trim();
        int separatorIndex = normalizedValue.LastIndexOf(':');

        if (separatorIndex <= 0 || separatorIndex >= normalizedValue.Length - 1)
        {
            return false;
        }

        address = normalizedValue.Substring(0, separatorIndex).Trim();
        return !string.IsNullOrWhiteSpace(address)
            && ushort.TryParse(normalizedValue.Substring(separatorIndex + 1), out port)
            && port > 0;
    }

    private void HandleServerStarted()
    {
        UpdateConnectedPlayerCount();

        if (networkManager != null && networkManager.IsHost)
        {
            SetStatus($"호스트가 시작되었습니다. Join Code: {CurrentJoinCode}");
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        UpdateConnectedPlayerCount();

        if (networkManager == null)
        {
            return;
        }

        if (networkManager.LocalClientId == clientId)
        {
            if (networkManager.IsHost)
            {
                SetStatus($"호스트로 로비에 입장했습니다. Join Code: {CurrentJoinCode}");
                return;
            }

            SetStatus("로비에 입장했습니다.");
            return;
        }

        if (networkManager.IsServer)
        {
            GetOrAssignCharacterIndex(clientId);
            SetClientReady(clientId, false);
            SetStatus($"플레이어가 로비에 입장했습니다. 현재 인원: {ConnectedPlayerCount}");
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        UpdateConnectedPlayerCount();

        if (networkManager == null)
        {
            return;
        }

        if (networkManager.LocalClientId == clientId)
        {
            if (!isShuttingDown)
            {
                SetStatus("세션 연결이 종료되었습니다.");
            }

            CurrentJoinCode = string.Empty;

            return;
        }

        if (networkManager.IsServer)
        {
            readyClientIds.Remove(clientId);
            ReleaseCharacterIndex(clientId);
            SetStatus($"플레이어가 로비에서 나갔습니다. 현재 인원: {ConnectedPlayerCount}");
            TryStartGameWhenAllReady();
        }
    }

    private void HandleReadyStateMessage(ulong clientId, FastBufferReader reader)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        reader.ReadValueSafe(out bool isReady);
        LogReadyDebug($"Ready message received. clientId={clientId}, ready={isReady}");
        SetClientReady(clientId, isReady);
    }

    private bool TrySubmitReadyThroughPlayerObject(bool isReady)
    {
        NetworkObject playerObject = networkManager.LocalClient != null
            ? networkManager.LocalClient.PlayerObject
            : null;

        if (playerObject == null)
        {
            LogReadyDebug("Ready ServerRpc path unavailable. Local player object is null.");
            return false;
        }

        NetworkOwnedObjectActivator activator = playerObject.GetComponent<NetworkOwnedObjectActivator>();
        if (activator == null)
        {
            LogReadyDebug("Ready ServerRpc path unavailable. NetworkOwnedObjectActivator is missing on local player.");
            return false;
        }

        activator.SubmitReadyState(isReady);
        LogReadyDebug($"Ready ServerRpc sent through player object. ready={isReady}");
        return true;
    }

    private void SetClientReady(ulong clientId, bool isReady)
    {
        if (isReady)
        {
            readyClientIds.Add(clientId);
        }
        else
        {
            readyClientIds.Remove(clientId);
        }

        LogReadyDebug($"Ready state updated. clientId={clientId}, ready={isReady}, readyCount={readyClientIds.Count}, connectedCount={GetConnectedClientCountForDebug()}");

        // 서버가 접속 중인 모든 플레이어의 준비 상태를 기준으로 자동 시작을 판단합니다.
        TryStartGameWhenAllReady();
        NotifyStateChanged();
    }

    private void TryStartGameWhenAllReady()
    {
        if (networkManager == null || !networkManager.IsServer || gameStartRequested)
        {
            return;
        }

        if (networkManager.ConnectedClientsIds.Count == 0)
        {
            return;
        }

        List<ulong> notReadyClientIds = new List<ulong>();
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (!readyClientIds.Contains(clientId))
            {
                notReadyClientIds.Add(clientId);
            }
        }

        if (notReadyClientIds.Count > 0)
        {
            LogReadyDebug($"Waiting for ready clients. notReady=[{string.Join(", ", notReadyClientIds)}]");
            return;
        }

        LogReadyDebug($"All players ready. Starting scene: {gameSceneName}");
        gameStartRequested = true;
        if (!StartGame())
        {
            gameStartRequested = false;
        }
    }

    private void ResetReadyState()
    {
        LocalReady = false;
        gameStartRequested = false;
        readyClientIds.Clear();
        LogReadyDebug("Ready state reset.");
        NotifyStateChanged();
    }

    private int GetConnectedClientCountForDebug()
    {
        return networkManager != null ? networkManager.ConnectedClientsIds.Count : 0;
    }

    private bool IsConnectedClientId(ulong clientId)
    {
        if (networkManager == null)
        {
            return false;
        }

        foreach (ulong connectedClientId in networkManager.ConnectedClientsIds)
        {
            if (connectedClientId == clientId)
            {
                return true;
            }
        }

        return false;
    }

    private void LogReadyDebug(string message)
    {
        if (!logReadyDebug)
        {
            return;
        }

        // 준비 상태 자동 시작 흐름을 Unity Console에서 추적하기 위한 로그입니다.
        Debug.Log($"[ReadyFlow] {message}");
    }

    public int GetOrAssignCharacterIndex(ulong clientId)
    {
        if (assignedCharacters.TryGetValue(clientId, out int assignedIndex))
        {
            return assignedIndex;
        }

        if (availableCharacters.Count == 0)
        {
            return 0;
        }

        int characterIndex = availableCharacters[0];
        availableCharacters.RemoveAt(0);
        assignedCharacters[clientId] = characterIndex;
        return characterIndex;
    }

    private void ReleaseCharacterIndex(ulong clientId)
    {
        if (!assignedCharacters.TryGetValue(clientId, out int characterIndex))
        {
            return;
        }

        assignedCharacters.Remove(clientId);
        if (!availableCharacters.Contains(characterIndex))
        {
            availableCharacters.Add(characterIndex);
        }
    }

    private void ResetCharacterAssignments()
    {
        assignedCharacters.Clear();
        availableCharacters.Clear();

        for (int i = 0; i < characterSlotCount; i++)
        {
            availableCharacters.Add(i);
        }

        // 방마다 캐릭터 순서를 섞어서 입장 순서와 캐릭터가 고정되지 않게 합니다.
        for (int i = 0; i < availableCharacters.Count; i++)
        {
            int swapIndex = characterRandom.Next(i, availableCharacters.Count);
            (availableCharacters[i], availableCharacters[swapIndex]) =
                (availableCharacters[swapIndex], availableCharacters[i]);
        }
    }

    private void HandleConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        bool hasCharacterSlot = assignedCharacters.ContainsKey(request.ClientNetworkId) || availableCharacters.Count > 0;

        response.Approved = hasCharacterSlot;
        response.CreatePlayerObject = hasCharacterSlot;
        response.Reason = hasCharacterSlot ? string.Empty : "사용 가능한 캐릭터가 없습니다.";

        if (hasCharacterSlot)
        {
            GetOrAssignCharacterIndex(request.ClientNetworkId);
        }
    }

    private void UpdateConnectedPlayerCount()
    {
        if (networkManager == null)
        {
            ConnectedPlayerCount = 0;
            NotifyStateChanged();
            return;
        }

        if (networkManager.IsServer)
        {
            ConnectedPlayerCount = networkManager.ConnectedClientsIds.Count;
        }
        else if (networkManager.IsConnectedClient)
        {
            ConnectedPlayerCount = 1;
        }
        else
        {
            ConnectedPlayerCount = 0;
        }

        NotifyStateChanged();
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        NotifyStateChanged();
    }

    private void SetBusy(bool isBusy)
    {
        IsBusy = isBusy;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
