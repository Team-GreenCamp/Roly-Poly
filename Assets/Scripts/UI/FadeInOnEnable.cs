using System.Collections;
using UnityEngine;

// 켜질 때마다 CanvasGroup을 투명에서 서서히 보이게 한다. 대기실 가장자리 그늘처럼 갑자기 나타나면 어색한 UI에 붙인다.
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class FadeInOnEnable : MonoBehaviour
{
    [SerializeField] private float duration = 0.8f;

    private CanvasGroup group;

    private void OnEnable()
    {
        if (group == null)
        {
            group = GetComponent<CanvasGroup>();
        }

        group.alpha = 0f;
        StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        float dur = Mathf.Max(0.05f, duration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.SmoothStep(0f, 1f, t / dur);
            yield return null;
        }

        group.alpha = 1f;
    }
}
