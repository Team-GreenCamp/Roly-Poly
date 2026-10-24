using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 서바이벌 매치 HUD (아레나 씬 Canvas에 부착, 순수 로컬).
// 상태 표시는 SurvivalGameManager의 NetworkVariable을 매 프레임 폴링한다
// (NetworkVariable 초기값은 OnValueChanged가 불리지 않을 수 있어 폴링이 안전).
// 이벤트(OnLocalEliminated 등)는 패널 전환 연출 트리거로만 사용한다.
[DisallowMultipleComponent]
public class SurvivalHudController : MonoBehaviour
{
    [Header("생존자 수 (우상단)")]
    [SerializeField] private GameObject alivePanel;
    [Tooltip("한 줄 합침형(\"생존 N/M\"). 아래 분리형 텍스트를 쓰는 씬에서는 비워도 됩니다.")]
    [SerializeField] private TMP_Text aliveCountText;
    [Tooltip("분리형: 생존 인원 숫자만 표시(선택). Alive/Total 텍스트가 나뉜 패널용.")]
    [SerializeField] private TMP_Text aliveNumberText;
    [Tooltip("분리형: 전체 인원 숫자만 표시(선택).")]
    [SerializeField] private TMP_Text totalNumberText;

    [Header("시작 카운트다운 (중앙)")]
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private TMP_Text countdownText;
    [Tooltip("GO! 표시를 유지하는 시간(초).")]
    [SerializeField] private float goDisplaySeconds = 1f;

    [Header("탈락 배너 + 관전 힌트")]
    [SerializeField] private GameObject eliminatedPanel;
    [SerializeField] private TMP_Text spectateHintText;

    [Header("관전 바 (하단 중앙, 선택)")]
    [Tooltip("연결하면 관전 중에는 안내 문구 대신 이 바를 표시한다.")]
    [SerializeField] private GameObject spectatePanel;
    [SerializeField] private TMP_Text spectateLabelText;
    [SerializeField] private TMP_Text spectateNameText;
    [SerializeField] private TMP_Text spectateAliveText;
    [SerializeField] private Button spectatePrevButton;
    [SerializeField] private Button spectateNextButton;

    [Header("승자 패널")]
    [SerializeField] private GameObject winnerPanel;
    [SerializeField] private TMP_Text winnerText;
    [Tooltip("로컬 플레이어 승리 시 winner 패널과 함께 표시할 연출 오브젝트.")]
    [SerializeField] private GameObject actionTextWin;
    [Tooltip("로컬 플레이어 패배 시 winner 패널과 함께 표시할 연출 오브젝트.")]
    [SerializeField] private GameObject actionTextLose;
    [Tooltip("상위권(시상대 순위 안, 꼴찌 제외)일 때 패배 이미지 대신 보여줄 스프라이트(예: GREAT). 비우면 패배 이미지.")]
    [SerializeField] private Sprite topRankSprite;
    [Tooltip("승자 없이 끝났을 때(전원 탈락·완주자 없음) 보여줄 스프라이트(예: GAME OVER). 비우면 패배 이미지.")]
    [SerializeField] private Sprite noWinnerSprite;
    [Tooltip("이 등수까지를 상위권으로 본다.")]
    [SerializeField] private int topRankCount = 3;
    [Tooltip("서버의 최종 순위를 기다리는 최대 시간(초). 넘으면 승패만으로 먼저 표시한다.")]
    [SerializeField] private float rankingWaitSeconds = 0.5f;
    [Tooltip("결과 이미지 등장 연출 시간(초).")]
    [SerializeField] private float resultPopSeconds = 0.5f;

    [Header("서든데스 경고 (선택)")]
    [SerializeField] private GameObject suddenDeathPanel; // 배경 포함 루트(비우면 텍스트 자체를 토글)
    [SerializeField] private TMP_Text suddenDeathText;
    [Tooltip("서든데스(붕괴 시작) 몇 초 전부터 경고 타이머를 표시할지.")]
    [SerializeField] private float suddenDeathWarnSeconds = 15f;
    [Tooltip("안내 띠의 강조 막대(선택). 붕괴 경고 중에는 경고색으로 바뀐다.")]
    [SerializeField] private Image suddenDeathAccent;

