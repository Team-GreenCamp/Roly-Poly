using TMPro;
using Unity.Netcode;
using UnityEngine;

// 서바이벌 매치 HUD (아레나 씬 Canvas에 부착, 순수 로컬).
// 상태 표시는 SurvivalGameManager의 NetworkVariable을 매 프레임 폴링한다
// (NetworkVariable 초기값은 OnValueChanged가 불리지 않을 수 있어 폴링이 안전).
// 이벤트(OnLocalEliminated 등)는 패널 전환 연출 트리거로만 사용한다.
[DisallowMultipleComponent]
public class SurvivalHudController : MonoBehaviour
{
    [Header("생존자 수 (우상단)")]
    [SerializeField] private GameObject alivePanel;
    [SerializeField] private TMP_Text aliveCountText;

    [Header("시작 카운트다운 (중앙)")]
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private TMP_Text countdownText;
    [Tooltip("GO! 표시를 유지하는 시간(초).")]
    [SerializeField] private float goDisplaySeconds = 1f;

    [Header("탈락 배너 + 관전 힌트")]
    [SerializeField] private GameObject eliminatedPanel;
    [SerializeField] private TMP_Text spectateHintText;

    [Header("승자 패널")]
    [SerializeField] private GameObject winnerPanel;
    [SerializeField] private TMP_Text winnerText;

    [Header("서든데스 경고 (선택)")]
    [SerializeField] private GameObject suddenDeathPanel; // 배경 포함 루트(비우면 텍스트 자체를 토글)
    [SerializeField] private TMP_Text suddenDeathText;
    [Tooltip("서든데스 몇 초 전부터 경고를 표시할지.")]
    [SerializeField] private float suddenDeathWarnSeconds = 10f;

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

    private void Start()
    {
        SetActiveSafe(alivePanel, false);
        SetActiveSafe(countdownPanel, false);
        SetActiveSafe(eliminatedPanel, false);
        SetActiveSafe(winnerPanel, false);
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
        UpdateSuddenDeathText(state);
        UpdateKillFeed();
    }

    private void UpdateSuddenDeathText(SurvivalGameManager.MatchState state)
    {
        if (suddenDeathText == null)
        {
            return;
        }

        if (state != SurvivalGameManager.MatchState.Playing)
        {
            SetActiveSafe(SuddenDeathRoot, false);
            return;
        }

        if (gameManager.IsSuddenDeath)
        {
            if (!suddenDeathAnnounced)
            {
                suddenDeathAnnounced = true;
                GameFeedback.SuddenDeath();
            }

            suddenDeathText.text = "⚠ SUDDEN DEATH! 발판이 무너집니다!";
            SetActiveSafe(SuddenDeathRoot, true);
            return;
        }

        double remaining = gameManager.SuddenDeathRemaining;
        if (remaining <= suddenDeathWarnSeconds)
        {
            suddenDeathText.text = $"서든데스까지 {Mathf.CeilToInt((float)remaining)}초";
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
        if (killFeedText == null)
        {
            return;
        }

        string victimName = $"Player {victimClientId + 1}";
        killFeedText.text = killerClientId != ulong.MaxValue
            ? $"Player {killerClientId + 1} ▶ {victimName} 떨어뜨림!"
            : $"{victimName} 추락...";

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

        SetActiveSafe(alivePanel, show);

        if (show && aliveCountText != null)
        {
            aliveCountText.text = $"생존 {gameManager.AliveCount}/{gameManager.TotalPlayerCount}";
        }
    }

    private void UpdateCountdownPanel(SurvivalGameManager.MatchState state)
    {
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
                    countdownText.text = "GO!";
                }
                GameFeedback.MatchStart();
            }

            SetActiveSafe(countdownPanel, Time.time < hideGoAtTime);
            return;
        }

        SetActiveSafe(countdownPanel, false);
    }

    private void UpdateEliminatedPanel(SurvivalGameManager.MatchState state)
    {
        bool show = gameManager.LocalEliminated && state != SurvivalGameManager.MatchState.Finished;
        SetActiveSafe(eliminatedPanel, show);

        if (show && spectateHintText != null)
        {
            spectateHintText.text = string.IsNullOrEmpty(spectateTargetName)
                ? "탈락! 잠시 후 관전으로 전환됩니다"
                : $"관전 중: {spectateTargetName}   (좌클릭: 다음 / Tab: 이전)";
        }
    }

    private void UpdateWinnerPanel(SurvivalGameManager.MatchState state)
    {
        // 종료 직후엔 winner 패널을 보여주고, 시상식이 시작되면(PodiumActive) 끈다.
        bool show = state == SurvivalGameManager.MatchState.Finished && !gameManager.PodiumActive;
        SetActiveSafe(winnerPanel, show);

        if (!show || winnerText == null)
        {
            return;
        }

        ulong winner = gameManager.WinnerClientId;
        bool localWon = NetworkManager.Singleton != null && winner == NetworkManager.Singleton.LocalClientId;
        string winnerName = localWon ? "YOU WIN!" : $"Player {winner + 1} WINS!";
        int wins = SurvivalWinTracker.GetWins(winner);
        string winsSuffix = wins > 1 ? $"  (통산 {wins}승)" : string.Empty;
        winnerText.text = $"{winnerName}{winsSuffix}\n잠시 후 로비로 돌아갑니다...";
    }

    private static void SetActiveSafe(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
