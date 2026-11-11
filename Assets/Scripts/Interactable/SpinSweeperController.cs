using Unity.Netcode;
using UnityEngine;

// 회전봉 생존(Spin Survival) 경기 진행. 서버는 경기 시작 시각과 시드만 동기화하고,
// 봉의 각도·속도·방향 전환은 모든 머신이 같은 서버 시각으로 계산한다(RaceObstacle Driven 모드가 읽어 간다).
// 서버는 일정 간격으로 바깥 발판부터 무너뜨려 경기장을 줄인다.
[DisallowMultipleComponent]
public class SpinSweeperController : NetworkBehaviour
{
    [Header("회전 (도/초)")]
    [SerializeField] private float startSpeed = 55f;
    [Tooltip("구간이 바뀔 때마다 늘어나는 속도.")]
    [SerializeField] private float speedStep = 8f;
    [SerializeField] private float maxSpeed = 150f;
    [Tooltip("속도·방향이 유지되는 구간 길이(초).")]
    [SerializeField] private float segmentSeconds = 6f;
    [Tooltip("구간이 바뀔 때 회전 방향이 반대로 바뀔 확률.")]
    [Range(0f, 1f)] [SerializeField] private float reverseChance = 0.35f;
    [Tooltip("경기 초반 이 구간 수 동안은 방향을 바꾸지 않는다.")]
    [SerializeField] private int noReverseSegments = 2;

    [Header("방향 전환 예고")]
    [SerializeField] private float reverseWarningSeconds = 1f;
    [SerializeField] private Renderer warningRenderer;
    [SerializeField] private Material warningMaterial;

    [Header("경기장 축소")]
    [SerializeField] private float shrinkStartSeconds = 25f;
    [SerializeField] private float shrinkIntervalSeconds = 15f;
    [Tooltip("한 번에 줄어드는 반경(발판 한 줄 = 2.1m).")]
    [SerializeField] private float shrinkStep = 2.1f;
    [SerializeField] private float minRadius = 5f;

    private readonly NetworkVariable<double> startTime = new NetworkVariable<double>(0);
    private readonly NetworkVariable<int> seed = new NetworkVariable<int>(0);

    private Material normalMaterial;
    private bool warningShown;
    private float initialRadius;
    private int shrinkCount;

    private void Awake()
    {
        if (warningRenderer != null)
        {
            normalMaterial = warningRenderer.sharedMaterial;
        }
    }

    private void Update()
    {
        if (IsSpawned && IsServer)
        {
            UpdateServer();
        }

        UpdateReverseWarning();
    }

    // 봉의 현재 각도(도). 경기 시작 전에는 0에 멈춰 있다.
    public float AngleAt(double time)
    {
        double elapsed = Elapsed(time);
        if (elapsed <= 0)
        {
            return 0f;
        }

        double angle = 0;
        int direction = 1;
        for (int k = 0; elapsed > 0; k++)
        {
            direction = DirectionAfter(direction, k);
            angle += direction * SpeedOf(k) * System.Math.Min(elapsed, segmentSeconds);
            elapsed -= segmentSeconds;
        }

        return (float)(angle % 360.0);
    }

    // 봉의 현재 각속도(도/초, 부호 = 방향). 피격 시 날아가는 세기 계산에 쓴다.
    public float AngularSpeedAt(double time)
    {
        double elapsed = Elapsed(time);
        if (elapsed <= 0)
        {
            return 0f;
        }

        int segment = SegmentAt(elapsed);
        return DirectionOf(segment) * SpeedOf(segment);
    }

    private void UpdateServer()
    {
        var manager = SurvivalGameManager.Instance;
        bool playing = manager != null && manager.State == SurvivalGameManager.MatchState.Playing;
        if (startTime.Value == 0)
        {
            if (!playing)
            {
                return;
            }

            seed.Value = Random.Range(1, int.MaxValue);
            startTime.Value = NetworkManager.ServerTime.Time;
            initialRadius = OuterPlatformRadius();
            shrinkCount = 0;
        }

        if (!playing)
        {
            return;
        }

        double elapsed = Elapsed(NetworkManager.ServerTime.Time);
        if (elapsed < shrinkStartSeconds + shrinkCount * shrinkIntervalSeconds)
        {
            return;
        }

        shrinkCount++;
        float radius = Mathf.Max(minRadius, initialRadius - shrinkCount * shrinkStep);
        foreach (var platform in FallingPlatform.ActivePlatforms.ToArray())
        {
            if (platform != null && !platform.IsFalling && PlanarDistance(platform.transform.position) > radius)
            {
                platform.ServerForceFall();
            }
        }
    }

    // 다음 구간에서 방향이 바뀌면 그 직전에 허브 색을 바꿔 예고한다(모든 클라가 같은 시각에 계산).
    private void UpdateReverseWarning()
    {
        if (warningRenderer == null || warningMaterial == null)
        {
            return;
        }

        bool show = false;
        double elapsed = Elapsed(CurrentTime());
        if (elapsed > 0)
        {
            int segment = SegmentAt(elapsed);
            double untilNext = (segment + 1) * segmentSeconds - elapsed;
            show = untilNext <= reverseWarningSeconds
                && DirectionOf(segment + 1) != DirectionOf(segment)
                && Mathf.Repeat((float)untilNext * 6f, 1f) < 0.5f; // 깜빡임
        }

        if (show != warningShown)
        {
            warningShown = show;
            warningRenderer.sharedMaterial = show ? warningMaterial : normalMaterial;
        }
    }

    private double Elapsed(double time) => startTime.Value > 0 ? time - startTime.Value : -1;

    private int SegmentAt(double elapsed) => (int)(elapsed / Mathf.Max(0.5f, segmentSeconds));

    private float SpeedOf(int segment) => Mathf.Min(maxSpeed, startSpeed + segment * speedStep);

    private int DirectionOf(int segment)
    {
        int direction = 1;
        for (int k = 0; k <= segment; k++)
        {
            direction = DirectionAfter(direction, k);
        }

        return direction;
    }

    private int DirectionAfter(int previous, int segment)
    {
        return segment >= noReverseSegments && Hash(seed.Value, segment) < reverseChance ? -previous : previous;
    }

    private float OuterPlatformRadius()
    {
        float radius = 0f;
        foreach (var platform in FallingPlatform.ActivePlatforms)
        {
            if (platform != null)
            {
                radius = Mathf.Max(radius, PlanarDistance(platform.transform.position));
            }
        }

        return radius;
    }

    private float PlanarDistance(Vector3 position)
    {
        Vector3 delta = position - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static double CurrentTime()
    {
        var network = NetworkManager.Singleton;
        return network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
    }
}
