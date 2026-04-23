using System;
using System.Threading.Tasks;
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

    [Header("Local")]
    [SerializeField] private string localAdvertisedAddress = "127.0.0.1";
    [SerializeField] private string localListenAddress = "0.0.0.0";
    [SerializeField] private int localPort = 7777;

    [Header("Scene Flow")]
    [SerializeField] private string gameSceneName = "GameScene";

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

    private bool callbacksRegistered;
    private bool isShuttingDown;

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
        UpdateConnectedPlayerCount();
        SetStatus("세션을 종료했습니다.");
        isShuttingDown = false;
    }

    public void StartGame()
    {
        if (networkManager == null)
        {
            SetStatus("NetworkManager를 찾을 수 없습니다.");
            return;
        }

        if (!networkManager.IsServer)
        {
            SetStatus("게임 시작은 호스트만 할 수 있습니다.");
            return;
        }

        if (!networkManager.NetworkConfig.EnableSceneManagement)
        {
            SetStatus("NetworkManager에서 Enable Scene Management를 켜야 합니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            SetStatus("전환할 게임 씬 이름이 비어 있습니다.");
            return;
        }

        SceneEventProgressStatus progressStatus =
            networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);

        if (progressStatus != SceneEventProgressStatus.Started)
        {
            SetStatus($"게임 씬 전환을 시작하지 못했습니다. ({progressStatus})");
            return;
        }

        SetStatus($"게임 씬으로 전환 중입니다: {gameSceneName}");
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

        maxPlayers = Mathf.Max(2, maxPlayers);
        if (localPort <= 0 || localPort > ushort.MaxValue)
        {
            localPort = 7777;
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
            SetStatus($"플레이어가 로비에서 나갔습니다. 현재 인원: {ConnectedPlayerCount}");
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
