using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;

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

        await RunAsync(sessionManager.StartHostAsync());
    }

    private async void HandleJoinClicked()
    {
        if (sessionManager == null)
        {
            return;
        }

        await RunAsync(sessionManager.StartClientAsync(GetJoinCode()));
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

    private string GetJoinCode()
    {
        if (addressInputField == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(addressInputField.text)
            ? string.Empty
            : addressInputField.text.Trim();
    }

    private async Task RunAsync(Task task)
    {
        if (task == null)
        {
            return;
        }

        await task;
    }

    private void RefreshUI()
    {
        bool isOnline = sessionManager != null && sessionManager.IsOnline;
        bool isServer = sessionManager != null && sessionManager.IsServer;
        bool isBusy = sessionManager != null && sessionManager.IsBusy;
        int playerCount = sessionManager != null ? sessionManager.ConnectedPlayerCount : 0;

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
            if (sessionManager == null)
            {
                statusText.text = "NetworkSessionManager가 연결되지 않았습니다.";
            }
            else if (sessionManager.IsHost && !string.IsNullOrWhiteSpace(sessionManager.CurrentJoinCode))
            {
                statusText.text = $"{sessionManager.StatusMessage}\nJoin Code: {sessionManager.CurrentJoinCode}";
            }
            else
            {
                statusText.text = sessionManager.StatusMessage;
            }
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
