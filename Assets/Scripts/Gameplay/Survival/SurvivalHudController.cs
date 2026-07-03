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

    private SurvivalGameManager gameManager;
    private SpectatorController spectator;
    private string spectateTargetName = string.Empty;
    private float hideGoAtTime = -1f;

    private void Start()
    {
        SetActiveSafe(alivePanel, false);
        SetActiveSafe(countdownPanel, false);
        SetActiveSafe(eliminatedPanel, false);
        SetActiveSafe(winnerPanel, false);
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

        spectator = gameManager.GetComponent<SpectatorController>();
        if (spectator != null)
        {
            spectator.OnSpectateTargetChanged += HandleSpectateTargetChanged;
        }

        return true;
    }

    private void UnhookEvents()
    {
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

            if (countdownText != null)
            {
                int remaining = Mathf.Max(1, Mathf.CeilToInt((float)gameManager.CountdownRemaining));
                countdownText.text = remaining.ToString();
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
        bool show = state == SurvivalGameManager.MatchState.Finished;
        SetActiveSafe(winnerPanel, show);

        if (!show || winnerText == null)
        {
            return;
        }

        ulong winner = gameManager.WinnerClientId;
        bool localWon = NetworkManager.Singleton != null && winner == NetworkManager.Singleton.LocalClientId;
        string winnerName = localWon ? "YOU WIN!" : $"Player {winner + 1} WINS!";
        winnerText.text = $"{winnerName}\n잠시 후 로비로 돌아갑니다...";
    }

    private static void SetActiveSafe(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
