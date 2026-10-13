using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class SurvivalGameManager
{
    [Header("Obstacle Race")]
    [SerializeField] private bool raceMode;
    [SerializeField] private float raceTimeLimit = 180f;
    [SerializeField] private Vector3[] customRaceGates;
    private readonly List<ulong> raceFinishOrder = new();
    private readonly Dictionary<ulong, int> raceProgress = new();
    private readonly Dictionary<ulong, double> raceRespawnTimes = new();
    private readonly NetworkVariable<double> raceDeadline = new(0);
    private readonly NetworkVariable<int> raceFinishCount = new(0);
    private bool localRaceFinished;
    private Transform raceRespawnPoint;
    public bool IsRace => raceMode;
    public int RaceFinishCount => raceFinishCount.Value;
    public bool LocalRaceFinished => localRaceFinished;
    // 세션 종료와 로비 전환 사이에도 HUD가 남은 시간을 안전하게 읽을 수 있게 합니다.
    public double RaceRemaining => NetworkManager == null || raceDeadline.Value <= 0 ? raceTimeLimit :
        System.Math.Max(0, raceDeadline.Value - NetworkManager.ServerTime.Time);

    private Vector3[] ActiveGates => (customRaceGates != null && customRaceGates.Length > 0) ? customRaceGates : RaceGates;

    // U자 코스를 순서대로 통과해야 완주로 인정합니다. 서버가 동기화된 위치로 판정합니다.
    private static readonly Vector3[] RaceGates = {
        new(-12, 1.5f, 18), new(-12, 1.5f, 38), new(-12, 1.5f, 58),
        new(0, 1.5f, 67), new(12, 1.5f, 58), new(12, 1.5f, 38),
        new(12, 1.5f, 18), new(12, 1.5f, 2)
    };

    private void UpdateRace()
    {
        if (!IsSpawned || !IsServer || State != MatchState.Playing) return;
        if (raceDeadline.Value == 0) raceDeadline.Value = NetworkManager.ServerTime.Time + raceTimeLimit;
        var activeGates = ActiveGates;
        if (activeGates == null || activeGates.Length == 0) return;
        foreach (ulong id in new List<ulong>(aliveClients))
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client) || client.PlayerObject == null) continue;
            Vector3 position = client.PlayerObject.transform.position;
            int progress = raceProgress.TryGetValue(id, out int value) ? value : 0;
            if (position.y < -5)
            {
                // 소유자가 물리를 제어하므로 복귀 명령은 소유 클라이언트에 적용합니다.
                double now = NetworkManager.ServerTime.Time;
                if (!raceRespawnTimes.TryGetValue(id, out double last) || now - last > 2)
                {
                    raceRespawnTimes[id] = now;
                    bool returning = (customRaceGates == null || customRaceGates.Length == 0) && progress >= 4;
                    Vector3 respawnPos = progress == 0 ? (activeGates[0] + Vector3.back * 6f) : activeGates[progress - 1];
                    RaceRespawnClientRpc(id, respawnPos, returning);
                }
                continue;
            }
            if (progress >= activeGates.Length) continue;

            if (customRaceGates != null && customRaceGates.Length > 0)
            {
                // 직선 코스 (Door Dash 등): 코스 폭 및 높이 범위 내에서 게이트 Z 라인을 지나가면 통과 인정
                float maxDx = 9.5f;
                if (Mathf.Abs(position.x) > maxDx || position.y < -1 || position.y > 6) continue;
                if (position.z < activeGates[progress].z - 1.5f) continue;

                // 고속 이동(대시 등) 시 게이트 누락 방지를 위해 현재 Z 위치까지의 게이트 일괄 갱신
                while (progress < activeGates.Length && position.z >= activeGates[progress].z - 1.5f)
                {
                    progress++;
                }
                raceProgress[id] = progress;
            }
            else
            {
                // U자 곡선 코스 (기존 Obstacle Race): 반경 중심 박스 검사
                Vector3 delta = position - activeGates[progress];
                float maxDx = 5f;
                float maxDz = 3f;
                if (Mathf.Abs(delta.x) > maxDx || Mathf.Abs(delta.z) > maxDz || position.y < -1 || position.y > 6) continue;
                raceProgress[id] = ++progress;
            }
            if (progress != activeGates.Length) continue;
            raceFinishOrder.Add(id);
            raceFinishCount.Value = raceFinishOrder.Count;
            aliveClients.Remove(id);
            aliveCount.Value = aliveClients.Count;
            RaceFinishedClientRpc(id);
        }
        CheckRaceFinished();
    }

    private void CheckRaceFinished()
    {
        if (!IsServer || State != MatchState.Playing) return;
        if (aliveClients.Count == 0 || (raceDeadline.Value > 0 && RaceRemaining <= 0))
            FinishMatchOnServer(raceFinishOrder.Count > 0 ? raceFinishOrder[0] : ulong.MaxValue);
    }

    [ClientRpc]
    private void RaceFinishedClientRpc(ulong id)
    {
        if (id != NetworkManager.LocalClientId) return;
        localRaceFinished = true;
        var player = ResolveLocalPlayer();
        if (player != null && player.TryGetComponent(out localPlayerBody))
        {
            localPlayerBody.linearVelocity = Vector3.zero;
            localPlayerBody.angularVelocity = Vector3.zero;
            localPlayerBody.isKinematic = true;
            localBodyMadeKinematic = true;
        }
    }

    [ClientRpc]
    private void RaceRespawnClientRpc(ulong id, Vector3 position, bool returning)
    {
        if (id != NetworkManager.LocalClientId) return;
        var player = ResolveLocalPlayer();
        if (player == null) return;
        if (raceRespawnPoint == null)
        {
            raceRespawnPoint = new GameObject("Race Respawn Point").transform;
            raceRespawnPoint.SetParent(transform);
        }
        raceRespawnPoint.SetPositionAndRotation(position, Quaternion.Euler(0, returning ? 180 : 0, 0));
        player.SetCheckpoint(raceRespawnPoint);
        player.RespawnAtCheckpoint();
    }
}
