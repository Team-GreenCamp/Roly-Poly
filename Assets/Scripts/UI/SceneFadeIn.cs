using System.Collections;
using UnityEngine;

// 씬 로드 시 검은 화면에서 서서히 밝아지는 페이드인. 로비 등 아무 씬에나 붙일 수 있다.
// 검은 전체화면 오버레이(CanvasGroup)를 alpha=1에서 0으로 낮춘다.
[DisallowMultipleComponent]
public class SceneFadeIn : MonoBehaviour
{
    [SerializeField] private CanvasGroup overlay;
    [SerializeField] private float duration = 0.45f;
    [Tooltip("페이드 시작 전 검은 화면을 유지하는 시간(초).")]
    [SerializeField] private float holdBlackSeconds = 0.05f;

    private void Reset()
    {
        overlay = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (overlay == null)
        {
            overlay = GetComponent<CanvasGroup>();
        }
        if (overlay == null)
        {
            return;
        }

        overlay.gameObject.SetActive(true);
        overlay.alpha = 1f;
        overlay.blocksRaycasts = true;
        StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        if (holdBlackSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(holdBlackSeconds);
        }

        float dur = Mathf.Max(0.05f, duration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            overlay.alpha = Mathf.Lerp(1f, 0f, t / dur);
            yield return null;
        }

        overlay.alpha = 0f;
        overlay.blocksRaycasts = false;
        overlay.gameObject.SetActive(false);
    }
}
