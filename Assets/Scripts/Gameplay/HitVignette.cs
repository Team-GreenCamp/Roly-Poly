using UnityEngine;
using UnityEngine.UI;

// 피격 시 화면 테두리를 살짝 붉히는 로컬 연출 (순수 클라이언트, 네트워킹 없음).
//
// 씬/프리팹 배선이 필요 없도록 첫 호출 시 오버레이 Canvas와 절차적 비네트 텍스처를 런타임 생성한다.
// 사용: 피해자 '소유자' 머신에서 HitVignette.Show() 호출 (PlayerController.Combat의 피격 릴레이 지점).
// 파티 게임 톤에 맞게 알파는 낮게, 페이드는 짧게. 연타 시에도 maxAlpha 이상 진해지지 않는다.
public class HitVignette : MonoBehaviour
{
    private const float PeakAlpha = 0.45f;   // 1회 피격 시 도달 알파
    private const float MaxAlpha = 0.6f;     // 연타 누적 상한
    private const float FadeDuration = 0.35f; // 페이드아웃 시간(초)
    private const int TextureSize = 128;

    private static HitVignette instance;

    private Image vignetteImage;
    private float currentAlpha;

    // 피격 피드백 표시. intensity로 세기를 조절할 수 있다(기본 1).
    public static void Show(float intensity = 1f)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("Hit Vignette");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<HitVignette>();
        }

        instance.Flash(Mathf.Clamp01(intensity));
    }

    private void Awake()
    {
        BuildOverlay();
    }

    private void Flash(float intensity)
    {
        currentAlpha = Mathf.Min(MaxAlpha, Mathf.Max(currentAlpha, PeakAlpha * intensity));
        ApplyAlpha();
    }

    private void Update()
    {
        if (currentAlpha <= 0f)
        {
            return;
        }

        currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, (PeakAlpha / FadeDuration) * Time.unscaledDeltaTime);
        ApplyAlpha();
    }

    private void ApplyAlpha()
    {
        if (vignetteImage != null)
        {
            vignetteImage.color = new Color(0.75f, 0.05f, 0.05f, currentAlpha);
        }
    }

    private void BuildOverlay()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // HUD 위, 시스템 팝업 아래쯤

        GameObject imageObject = new GameObject("Vignette Image");
        imageObject.transform.SetParent(transform, false);

        vignetteImage = imageObject.AddComponent<Image>();
        vignetteImage.sprite = CreateVignetteSprite();
        vignetteImage.raycastTarget = false; // 클릭을 가로채면 안 됨
        vignetteImage.color = new Color(0.75f, 0.05f, 0.05f, 0f);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 중앙은 투명하고 가장자리로 갈수록 붉어지는 방사형 비네트 텍스처를 절차 생성한다.
    private static Sprite CreateVignetteSprite()
    {
        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        float half = (TextureSize - 1) * 0.5f;
        Color32[] pixels = new Color32[TextureSize * TextureSize];

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                // 중심 기준 정규화 거리(모서리 ≈ 1.0)
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421356f;

                // 중앙 45%는 완전 투명, 가장자리로 갈수록 부드럽게 진해짐.
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, distance));
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
    }
}