    [Header("제한시간 (상단 중앙, 선택)")]
    [SerializeField] private GameObject matchTimerPanel;
    [SerializeField] private TMP_Text matchTimerText;
    [SerializeField] private TMP_Text matchTimerLabel;
    [Tooltip("남은 시간 진행 바(Image Type=Filled, 선택).")]
    [SerializeField] private Image matchTimerFill;
    [Tooltip("상단 강조선 등 경고 시 색이 바뀌는 장식(선택).")]
    [SerializeField] private Image matchTimerAccent;
    [SerializeField] private float matchTimerTotalSeconds = 120f;
    [SerializeField] private Color hudAccentColor = new Color(1f, 0.686f, 0f, 1f);   // Heat UI Accent
    [SerializeField] private Color hudWarningColor = new Color(1f, 0.294f, 0.294f, 1f); // Heat UI Negative

    [Header("코인 점수 (우상단 고정 UI)")]
    [SerializeField] private GameObject coinPanel;
    [SerializeField] private TMP_Text coinText;

    [Header("킬 피드 (선택)")]
    [SerializeField] private GameObject killFeedPanel; // 배경 포함 루트(비우면 텍스트 자체를 토글)
    [SerializeField] private TMP_Text killFeedText;
    [Tooltip("킬 피드 한 줄이 표시되는 시간(초).")]
    [SerializeField] private float killFeedDuration = 4f;

    private GameObject SuddenDeathRoot =>
        suddenDeathPanel != null ? suddenDeathPanel : (suddenDeathText != null ? suddenDeathText.gameObject : null);
    private GameObject KillFeedRoot =>
        killFeedPanel != null ? killFeedPanel : (killFeedText != null ? killFeedText.gameObject : null);

    private SurvivalGameManager gameManager;
    private SpectatorController spectator;
    private string spectateTargetName = string.Empty;
    private float hideGoAtTime = -1f;
    private float killFeedHideAt = -1f;
    private int lastCountdownNumber = -1;   // 카운트다운 틱 사운드용
    private bool suddenDeathAnnounced;      // 서든데스 보이스 1회 재생용
    private float countdownBaseFontSize;    // 카운트다운 숫자용 원래 크기(대기 문구는 축소 표시)

    private enum ResultKind { None, Win, TopRank, Lose, NoWinner }
    private ResultKind shownResult;
    private float resultShownAt;
    private float finishedSeenAt = -1f;
    // 패배 오브젝트의 이미지를 상위권/승자 없음 스프라이트로 바꿔 쓰므로 원본을 보관한다.
    private Image loseImage;
    private Sprite loseSprite;
    private Vector2 winBasePosition;
    private Vector2 loseBasePosition;

    private void Start()
    {
        if (actionTextLose != null && actionTextLose.TryGetComponent(out loseImage))
        {
            loseSprite = loseImage.sprite;
        }
        if (actionTextWin != null && actionTextWin.transform is RectTransform winRect)
        {
            winBasePosition = winRect.anchoredPosition;
        }
        if (actionTextLose != null && actionTextLose.transform is RectTransform loseRect)
        {
            loseBasePosition = loseRect.anchoredPosition;
        }

        if (countdownText != null)
        {
            countdownBaseFontSize = countdownText.fontSize;
        }

        SetActiveSafe(alivePanel, false);
        SetActiveSafe(coinPanel, false);
        SetActiveSafe(matchTimerPanel, false);
        if (matchTimerLabel != null) GameLocalization.Set(matchTimerLabel, "HudTimeLeft");
        SetActiveSafe(countdownPanel, false);
        SetActiveSafe(eliminatedPanel, false);
        SetActiveSafe(spectatePanel, false);
        SetActiveSafe(winnerPanel, false);
        SetActiveSafe(actionTextWin, false);
        SetActiveSafe(actionTextLose, false);
        if (SuddenDeathRoot != null) SetActiveSafe(SuddenDeathRoot, false);
        if (KillFeedRoot != null) SetActiveSafe(KillFeedRoot, false);
    }

    private void OnDisable()
    {
        UnhookEvents();
    }

    private void Update()
    {
        if (!TryResolveGameManager())
        {
            return;
        }

        SurvivalGameManager.MatchState state = gameManager.State;

        UpdateAlivePanel(state);
        UpdateCountdownPanel(state);
        UpdateEliminatedPanel(state);
        UpdateWinnerPanel(state);
        UpdateMatchTimer(state);
        UpdateSuddenDeathText(state);
        UpdateKillFeed();
    }

