using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 레이스 실시간 순위표(왼쪽 상단). 서버가 동기화하는 RaceStandings 순서대로 그린다.
// 줄은 순위 칸이 아니라 플레이어에 묶여 있어서, 순위가 바뀌면 그 줄이 새 자리로 미끄러져 이동한다.
[DisallowMultipleComponent]
public class RaceLeaderboardHud : MonoBehaviour
{
    [Serializable]
    public class Row
    {
        public RectTransform root;
        public Image highlight;
        public Image badge;
        public TMP_Text rankText;
        public TMP_Text nameText;
        public TMP_Text statusText;
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Row[] rows = Array.Empty<Row>();
    [SerializeField] private float rowSpacing = 42f;
    [SerializeField] private float firstRowY = -40f;
    [SerializeField] private float slideSpeed = 12f;
    [SerializeField] private Color goldColor = new Color(1f, 0.78f, 0.2f);
    [SerializeField] private Color silverColor = new Color(0.8f, 0.83f, 0.9f);
    [SerializeField] private Color bronzeColor = new Color(0.86f, 0.52f, 0.26f);
    [SerializeField] private Color otherBadgeColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private Color localNameColor = new Color(1f, 0.686f, 0f);

    private readonly Dictionary<ulong, int> rowOfClient = new Dictionary<ulong, int>();
    private readonly HashSet<ulong> seen = new HashSet<ulong>();
    private readonly List<ulong> stale = new List<ulong>();
    private readonly Dictionary<ulong, NetworkOwnedObjectActivator> players = new Dictionary<ulong, NetworkOwnedObjectActivator>();
    private float nextPlayerRefresh;

    private void Start()
    {
        if (titleText != null) GameLocalization.Set(titleText, "HudRaceRanking");
        SetPanel(false);
        foreach (var row in rows)
        {
            if (row.root != null) row.root.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        var gm = SurvivalGameManager.Instance;
        bool show = gm != null && gm.IsSpawned && gm.IsRace && !gm.PodiumActive
            && (gm.State == SurvivalGameManager.MatchState.Countdown || gm.State == SurvivalGameManager.MatchState.Playing
                || gm.State == SurvivalGameManager.MatchState.Finished);
        SetPanel(show);
        if (!show) return;

        RefreshPlayers();
        var standings = gm.RaceStandings;
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

        seen.Clear();
        for (int rank = 0; rank < standings.Count; rank++)
        {
            var s = standings[rank];
            seen.Add(s.ClientId);
            int rowIndex = AcquireRow(s.ClientId);
            if (rowIndex < 0) continue;
            var row = rows[rowIndex];
            bool appearing = !row.root.gameObject.activeSelf;
            row.root.gameObject.SetActive(true);

            float targetY = firstRowY - rank * rowSpacing;
            Vector2 pos = row.root.anchoredPosition;
            pos.y = appearing ? targetY : Mathf.Lerp(pos.y, targetY, 1f - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime));
            row.root.anchoredPosition = pos;

            bool isLocal = s.ClientId == localId;
            if (row.rankText != null) row.rankText.text = (rank + 1).ToString();
            if (row.badge != null) row.badge.color = rank == 0 ? goldColor : rank == 1 ? silverColor : rank == 2 ? bronzeColor : otherBadgeColor;
            if (row.highlight != null) row.highlight.enabled = isLocal;
            if (row.nameText != null)
            {
                row.nameText.text = ResolveName(s.ClientId);
                row.nameText.color = isLocal ? localNameColor : Color.white;
            }
            if (row.statusText != null)
            {
                if (s.Finished) GameLocalization.Set(row.statusText, "HudRaceRowFinished");
                else GameLocalization.Set(row.statusText, "HudRaceRowProgress", s.Percent);
            }
        }

        // 순위표에서 빠진 플레이어(이탈)의 줄은 반납한다.
        stale.Clear();
        foreach (var kv in rowOfClient)
        {
            if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
        }
        foreach (ulong id in stale)
        {
            rows[rowOfClient[id]].root.gameObject.SetActive(false);
            rowOfClient.Remove(id);
        }
    }

    private int AcquireRow(ulong clientId)
    {
        if (rowOfClient.TryGetValue(clientId, out int index)) return index;
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].root == null || rowOfClient.ContainsValue(i)) continue;
            rowOfClient[clientId] = i;
            return i;
        }
        return -1;
    }

    private void RefreshPlayers()
    {
        if (Time.unscaledTime < nextPlayerRefresh) return;
        nextPlayerRefresh = Time.unscaledTime + 1f;
        players.Clear();
        foreach (var p in FindObjectsByType<NetworkOwnedObjectActivator>(FindObjectsSortMode.None))
            players[p.OwnerClientId] = p;
    }

    private string ResolveName(ulong clientId)
    {
        return players.TryGetValue(clientId, out var p) && p != null ? p.DisplayName : $"Player {clientId + 1}";
    }

    private void SetPanel(bool active)
    {
        if (panel != null && panel.activeSelf != active) panel.SetActive(active);
    }
}
