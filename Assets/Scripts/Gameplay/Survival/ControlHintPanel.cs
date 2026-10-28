using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 경기 중 화면 구석에 작게 띄우는 조작 안내 (HUD Canvas에 부착, 순수 로컬).
// 모드는 SurvivalGameManager의 플래그로 판별하므로 씬마다 따로 설정하지 않는다.
// 카운트다운과 경기 초반에는 또렷하게, 이후에는 흐리게 남겨 두고, 탈락·종료 시 숨긴다.
[DisallowMultipleComponent]
public class ControlHintPanel : MonoBehaviour
{
    [System.Serializable]
    private struct HintSlot
    {
        public GameObject root;
        public Image mouseIcon;    // 마우스 버튼은 그림으로
        public GameObject keycap;  // 키보드 키는 키캡 + 글자로
        public TMP_Text keyText;
        public TMP_Text actionText;
    }

    [SerializeField] private CanvasGroup group;
    [Tooltip("안내 줄. 모드의 안내 개수보다 많은 줄은 숨긴다.")]
    [SerializeField] private HintSlot[] hintSlots;
    [SerializeField] private Sprite mouseLeftSprite;
    [SerializeField] private Sprite mouseRightSprite;
    [Tooltip("경기 시작 후 이 시간(초) 동안은 또렷하게 보여준다.")]
    [SerializeField] private float brightSeconds = 15f;
    [Tooltip("또렷한 시간이 지난 뒤의 투명도.")]
    [SerializeField] private float dimAlpha = 0.45f;
    [SerializeField] private float fadeSeconds = 0.4f;

    private enum Mode { None, Survival, Race, Falling }
    private enum HintInput { MouseLeft, MouseRight, E, Space, F, Shift }

    // (입력, 동작 로컬라이즈 키)
    private static readonly (HintInput input, string action)[] SurvivalHints =
    {
        (HintInput.MouseRight, "HintGrab"), (HintInput.MouseLeft, "HintThrow"), (HintInput.E, "HintPickUp"),
        (HintInput.Space, "HintJumpHold"), (HintInput.F, "HintDash"), (HintInput.Shift, "HintSprint"),
    };
    private static readonly (HintInput input, string action)[] RaceHints =
    {
        (HintInput.Space, "HintJumpHold"), (HintInput.F, "HintDash"), (HintInput.Shift, "HintSprint"),
    };
    private static readonly (HintInput input, string action)[] FallingHints =
    {
        (HintInput.MouseRight, "HintGrab"), (HintInput.MouseLeft, "HintThrow"), (HintInput.E, "HintPickUp"),
        (HintInput.Space, "HintJumpHold"), (HintInput.F, "HintDash"),
    };

    private Mode shownMode;
    private float playingSeenAt = -1f;

    private void Start()
    {
        if (group != null)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }
    }

    private void Update()
    {
        SurvivalGameManager gameManager = SurvivalGameManager.Instance;
        float target = 0f;
        if (gameManager != null)
        {
            SurvivalGameManager.MatchState state = gameManager.State;
            bool playing = state == SurvivalGameManager.MatchState.Playing;
            if (!playing) playingSeenAt = -1f;
            else if (playingSeenAt < 0f) playingSeenAt = Time.unscaledTime;

            bool show = (state == SurvivalGameManager.MatchState.Countdown || playing) && !gameManager.LocalEliminated;
            if (show)
            {
                ApplyMode(ResolveMode(gameManager));
                bool bright = !playing || Time.unscaledTime - playingSeenAt < brightSeconds;
                target = bright ? 1f : dimAlpha;
            }
        }

        if (group != null && !Mathf.Approximately(group.alpha, target))
        {
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        }
    }

    private static Mode ResolveMode(SurvivalGameManager gameManager)
    {
        if (gameManager.IsRace) return Mode.Race;
        if (gameManager.IsTimedCoinSurvival) return Mode.Survival;
        return Mode.Falling;
    }

    private void ApplyMode(Mode mode)
    {
        if (mode == shownMode) return;
        shownMode = mode;

        (HintInput input, string action)[] hints = mode switch
        {
            Mode.Race => RaceHints,
            Mode.Survival => SurvivalHints,
            _ => FallingHints,
        };

        for (int i = 0; i < hintSlots.Length; i++)
        {
            HintSlot slot = hintSlots[i];
            bool used = i < hints.Length;
            if (slot.root != null) slot.root.SetActive(used);
            if (!used) continue;

            HintInput input = hints[i].input;
            bool mouse = input == HintInput.MouseLeft || input == HintInput.MouseRight;
            if (slot.mouseIcon != null)
            {
                slot.mouseIcon.gameObject.SetActive(mouse);
                if (mouse) slot.mouseIcon.sprite = input == HintInput.MouseLeft ? mouseLeftSprite : mouseRightSprite;
            }
            if (slot.keycap != null) slot.keycap.SetActive(!mouse);
            if (!mouse && slot.keyText != null) slot.keyText.text = input.ToString();
            GameLocalization.Set(slot.actionText, hints[i].action);
        }
    }
}