    // 규칙 안내와 분리한 고정 타이머를 분:초로 표시합니다.
    private void UpdateMatchTimer(SurvivalGameManager.MatchState state)
    {
        bool show = state == SurvivalGameManager.MatchState.Playing && gameManager.IsTimedCoinSurvival;
        SetActiveSafe(matchTimerPanel, show);
        if (!show || matchTimerText == null) return;
        float remaining = Mathf.Max(0f, (float)gameManager.SurvivalRemaining);
        int seconds = Mathf.CeilToInt(remaining);
        bool warning = seconds <= 30;
        matchTimerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        matchTimerText.color = warning ? hudWarningColor : Color.white;

        // 마지막 10초는 숫자를 초마다 맥박처럼 키워 긴박감을 준다.
        float pulse = seconds <= 10 ? 1f + 0.12f * Mathf.Pow(1f - (remaining - Mathf.Floor(remaining)), 3f) : 1f;
        matchTimerText.rectTransform.localScale = new Vector3(pulse, pulse, 1f);

        if (matchTimerFill != null)
        {
            matchTimerFill.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(1f, matchTimerTotalSeconds));
            matchTimerFill.color = warning ? hudWarningColor : hudAccentColor;
        }

        if (matchTimerAccent != null)
        {
            matchTimerAccent.color = warning ? hudWarningColor : hudAccentColor;
        }
    }

    private void SetSuddenDeathAccent(bool warning)
    {
        if (suddenDeathAccent != null)
        {
            suddenDeathAccent.color = warning ? hudWarningColor : hudAccentColor;
        }
    }

    private void UpdateSuddenDeathText(SurvivalGameManager.MatchState state)
    {
        if (suddenDeathText == null)
        {
            return;
        }

        // 종료 후에는 같은 안내 띠에 로비 복귀 카운트다운을 결과 패널·시상식 내내 띄운다.
        if (state == SurvivalGameManager.MatchState.Finished && gameManager.LobbyReturnRemaining >= 0f)
        {
            GameLocalization.Set(suddenDeathText, "HudReturnLobby", Mathf.CeilToInt(gameManager.LobbyReturnRemaining));
            SetSuddenDeathAccent(false);
            SetActiveSafe(SuddenDeathRoot, true);
            return;
        }

        if (state != SurvivalGameManager.MatchState.Playing)
        {
            SetActiveSafe(SuddenDeathRoot, false);
            return;
        }

        // 레이스에서는 기존 경고 영역을 남은 시간과 완주 상태 표시에 사용합니다.
        if (gameManager.IsRace)
        {
            GameLocalization.Set(suddenDeathText, gameManager.LocalRaceFinished ? "HudRaceFinished" : "HudRaceTimer",
                Mathf.CeilToInt((float)gameManager.RaceRemaining), gameManager.RaceFinishCount, gameManager.TotalPlayerCount);
            SetSuddenDeathAccent(false);
            SetActiveSafe(SuddenDeathRoot, true);
            return;
        }

        if (gameManager.IsTimedCoinSurvival)
        {
            // 전용 타이머가 연결된 씬에서는 안내 영역에 규칙만 표시합니다.
            bool separateTimer = matchTimerPanel != null && matchTimerText != null;
            string key = gameManager.SurvivalRemaining <= 30
                ? (separateTimer ? "HudSurvivalBonusRule" : "HudSurvivalBonus")
                : (separateTimer ? "HudSurvivalRule" : "HudSurvivalTimer");
            GameLocalization.Set(suddenDeathText, key, Mathf.CeilToInt((float)gameManager.SurvivalRemaining));
            SetSuddenDeathAccent(false);
            SetActiveSafe(SuddenDeathRoot, true);
            return;
        }

        if (gameManager.IsSuddenDeath)
        {
            if (!suddenDeathAnnounced)
            {
                suddenDeathAnnounced = true;
                GameFeedback.SuddenDeath();
            }

            GameLocalization.Set(suddenDeathText, "HudCollapse");
            SetSuddenDeathAccent(true);
            SetActiveSafe(SuddenDeathRoot, true);
            return;
        }

        double remaining = gameManager.SuddenDeathRemaining;
        if (remaining <= suddenDeathWarnSeconds)
        {
            GameLocalization.Set(suddenDeathText, "HudCollapseTimer", Mathf.CeilToInt((float)remaining));
            SetSuddenDeathAccent(true);
            SetActiveSafe(SuddenDeathRoot, true);
        }
        else
        {
            SetActiveSafe(SuddenDeathRoot, false);
        }
    }

    private void UpdateKillFeed()
    {
        if (killFeedText != null && killFeedHideAt >= 0f && Time.time >= killFeedHideAt)
        {
            SetActiveSafe(KillFeedRoot, false);
            killFeedHideAt = -1f;
        }
    }

    private void HandlePlayerEliminated(ulong victimClientId, ulong killerClientId)
    {
        // 레이스에서는 왼쪽 상단을 순위표가 쓰므로 킬 피드를 띄우지 않는다.
        if (killFeedText == null || (gameManager != null && gameManager.IsRace))
        {
            return;
        }

        if (killerClientId != ulong.MaxValue)
            GameLocalization.Set(killFeedText, "HudKill", killerClientId + 1, victimClientId + 1);
        else
            GameLocalization.Set(killFeedText, "HudFall", victimClientId + 1);

        SetActiveSafe(KillFeedRoot, true);
        killFeedHideAt = Time.time + Mathf.Max(1f, killFeedDuration);
    }

    private bool TryResolveGameManager()
    {
        if (gameManager != null)
        {
            return true;
        }

        gameManager = SurvivalGameManager.Instance;
        if (gameManager == null)
        {
            return false;
        }

        gameManager.OnPlayerEliminated += HandlePlayerEliminated;

        spectator = gameManager.GetComponent<SpectatorController>();
        if (spectator != null)
        {
            spectator.OnSpectateTargetChanged += HandleSpectateTargetChanged;
            if (spectatePrevButton != null) spectatePrevButton.onClick.AddListener(spectator.Previous);
            if (spectateNextButton != null) spectateNextButton.onClick.AddListener(spectator.Next);
        }

        return true;
    }

    private void UnhookEvents()
    {
        if (gameManager != null)
        {
            gameManager.OnPlayerEliminated -= HandlePlayerEliminated;
        }

        if (spectator != null)
        {
            spectator.OnSpectateTargetChanged -= HandleSpectateTargetChanged;
            if (spectatePrevButton != null) spectatePrevButton.onClick.RemoveListener(spectator.Previous);
            if (spectateNextButton != null) spectateNextButton.onClick.RemoveListener(spectator.Next);
            spectator = null;
        }
    }

    private void HandleSpectateTargetChanged(string targetName)
    {
        spectateTargetName = targetName;
    }

    private void UpdateAlivePanel(SurvivalGameManager.MatchState state)
    {
        bool show = state == SurvivalGameManager.MatchState.Countdown
            || state == SurvivalGameManager.MatchState.Playing;

        SetActiveSafe(alivePanel, show && !gameManager.IsRace);

        if (show)
        {
            int alive = gameManager.AliveCount;
            int total = gameManager.TotalPlayerCount;

            // 연결된 텍스트만 갱신 — 합침형(구형 씬)과 분리형(Alive/Total 나뉜 패널) 모두 지원.
            if (aliveCountText != null && aliveCountText != aliveNumberText)
            {
                GameLocalization.Set(aliveCountText, "HudAlive", alive, total);
            }
            if (aliveNumberText != null)
            {
                aliveNumberText.text = alive.ToString();
            }
            if (totalNumberText != null)
            {
                totalNumberText.text = total.ToString();
            }
        }

        UpdateCoinText(show);
    }

    // 코인 점수(우상단, 생존자 수 아래). 코인 시스템이 없는 씬(스모 등)에서는 아무것도 표시하지 않는다.
    private void UpdateCoinText(bool alivePanelShown)
    {
        SurvivalCoinManager coinManager = SurvivalCoinManager.Instance;
        bool show = alivePanelShown && coinManager != null;

        // 프리팹에 마련된 코인 칸만 표시하며 런타임에는 UI를 생성하지 않는다.
        SetActiveSafe(coinPanel, show);
        if (show && coinText != null)
        {
            coinText.text = $"{coinManager.LocalScore}";
        }
    }

    private void UpdateCountdownPanel(SurvivalGameManager.MatchState state)
    {
        // 데디케이티드 서버 대기실: 인원이 모일 때까지 중앙에 대기 안내를 띄운다.
        if (state == SurvivalGameManager.MatchState.Waiting && gameManager.WaitingRequired > 0)
        {
            SetActiveSafe(countdownPanel, true);
            if (countdownText != null)
            {
                countdownText.fontSize = countdownBaseFontSize > 0f ? countdownBaseFontSize * 0.4f : 48f;
                countdownText.text = GameLocalization.Get("HudWaiting", gameManager.WaitingConnected, gameManager.WaitingRequired);
            }

            hideGoAtTime = -1f;
            return;
        }

        // 대기 표시가 끝났으면 카운트다운 숫자용 원래 크기로 복원.
        if (countdownText != null && countdownBaseFontSize > 0f && countdownText.fontSize != countdownBaseFontSize)
        {
            countdownText.fontSize = countdownBaseFontSize;
        }

        if (state == SurvivalGameManager.MatchState.Countdown)
        {
            SetActiveSafe(countdownPanel, true);

            int remaining = Mathf.Max(1, Mathf.CeilToInt((float)gameManager.CountdownRemaining));
            if (countdownText != null)
            {
                countdownText.text = remaining.ToString();
            }

            // 숫자가 바뀔 때마다 틱 사운드.
            if (remaining != lastCountdownNumber)
            {
                lastCountdownNumber = remaining;
                GameFeedback.CountdownTick();
            }

            hideGoAtTime = -1f;
            return;
        }

        if (state == SurvivalGameManager.MatchState.Playing)
        {
            // Playing 진입 직후 잠깐 "GO!"를 보여주고 숨긴다.
            if (hideGoAtTime < 0f)
            {
                hideGoAtTime = Time.time + Mathf.Max(0.2f, goDisplaySeconds);
                if (countdownText != null)
                {
                    countdownText.text = GameLocalization.Get("HudGo");
                }
                GameFeedback.MatchStart();
            }

            if (countdownText != null && Time.time < hideGoAtTime)
                countdownText.text = GameLocalization.Get("HudGo");
            SetActiveSafe(countdownPanel, Time.time < hideGoAtTime);
            return;
        }

        SetActiveSafe(countdownPanel, false);
    }

    private void UpdateEliminatedPanel(SurvivalGameManager.MatchState state)
    {
        bool show = gameManager.LocalEliminated && state != SurvivalGameManager.MatchState.Finished;

        // 관전 바가 연결돼 있고 볼 대상이 있으면 안내 문구 대신 바를 띄운다.
        bool showBar = show && spectatePanel != null && spectator != null && spectator.IsSpectating
            && !string.IsNullOrEmpty(spectateTargetName);
        SetActiveSafe(spectatePanel, showBar);
        SetActiveSafe(eliminatedPanel, show && !showBar);
        if (showBar)
        {
            if (spectateLabelText != null) GameLocalization.Set(spectateLabelText, "HudSpectatingLabel");
            if (spectateNameText != null) spectateNameText.text = spectateTargetName;
            if (spectateAliveText != null) GameLocalization.Set(spectateAliveText, "HudSpectateAlive", gameManager.AliveCount);
            return;
        }

        if (show && spectateHintText != null)
        {
            spectateHintText.text = string.IsNullOrEmpty(spectateTargetName)
                ? GameLocalization.Get("HudEliminated")
                : GameLocalization.Get("HudSpectating", spectateTargetName);
        }
    }

    private void UpdateWinnerPanel(SurvivalGameManager.MatchState state)
    {
        bool finished = state == SurvivalGameManager.MatchState.Finished;
        if (!finished) finishedSeenAt = -1f;
        else if (finishedSeenAt < 0f) finishedSeenAt = Time.unscaledTime;

        // 종료 직후엔 winner 패널을 보여주고, 시상식이 시작되면(PodiumActive) 끈다.
        // 최종 순위가 도착할 때까지(최대 rankingWaitSeconds) 기다려 결과 이미지가 중간에 바뀌지 않게 한다.
        bool ready = finished && (gameManager.HasFinalRanking || Time.unscaledTime - finishedSeenAt >= rankingWaitSeconds);
        bool show = ready && !gameManager.PodiumActive;

        SetActiveSafe(winnerPanel, show);
        if (!show)
        {
            HideResult();
            return;
        }

        ulong winner = gameManager.WinnerClientId;
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        bool hasWinner = winner != ulong.MaxValue;
        int rank = gameManager.GetFinalRank(localId);
        int playerCount = gameManager.FinalPlayerCount;

        ResultKind kind;
        if (!hasWinner) kind = ResultKind.NoWinner;
        else if (winner == localId) kind = ResultKind.Win;
        else if (rank > 0 && rank <= topRankCount && rank < playerCount) kind = ResultKind.TopRank; // 꼴찌는 상위권에서 제외
        else kind = ResultKind.Lose;

        if (kind != shownResult)
        {
            ShowResult(kind);
        }
        AnimateResult();

        if (winnerText == null)
        {
            return;
        }

        // 승리·승자 없음은 결과 이미지(WIN!/GAME OVER)가 이미 말해 주므로 문구는 승자가 따로 있을 때만 쓴다.
        int wins = hasWinner ? SurvivalWinTracker.GetWins(winner) : 0;
        string winsSuffix = wins > 1 ? GameLocalization.Get("HudWins", wins) : string.Empty;
        string headline = kind == ResultKind.TopRank || kind == ResultKind.Lose
            ? GameLocalization.Get("HudPlayerWins", gameManager.ResolvePlayerName(winner)) + winsSuffix
            : winsSuffix.Trim();
        // 로비 복귀 카운트다운은 시상식에도 남도록 상단 안내 띠(UpdateSuddenDeathText)에서 표시한다.
        string rankLine = rank > 0 ? $"<size=80%>{GameLocalization.Get("HudFinalRank", rank, playerCount)}</size>" : string.Empty;
        winnerText.text = headline.Length > 0 && rankLine.Length > 0 ? $"{headline}\n{rankLine}" : headline + rankLine;
    }

    private void ShowResult(ResultKind kind)
    {
        shownResult = kind;
        resultShownAt = Time.unscaledTime;

        SetActiveSafe(actionTextWin, kind == ResultKind.Win);
        SetActiveSafe(actionTextLose, kind != ResultKind.Win);

        // 패배 오브젝트 하나로 상위권/패배/승자 없음 이미지를 바꿔 보여준다.
        if (loseImage != null && kind != ResultKind.Win)
        {
            Sprite sprite = kind == ResultKind.TopRank ? topRankSprite
                : kind == ResultKind.NoWinner ? noWinnerSprite
                : null;
            loseImage.sprite = sprite != null ? sprite : loseSprite;
            loseImage.SetNativeSize();
        }
    }

    private void HideResult()
    {
        if (shownResult == ResultKind.None)
        {
            if (winnerPanel == null && winnerText != null && winnerText.text.Length > 0)
            {
                winnerText.text = string.Empty; // 패널 없이 텍스트만 있는 씬에서 시상식 중 문구가 남지 않게
            }
            return;
        }

        shownResult = ResultKind.None;
        SetActiveSafe(actionTextWin, false);
        SetActiveSafe(actionTextLose, false);
        if (winnerText != null)
        {
            winnerText.alpha = 1f;
            if (winnerPanel == null) winnerText.text = string.Empty;
        }
    }

    // 승리·상위권은 커지며 튀어나오고(EaseOutBack), 패배·승자 없음은 위에서 떨어져 튕긴다(EaseOutBounce).
    // 문구는 이미지가 자리 잡을 즈음 서서히 나타난다.
    private void AnimateResult()
    {
        float elapsed = Time.unscaledTime - resultShownAt;
        float duration = Mathf.Max(0.01f, resultPopSeconds);
        float t = Mathf.Clamp01(elapsed / duration);

        bool win = shownResult == ResultKind.Win;
        GameObject target = win ? actionTextWin : actionTextLose;
        if (target != null && target.transform is RectTransform rect)
        {
            Vector2 basePosition = win ? winBasePosition : loseBasePosition;
            if (shownResult == ResultKind.Win || shownResult == ResultKind.TopRank)
            {
                float scale = EaseOutBack(t);
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.localRotation = Quaternion.Euler(0f, 0f, -12f * (1f - t));
                rect.anchoredPosition = basePosition;
            }
            else
            {
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.Euler(0f, 0f, -6f * EaseOutBounce(t));
                rect.anchoredPosition = basePosition + Vector2.up * (160f * (1f - EaseOutBounce(t)));
            }
        }

        if (winnerText != null)
        {
            winnerText.alpha = Mathf.Clamp01((elapsed - duration * 0.5f) / duration);
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private static float EaseOutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }

    private static void SetActiveSafe(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
