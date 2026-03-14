using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class LobbyUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;

    [Header("Connection UI")]
    [SerializeField] private TMP_InputField addressInputField;
    [SerializeField] private TMP_InputField portInputField;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button startGameButton;

    [Header("Status UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private GameObject offlineRoot;
    [SerializeField] private GameObject onlineRoot;

    private bool listenersRegistered;

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
    }

    private void OnEnable()
    {
        RegisterListeners();

        if (sessionManager != null)
        {
            sessionManager.StateChanged += RefreshUI;
        }

        RefreshUI();
    }

    private void OnDisable()
    {
        UnregisterListeners();

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

    private void ApplyDefaultValues()
    {
        if (sessionManager == null)
        {
            return;
        }

        if (addressInputField != null && string.IsNullOrWhiteSpace(addressInputField.text))
        {
            addressInputField.text = sessionManager.DefaultAddress;
        }

        if (portInputField != null && string.IsNullOrWhiteSpace(portInputField.text))
        {
            portInputField.text = sessionManager.DefaultPort.ToString();
        }
    }

    private void HandleHostClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        sessionManager.StartHost(GetAddress(), GetPort());
    }

    private void HandleJoinClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        sessionManager.StartClient(GetAddress(), GetPort());
    }

    private void HandleLeaveClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        sessionManager.Shutdown();
    }

    private void HandleStartGameClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        sessionManager.StartGame();
    }

    private string GetAddress()
    {
        if (addressInputField == null)
        {
            return sessionManager != null ? sessionManager.DefaultAddress : "127.0.0.1";
        }

        return string.IsNullOrWhiteSpace(addressInputField.text)
            ? (sessionManager != null ? sessionManager.DefaultAddress : "127.0.0.1")
            : addressInputField.text.Trim();
    }

    private ushort GetPort()
    {
        if (portInputField == null || string.IsNullOrWhiteSpace(portInputField.text))
        {
            return sessionManager != null ? sessionManager.DefaultPort : (ushort)7777;
        }

        return ushort.TryParse(portInputField.text, out ushort port)
            ? port
            : (sessionManager != null ? sessionManager.DefaultPort : (ushort)7777);
    }

    private void RefreshUI()
    {
        bool isOnline = sessionManager != null && sessionManager.IsOnline;
        bool isServer = sessionManager != null && sessionManager.IsServer;
        int playerCount = sessionManager != null ? sessionManager.ConnectedPlayerCount : 0;

        if (hostButton != null)
        {
            hostButton.interactable = !isOnline;
        }

        if (joinButton != null)
        {
            joinButton.interactable = !isOnline;
        }

        if (leaveButton != null)
        {
            leaveButton.interactable = isOnline;
        }

        if (startGameButton != null)
        {
            startGameButton.interactable = isServer && playerCount > 0;
        }

        if (addressInputField != null)
        {
            addressInputField.interactable = !isOnline;
        }

        if (portInputField != null)
        {
            portInputField.interactable = !isOnline;
        }

        if (statusText != null)
        {
            statusText.text = sessionManager != null
                ? sessionManager.StatusMessage
                : "NetworkSessionManager가 연결되지 않았습니다.";
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
    }
}
