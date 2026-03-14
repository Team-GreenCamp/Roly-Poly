using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class NetworkSessionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;

    [Header("Connection Defaults")]
    [SerializeField] private string defaultAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;
    [SerializeField] private string hostListenAddress = "0.0.0.0";

    [Header("Scene Flow")]
    [SerializeField] private string gameSceneName = "GameScene";

    public event Action StateChanged;

    public string StatusMessage { get; private set; } = "오프라인";
    public int ConnectedPlayerCount { get; private set; }
    public string DefaultAddress => defaultAddress;
    public ushort DefaultPort => defaultPort;
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

    public void StartHost(string address, ushort port)
    {
        if (!CanStartSession())
        {
            return;
        }

        ConfigureHostTransport(address, port);
        isShuttingDown = false;

        if (!networkManager.StartHost())
        {
            SetStatus("호스트 시작에 실패했습니다.");
            return;
        }

        SetStatus("호스트를 시작했습니다. 다른 플레이어의 접속을 기다리는 중입니다.");
        UpdateConnectedPlayerCount();
    }

    public void StartClient(string address, ushort port)
    {
        if (!CanStartSession())
        {
            return;
        }

        ConfigureClientTransport(address, port);
        isShuttingDown = false;

        if (!networkManager.StartClient())
        {
            SetStatus("클라이언트 시작에 실패했습니다.");
            return;
        }

        SetStatus("호스트에 접속 중입니다...");
        UpdateConnectedPlayerCount();
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

    private void ConfigureHostTransport(string address, ushort port)
    {
        if (unityTransport == null)
        {
            return;
        }

        unityTransport.SetConnectionData(
            NormalizeAddress(address),
            port == 0 ? defaultPort : port,
            string.IsNullOrWhiteSpace(hostListenAddress) ? "0.0.0.0" : hostListenAddress);
    }

    private void ConfigureClientTransport(string address, ushort port)
    {
        if (unityTransport == null)
        {
            return;
        }

        unityTransport.SetConnectionData(NormalizeAddress(address), port == 0 ? defaultPort : port);
    }

    private string NormalizeAddress(string address)
    {
        return string.IsNullOrWhiteSpace(address) ? defaultAddress : address.Trim();
    }

    private void HandleServerStarted()
    {
        UpdateConnectedPlayerCount();

        if (networkManager != null && networkManager.IsHost)
        {
            SetStatus("호스트가 시작되었습니다. 로비에서 대기 중입니다.");
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
                SetStatus("호스트로 로비에 입장했습니다.");
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

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
