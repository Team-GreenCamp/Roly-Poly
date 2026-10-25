using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class SurvivalGameManager
{
    [Header("Obstacle Race")]
    [SerializeField] private bool raceMode;
    [SerializeField] private float raceTimeLimit = 180f;
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

    // U자 코스를 순서대로 통과해야 완주로 인정합니다. 서버가 동기화된 위치로 판정합니다.
    // 마지막 게이트가 결승선. 코스를 바꾸면 씬에서 게이트 좌표만 고치면 된다.
    [SerializeField] private Vector3[] raceGates = {
        new(-12, 1.5f, 18), new(-12, 1.5f, 38), new(-12, 1.5f, 58),
        new(0, 1.5f, 67), new(12, 1.5f, 58), new(12, 1.5f, 38),
        new(12, 1.5f, 18), new(12, 1.5f, 2)
    };
    [Tooltip("게이트를 하나도 통과하지 못하고 떨어졌을 때 돌아갈 위치.")]
    [SerializeField] private Vector3 raceStartRespawn = new(-12, 1.5f, 0);
    [Tooltip("이 번호(통과한 게이트 수) 이상이면 돌아오는 구간으로 보고 리스폰 방향을 뒤집는다.")]
    [SerializeField] private int raceReturnGateCount = 4;
    private Vector3[] RaceGates => raceGates;

    // 실시간 순위표용 상태. 서버가 순위 순서대로 채우고 모든 클라가 그대로 그린다.
    public struct RaceStanding : INetworkSerializable, System.IEquatable<RaceStanding>
    {
        public ulong ClientId;
        public byte Progress;  // 통과한 게이트 수
        public byte Percent;   // 코스 진행률 0~100 (완주 전에는 최대 99)
        public bool Finished;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref Percent);
            serializer.SerializeValue(ref Finished);
        }

        public bool Equals(RaceStanding other) => ClientId == other.ClientId && Progress == other.Progress
            && Percent == other.Percent && Finished == other.Finished;
    }

    private readonly NetworkList<RaceStanding> raceStandings = new();
    private readonly List<RaceStanding> standingsBuffer = new();
    private readonly List<(RaceStanding standing, float distance)> standingsSort = new();
    private float nextStandingsUpdateTime;
    private const float StandingsUpdateInterval = 0.25f;
    public NetworkList<RaceStanding> RaceStandings => raceStandings;

    private void UpdateRace()
    {
        if (!IsSpawned || !IsServer || State != MatchState.Playing) return;
        if (raceDeadline.Value == 0) raceDeadline.Value = NetworkManager.ServerTime.Time + raceTimeLimit;
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
                    RaceRespawnClientRpc(id, progress == 0 ? raceStartRespawn : RaceGates[progress - 1], progress >= raceReturnGateCount);
                }
                continue;
            }
            if (!HasPassedRaceGate(progress, position)) continue;
            raceProgress[id] = ++progress;
            if (progress != RaceGates.Length) continue;
            raceFinishOrder.Add(id);
            raceFinishCount.Value = raceFinishOrder.Count;
            aliveClients.Remove(id);
            aliveCount.Value = aliveClients.Count;
            RaceFinishedClientRpc(id);
        }
        UpdateRaceStandings();
        CheckRaceFinished();
    }

    // 순위: 완주자(완주 순) → 통과 게이트 수 많은 순 → 다음 게이트까지 가까운 순. 4Hz로 갱신.
    private void UpdateRaceStandings()
    {
        if (Time.unscaledTime < nextStandingsUpdateTime) return;
        nextStandingsUpdateTime = Time.unscaledTime + StandingsUpdateInterval;

        standingsSort.Clear();
        for (int i = 0; i < raceFinishOrder.Count; i++)
            standingsSort.Add((new RaceStanding { ClientId = raceFinishOrder[i], Progress = (byte)RaceGates.Length, Percent = 100, Finished = true }, -1000f + i));
        foreach (ulong id in aliveClients)
        {
            int progress = raceProgress.TryGetValue(id, out int value) ? value : 0;
            float distance = 0f;
            if (NetworkManager.ConnectedClients.TryGetValue(id, out var client) && client.PlayerObject != null && progress < RaceGates.Length)
                distance = Vector3.ProjectOnPlane(RaceGates[progress] - client.PlayerObject.transform.position, Vector3.up).magnitude;
            byte percent = (byte)Mathf.Min(99, Mathf.FloorToInt(RaceCompletion(progress, distance) * 100f));
            standingsSort.Add((new RaceStanding { ClientId = id, Progress = (byte)progress, Percent = percent, Finished = false }, distance));
        }
        standingsSort.Sort((a, b) =>
        {
            if (a.standing.Finished != b.standing.Finished) return a.standing.Finished ? -1 : 1;
            if (a.standing.Finished) return a.distance.CompareTo(b.distance); // 완주 순서
            int byProgress = b.standing.Progress.CompareTo(a.standing.Progress);
            return byProgress != 0 ? byProgress : a.distance.CompareTo(b.distance);
        });

        standingsBuffer.Clear();
        foreach (var entry in standingsSort) standingsBuffer.Add(entry.standing);

        // 바뀐 칸만 갱신해 네트워크 이벤트를 줄인다.
        if (raceStandings.Count != standingsBuffer.Count)
        {
            raceStandings.Clear();
            foreach (var s in standingsBuffer) raceStandings.Add(s);
            return;
        }
        for (int i = 0; i < standingsBuffer.Count; i++)
            if (!raceStandings[i].Equals(standingsBuffer[i])) raceStandings[i] = standingsBuffer[i];
    }

    // 출발점→게이트들을 잇는 경로 길이 기준 진행률(0~1). 통과한 구간 + 지금 구간에서 다음 게이트에 다가간 만큼.
    private float RaceCompletion(int progress, float distanceToNext)
    {
        float total = 0f, done = 0f;
        Vector3 from = raceStartRespawn;
        for (int i = 0; i < RaceGates.Length; i++)
        {
            float length = Vector3.ProjectOnPlane(RaceGates[i] - from, Vector3.up).magnitude;
            total += length;
            if (i < progress) done += length;
            else if (i == progress) done += Mathf.Clamp(length - distanceToNext, 0f, length);
            from = RaceGates[i];
        }
        return total > 0f ? Mathf.Clamp01(done / total) : 0f;
    }

    // 중간 게이트는 게이트 주변 박스에 들어오면 통과(회전 구간 어느 레인으로 돌아도 인정되도록 넉넉하게).
    // 결승 게이트는 직전 게이트에서 오는 방향 기준으로 실제 결승선을 '넘었을 때'만 완주로 본다.
    // (박스 판정이면 눈에 보이는 결승선보다 몇 m 앞에서 완주 처리된다)
    private bool HasPassedRaceGate(int index, Vector3 position)
    {
        if (position.y < 0 || position.y > 5) return false;
        Vector3 gate = RaceGates[index];
        Vector3 delta = position - gate;
        if (index < RaceGates.Length - 1)
            return Mathf.Abs(delta.x) <= 5 && Mathf.Abs(delta.z) <= 3;

        Vector3 from = index > 0 ? RaceGates[index - 1] : raceStartRespawn;
        Vector3 forward = Vector3.ProjectOnPlane(gate - from, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        float along = Vector3.Dot(Vector3.ProjectOnPlane(delta, Vector3.up), forward);
        float lateral = Mathf.Abs(Vector3.Dot(delta, side));
        return along >= 0f && along <= 4f && lateral <= 5f;
    }

    private void CheckRaceFinished()
    {
        if (!IsServer || State != MatchState.Playing) return;
        if (aliveClients.Count == 0 || (raceDeadline.Value > 0 && RaceRemaining <= 0))
            FinishMatchOnServer(FirstConnectedRaceFinisher());
    }

    // 1등으로 들어왔어도 이후 나갔으면 승자·승수에서 빼고 다음 완주자에게 넘긴다.
    private ulong FirstConnectedRaceFinisher()
    {
        foreach (ulong id in raceFinishOrder)
        {
            if (IsConnectedForResult(id)) return id;
        }
        return ulong.MaxValue;
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
