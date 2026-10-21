using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 호스트 이탈·네트워크 끊김·입장 거절 등으로 세션이 끊겼을 때 이유를 화면 상단에 잠시 띄우고,
// 필요하면 로비 씬으로 돌려보낸다. 씬 전환을 넘어 표시되도록 DontDestroyOnLoad 오브젝트로 만든다.
[DisallowMultipleComponent]
public class ConnectionLostNotice : MonoBehaviour
{
    private const float DisplaySeconds = 5f;
    private const float FadeSeconds = 0.4f;

    private static ConnectionLostNotice instance;

    private CanvasGroup canvasGroup;
    private TMP_Text messageText;
    private Coroutine routine;

    public static void Show(string message, string returnSceneName)
    {
        if (instance == null)
        {
            GameObject noticeObject = new GameObject("[ConnectionLostNotice]");
            DontDestroyOnLoad(noticeObject);
            instance = noticeObject.AddComponent<ConnectionLostNotice>();
        }

        instance.Begin(message, returnSceneName);
    }

    private void Awake()
    {
        EnsureUIComponents();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Begin(string message, string returnSceneName)
    {
        EnsureUIComponents();
        messageText.text = message;

        if (routine != null)
        {
            StopCoroutine(routine);
        }
        routine = StartCoroutine(NoticeRoutine(returnSceneName));
    }

    private IEnumerator NoticeRoutine(string returnSceneName)
    {
        canvasGroup.alpha = 1f;

        if (!string.IsNullOrWhiteSpace(returnSceneName))
        {
            // InGamePauseMenu.LeaveGame과 같은 순서: NGO 종료가 끝난 뒤 남은 NetworkManager를 제거하고
            // 로비로 이동해 로비 씬의 NetworkManager와 중복되지 않게 한다.
            NetworkManager manager = NetworkManager.Singleton;
            while (manager != null && manager.IsListening)
            {
                yield return null;
            }

            if (manager != null)
            {
                Destroy(manager.gameObject);
            }
            yield return null;

            MouseController.SetCursorLock(false);
            SceneFader.LoadSceneWithFade(returnSceneName);
        }

        // 페이드 중에는 검은 화면에 가려지므로 전환이 끝난 뒤부터 표시 시간을 센다.
        while (SceneFader.Instance.IsTransitioning)
        {
            yield return null;
        }

        yield return new WaitForSecondsRealtime(DisplaySeconds);

        float elapsed = 0f;
        while (elapsed < FadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / FadeSeconds);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        routine = null;
    }

    private void EnsureUIComponents()
    {
        if (canvasGroup != null)
        {
            return;
        }

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32766; // SceneFader(32767) 바로 아래

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(transform, false);
        Image panel = panelObject.AddComponent<Image>();
        panel.color = new Color(0f, 0f, 0f, 0.75f);
        panel.raycastTarget = false;
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -60f);
        panelRect.sizeDelta = new Vector2(900f, 110f);

        GameObject textObject = new GameObject("Message");
        textObject.transform.SetParent(panelObject.transform, false);
        // 기본 TMP 폰트의 폴백(NanumGothic)으로 한글도 표시된다.
        messageText = textObject.AddComponent<TextMeshProUGUI>();
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.fontSize = 36f;
        messageText.color = Color.white;
        messageText.raycastTarget = false;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 12f);
        textRect.offsetMax = new Vector2(-24f, -12f);
    }
}
