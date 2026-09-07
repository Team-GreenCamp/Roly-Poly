using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SceneFader : MonoBehaviour
{
    private static SceneFader instance;
    public static SceneFader Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<SceneFader>();
                if (instance == null)
                {
                    GameObject faderObject = new GameObject("[SceneFader]");
                    instance = faderObject.AddComponent<SceneFader>();
                    DontDestroyOnLoad(faderObject);
                }
            }
            return instance;
        }
    }

    [Header("Fade Settings")]
    [SerializeField] private float defaultFadeOutDuration = 0.4f;
    [SerializeField] private float defaultFadeInDuration = 0.4f;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private Image fadeImage;
    private Coroutine currentFadeRoutine;
    private bool isTransitioning;

    public bool IsTransitioning => isTransitioning;
    public float CurrentAlpha => canvasGroup != null ? canvasGroup.alpha : 0f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureUIComponents();
    }

    private void EnsureUIComponents()
    {
        if (canvasGroup != null)
        {
            return;
        }

        canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767; // 모든 UI 요소 최상위에 표시

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.ignoreParentGroups = true;

        Transform imageTransform = transform.Find("FadeOverlayImage");
        GameObject imageObj = imageTransform != null ? imageTransform.gameObject : null;
        if (imageObj == null)
        {
            imageObj = new GameObject("FadeOverlayImage");
            imageObj.transform.SetParent(transform, false);
        }

        fadeImage = imageObj.GetComponent<Image>();
        if (fadeImage == null)
        {
            fadeImage = imageObj.AddComponent<Image>();
        }
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;

        RectTransform rect = imageObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 페이드 아웃 -> 씬 로드 -> 페이드 인 순서로 안전하게 씬을 전환합니다.
    /// </summary>
    public static void LoadSceneWithFade(string sceneName, float fadeOutDuration = -1f, float fadeInDuration = -1f)
    {
        Instance.StartSceneTransition(sceneName, fadeOutDuration, fadeInDuration);
    }

    public void StartSceneTransition(string sceneName, float fadeOutDuration = -1f, float fadeInDuration = -1f)
    {
        if (isTransitioning)
        {
            return;
        }

        EnsureUIComponents();

        float outDur = fadeOutDuration >= 0f ? fadeOutDuration : defaultFadeOutDuration;
        float inDur = fadeInDuration >= 0f ? fadeInDuration : defaultFadeInDuration;

        if (currentFadeRoutine != null)
        {
            StopCoroutine(currentFadeRoutine);
        }
        currentFadeRoutine = StartCoroutine(TransitionRoutine(sceneName, outDur, inDur));
    }

    private IEnumerator TransitionRoutine(string sceneName, float fadeOutDuration, float fadeInDuration)
    {
        isTransitioning = true;
        canvasGroup.blocksRaycasts = true;

        // 1. Fade Out (0 -> 1)
        yield return FadeRoutine(canvasGroup.alpha, 1f, fadeOutDuration);
        canvasGroup.alpha = 1f;

        // 잠시 어두운 상태 유지
        yield return new WaitForSecondsRealtime(0.05f);

        // 2. 씬 비동기 로드
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // 새 씬 로드 후 안정화 대기
        yield return null;
        yield return new WaitForSecondsRealtime(0.05f);

        // 3. Fade In (1 -> 0)
        yield return FadeRoutine(1f, 0f, fadeInDuration);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;

        isTransitioning = false;
        currentFadeRoutine = null;
    }

    public IEnumerator FadeOut(float duration)
    {
        EnsureUIComponents();
        canvasGroup.blocksRaycasts = true;
        yield return FadeRoutine(canvasGroup.alpha, 1f, duration);
        canvasGroup.alpha = 1f;
    }

    public IEnumerator FadeIn(float duration)
    {
        EnsureUIComponents();
        yield return FadeRoutine(canvasGroup.alpha, 0f, duration);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
    }

    private IEnumerator FadeRoutine(float startAlpha, float targetAlpha, float duration)
    {
        float dur = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }
}
