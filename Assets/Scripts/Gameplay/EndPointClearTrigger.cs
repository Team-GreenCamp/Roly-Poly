using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class EndPointClearTrigger : NetworkBehaviour
{
    [Header("Clear Flow")]
    [SerializeField] private float returnToLobbyDelay = 4f;
    [SerializeField] private string fallbackLobbySceneName = "Lobby Scene";
    [SerializeField] private string clearMessage = "CLEAR";
    [SerializeField] private string returningMessage = "Returning to lobby...";

    [Header("Optional UI")]
    [SerializeField] private GameObject clearPanel;
    [SerializeField] private TMP_Text clearMessageText;
    [SerializeField] private TMP_Text returningMessageText;

    private readonly Dictionary<ulong, int> playerContactCounts = new Dictionary<ulong, int>();
    private Coroutine returnRoutine;
    private bool clearTriggered;
    private GameObject runtimeClearPanel;

    private void Reset()
    {
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void Awake()
    {
        SetClearPanelVisible(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanServerProcessTrigger() || clearTriggered || !TryGetPlayerClientId(other, out ulong clientId))
        {
            return;
        }

        RegisterPlayerInside(clientId, true);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!CanServerProcessTrigger() || clearTriggered || !TryGetPlayerClientId(other, out ulong clientId))
        {
            return;
        }

        // 씬 로드/스폰 타이밍 때문에 Enter를 놓친 경우에도 End Point 안에 있으면 다시 등록합니다.
        RegisterPlayerInside(clientId, false);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!CanServerProcessTrigger() || clearTriggered || !TryGetPlayerClientId(other, out ulong clientId))
        {
            return;
        }

        if (!playerContactCounts.TryGetValue(clientId, out int contactCount))
        {
            return;
        }

        contactCount--;
        if (contactCount <= 0)
        {
            playerContactCounts.Remove(clientId);
        }
        else
        {
            playerContactCounts[clientId] = contactCount;
        }
    }

    private bool TryGetPlayerClientId(Collider other, out ulong clientId)
    {
        clientId = 0;
        NetworkObject networkObject = other.GetComponentInParent<NetworkObject>();
        if (networkObject == null || !networkObject.IsPlayerObject)
        {
            return false;
        }

        clientId = networkObject.OwnerClientId;
        return true;
    }

    private bool CanServerProcessTrigger()
    {
        if (IsServer)
        {
            return true;
        }

        NetworkManager activeNetworkManager = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
        return activeNetworkManager != null && activeNetworkManager.IsServer;
    }

    private void RegisterPlayerInside(ulong clientId, bool incrementContactCount)
    {
        // 플레이어에 여러 Collider가 있어도 한 명은 한 번만 입장 처리되도록 접촉 수를 셉니다.
        playerContactCounts.TryGetValue(clientId, out int contactCount);
        if (incrementContactCount)
        {
            playerContactCounts[clientId] = contactCount + 1;
        }
        else if (contactCount <= 0)
        {
            playerContactCounts[clientId] = 1;
        }

        TryTriggerClear();
    }

    private void TryTriggerClear()
    {
        if (!AreAllConnectedPlayersInside())
        {
            return;
        }

        clearTriggered = true;
        if (IsSpawned)
        {
            ShowClearPanelClientRpc();
        }
        else
        {
            SetClearPanelVisible(true);
        }

        if (returnRoutine == null)
        {
            returnRoutine = StartCoroutine(ReturnToLobbyAfterDelay());
        }
    }

    private bool AreAllConnectedPlayersInside()
    {
        if (NetworkManager == null || NetworkManager.ConnectedClientsIds.Count == 0)
        {
            return false;
        }

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!playerContactCounts.ContainsKey(clientId))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, returnToLobbyDelay));

        NetworkSessionManager sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        bool lobbyLoadStarted = false;
        if (sessionManager != null)
        {
            lobbyLoadStarted = sessionManager.ReturnToLobby();
        }

        if (!lobbyLoadStarted)
        {
            // 세션 매니저 경로가 실패해도 서버의 Netcode SceneManager로 로비 복귀를 한 번 더 시도합니다.
            TryLoadFallbackLobbyScene();
        }
    }

    private bool TryLoadFallbackLobbyScene()
    {
        NetworkManager activeNetworkManager = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
        if (activeNetworkManager == null || !activeNetworkManager.IsServer || activeNetworkManager.SceneManager == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(fallbackLobbySceneName))
        {
            return false;
        }

        SceneEventProgressStatus progressStatus =
            activeNetworkManager.SceneManager.LoadScene(fallbackLobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        return progressStatus == SceneEventProgressStatus.Started;
    }

    [ClientRpc]
    private void ShowClearPanelClientRpc()
    {
        SetClearPanelVisible(true);
    }

    private void SetClearPanelVisible(bool visible)
    {
        GameObject panel = clearPanel != null ? clearPanel : visible ? GetOrCreateRuntimeClearPanel() : runtimeClearPanel;
        if (panel != null)
        {
            panel.SetActive(visible);
        }

        if (clearMessageText != null)
        {
            clearMessageText.text = clearMessage;
        }

        if (returningMessageText != null)
        {
            returningMessageText.text = returningMessage;
        }
    }

    private GameObject GetOrCreateRuntimeClearPanel()
    {
        if (runtimeClearPanel != null)
        {
            return runtimeClearPanel;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Runtime Clear Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        runtimeClearPanel = new GameObject("Runtime Clear Panel");
        runtimeClearPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = runtimeClearPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image background = runtimeClearPanel.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.65f);

        clearMessageText = CreateRuntimeText("Clear Message", runtimeClearPanel.transform, clearMessage, 54, new Vector2(0f, 36f));
        returningMessageText = CreateRuntimeText("Returning Message", runtimeClearPanel.transform, returningMessage, 24, new Vector2(0f, -32f));

        return runtimeClearPanel;
    }

    private TMP_Text CreateRuntimeText(string objectName, Transform parent, string message, float fontSize, Vector2 anchoredPosition)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(720f, 100f);
        textRect.anchoredPosition = anchoredPosition;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = message;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return text;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        playerContactCounts.Remove(clientId);
        if (!clearTriggered)
        {
            TryTriggerClear();
        }
    }
}
