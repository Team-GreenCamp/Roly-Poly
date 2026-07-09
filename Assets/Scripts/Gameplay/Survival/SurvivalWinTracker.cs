using System.Collections.Generic;

// 세션 동안 유지되는 서바이벌 승수 기록(로컬 static — 씬 전환을 넘어 유지).
// 서버가 매치 종료 시 AddWin 후 ClientRpc로 전파하고, 각 클라이언트는 SetWins로 맞춘다.
// 새 세션 시작 시(NetworkSessionManager) ResetAll로 초기화한다(클라이언트 ID 재사용 대비).
public static class SurvivalWinTracker
{
    private static readonly Dictionary<ulong, int> wins = new Dictionary<ulong, int>();

    public static int GetWins(ulong clientId)
    {
        return wins.TryGetValue(clientId, out int count) ? count : 0;
    }

    public static void SetWins(ulong clientId, int count)
    {
        wins[clientId] = count;
    }

    public static int AddWin(ulong clientId)
    {
        int count = GetWins(clientId) + 1;
        wins[clientId] = count;
        return count;
    }

    public static void ResetAll()
    {
        wins.Clear();
    }
}
