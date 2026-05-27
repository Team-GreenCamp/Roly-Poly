using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using System.Collections;
using CanvasButton = UnityEngine.UI.Button;

[DisallowMultipleComponent]
public class TitleSceneUIController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string lobbySceneName = "Lobby Scene";
    [SerializeField] private float sceneLoadDelay = 0.6f;

    [Header("Canvas UI")]
    [SerializeField] private CanvasButton playButton;

    [Header("UI Toolkit")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private string toolkitPlayButtonName = "play-button";

    private Button toolkitPlayButton;
    private bool canvasListenerRegistered;
    private bool toolkitListenerRegistered;
    private bool isSceneLoading;

    private void Reset()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }
    }

    private void OnEnable()
    {
        RegisterCanvasListeners();
        BindToolkitUI();
        RegisterToolkitListeners();
    }

    private void OnDisable()
    {
        UnregisterCanvasListeners();
        UnregisterToolkitListeners();
    }

    public void Play()
    {
        if (isSceneLoading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(lobbySceneName))
        {
            Debug.LogWarning("이동할 로비 씬 이름이 비어 있습니다.");
            return;
        }

        StartCoroutine(LoadLobbySceneAfterFade());
    }

    private IEnumerator LoadLobbySceneAfterFade()
    {
        isSceneLoading = true;

        if (playButton != null)
        {
            playButton.interactable = false;
        }

        // StartBtn의 Screen Fade 이벤트가 보일 시간을 준 뒤 로비 씬으로 이동합니다.
        float delay = Mathf.Max(0f, sceneLoadDelay);
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    private void RegisterCanvasListeners()
    {
        if (canvasListenerRegistered)
        {
            return;
        }

        if (playButton != null)
        {
            playButton.onClick.AddListener(Play);
        }

        canvasListenerRegistered = true;
    }

    private void UnregisterCanvasListeners()
    {
        if (!canvasListenerRegistered)
        {
            return;
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveListener(Play);
        }

        canvasListenerRegistered = false;
    }

    private void BindToolkitUI()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        toolkitPlayButton = uiDocument.rootVisualElement.Q<Button>(toolkitPlayButtonName);
    }

    private void RegisterToolkitListeners()
    {
        if (toolkitListenerRegistered)
        {
            return;
        }

        if (toolkitPlayButton != null)
        {
            toolkitPlayButton.clicked += Play;
        }

        toolkitListenerRegistered = true;
    }

    private void UnregisterToolkitListeners()
    {
        if (!toolkitListenerRegistered)
        {
            return;
        }

        if (toolkitPlayButton != null)
        {
            toolkitPlayButton.clicked -= Play;
        }

        toolkitListenerRegistered = false;
    }
}
