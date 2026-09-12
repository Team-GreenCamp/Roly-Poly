using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class SurvivalGameManager
{
    [SerializeField] private bool timedCoinSurvival;
    private readonly NetworkVariable<double> survivalDeadline = new NetworkVariable<double>(0);
    private readonly List<ulong> coinFinishOrder = new List<ulong>();
    public bool IsTimedCoinSurvival => timedCoinSurvival;
    public double SurvivalRemaining => NetworkManager == null || survivalDeadline.Value <= 0 ? 120 :
        System.Math.Max(0, survivalDeadline.Value - NetworkManager.ServerTime.Time);

    // 생존 승리는 기존 탈락 판정이 처리하고, 2분이 지나면 생존자의 코인 점수로 순위를 확정합니다.
    private void UpdateTimedSurvival()
    {
        if (!timedCoinSurvival || !IsSpawned || !IsServer || State != MatchState.Playing
            || survivalDeadline.Value <= 0 || SurvivalRemaining > 0) return;
        nextKillSweepTime = 0;
        UpdateServerKillSweep();
        if (State != MatchState.Playing) return;
        coinFinishOrder.Clear();
        coinFinishOrder.AddRange(aliveClients);
        var coins = SurvivalCoinManager.Instance;
        coinFinishOrder.Sort((a, b) =>
        {
            int score = (coins != null ? coins.GetScore(b) : 0).CompareTo(coins != null ? coins.GetScore(a) : 0);
            if (score != 0) return score;
            int reached = (coins != null ? coins.GetScoreReachedAt(a) : 0).CompareTo(coins != null ? coins.GetScoreReachedAt(b) : 0);
            return reached != 0 ? reached : a.CompareTo(b);
        });
        ulong winner = coinFinishOrder.Count > 0 ? coinFinishOrder[0] : ulong.MaxValue;
        // 생존자가 적으면 뒤쪽 시상대는 기존 탈락 순서로 채웁니다.
        for (int i = eliminationOrder.Count - 1; i >= 0; i--)
            if (!coinFinishOrder.Contains(eliminationOrder[i])) coinFinishOrder.Add(eliminationOrder[i]);
        FinishMatchOnServer(winner);
    }
}
