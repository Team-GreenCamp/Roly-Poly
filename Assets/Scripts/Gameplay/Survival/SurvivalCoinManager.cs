using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 서바이벌 코인(정체 방지 보조): 서버가 발판 위에 코인을 뿌리고, 주우면 매치 점수를 얻는다.
// 승패(최후 1인)에는 관여하지 않는 부가 점수. 중앙에서 먼 발판일수록 가치가 높아
// 안전구역 캠핑 대신 위험 지역 이동을 유도한다. 서든데스가 시작되면 남은 코인은 전부 회수된다.
//
// 네트워킹: 코인은 NetworkObject가 아니라 순수 로컬 오브젝트다. 서버가 ClientRpc로 생성/제거를
// 지시하므로 NetworkPrefabs 등록이 필요 없고, 줍기는 로컬 플레이어 감지 → ServerRpc 검증 →
// ClientRpc 확정 순서로 처리한다(PvP 이펙트와 같은 릴레이 패턴). 매치는 전원 접속 후 시작되고
// 씬 전환 시 매니저가 새로 만들어지므로 늦은 참가자 동기화는 고려하지 않는다.
[DisallowMultipleComponent]
public class SurvivalCoinManager : NetworkBehaviour
{
    [Header("스폰")]
    [Tooltip("동시에 존재할 수 있는 최대 코인 수.")]
    [SerializeField] private int maxActiveCoins = 24;
    [Tooltip("코인 리스폰 웨이브 간격(초). 웨이브마다 최대치까지 다시 채운다.")]
    [SerializeField] private float respawnInterval = 12f;
    [Tooltip("발판 윗면 기준 코인이 떠 있는 높이(m).")]
    [SerializeField] private float coinHeightOffset = 0.9f;
    [Tooltip("이미 코인이 있는 자리 주변 이 거리(m) 안에는 새 코인을 놓지 않는다.")]
    [SerializeField] private float minCoinSpacing = 1.5f;

    [Header("가치 (중앙에서 먼 발판일수록 높음)")]
    [SerializeField] private int innerCoinValue = 1;
    [SerializeField] private int outerCoinValue = 3;
    [Tooltip("중앙~최외곽 정규화 거리가 이 값보다 크면 outer 가치를 준다.")]
    [Range(0f, 1f)] [SerializeField] private float outerDistanceThreshold = 0.6f;

    [Header("줍기")]
    [Tooltip("로컬 플레이어가 코인에 이 거리(m) 안으로 붙으면 줍는다.")]
    [SerializeField] private float pickupRadius = 1.1f;
    [Tooltip("서버 검증 여유 거리(m). 네트워크 지연 동안 이동한 거리를 감안해 넉넉히 잡는다.")]
    [SerializeField] private float serverValidationRadius = 4f;

    [Header("연출")]
    [SerializeField] private float coinSpinSpeed = 140f;
    [SerializeField] private float coinBobAmplitude = 0.15f;
    [SerializeField] private float coinBobFrequency = 1.6f;
    [SerializeField] private Color coinColor = new Color(1f, 0.82f, 0.2f, 1f);

    public static SurvivalCoinManager Instance { get; private set; }

    private class CoinData
    {
        public Vector3 position;
        public int value;
        public GameObject visual;
        public FallingPlatform platform; // 서버 전용: 발판이 무너지면 코인을 회수하기 위한 추적
    }

    // 서버·클라 공통 상태(서버는 권한 원본, 클라는 ClientRpc로 따라온다).
    private readonly Dictionary<int, CoinData> activeCoins = new Dictionary<int, CoinData>();
    private readonly Dictionary<ulong, int> scores = new Dictionary<ulong, int>();
    // 로컬에서 줍기 요청을 보낸 코인(응답 전 중복 RPC 방지).
    private readonly HashSet<int> pendingPickups = new HashSet<int>();

    private Material coinMaterial;
    private int nextCoinId;
    private float nextWaveTime;
    private bool spawningStopped; // 서든데스/매치 종료 후 true

    public int LocalScore =>
        NetworkManager != null && scores.TryGetValue(NetworkManager.LocalClientId, out int score) ? score : 0;

    public int GetScore(ulong clientId) => scores.TryGetValue(clientId, out int score) ? score : 0;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (coinMaterial != null)
        {
            Destroy(coinMaterial);
        }

        base.OnDestroy();
    }

    private void Update()
    {
        UpdateServerSpawning();
        UpdateCoinVisuals();
        UpdateLocalPickup();
    }

    // ─────────────────────────────────────────────────────────────
    // 서버: 스폰/회수
    // ─────────────────────────────────────────────────────────────
    private void UpdateServerSpawning()
    {
        if (!IsSpawned || !IsServer || spawningStopped)
        {
            return;
        }

        SurvivalGameManager gameManager = SurvivalGameManager.Instance;
        if (gameManager == null || gameManager.State != SurvivalGameManager.MatchState.Playing)
        {
            return;
        }

        // 서든데스가 시작되면 남은 코인을 전부 회수하고 스폰을 멈춘다(이후 순수 생존전).
        if (gameManager.IsSuddenDeath)
        {
            spawningStopped = true;
            ClearAllCoinsOnServer();
            return;
        }

        ReclaimCoinsOnFallenPlatforms();

        if (Time.time < nextWaveTime)
        {
            return;
        }

        nextWaveTime = Time.time + Mathf.Max(2f, respawnInterval);
        SpawnWaveOnServer();
    }

    // 서버: 코인이 놓인 발판이 무너지기 시작했거나 despawn되면 코인도 즉시 회수한다(공중 부양 방지).
    private static readonly List<int> reclaimBuffer = new List<int>();
    private void ReclaimCoinsOnFallenPlatforms()
    {
        reclaimBuffer.Clear();
        foreach (KeyValuePair<int, CoinData> entry in activeCoins)
        {
            if (entry.Value.platform == null || entry.Value.platform.IsFalling)
            {
                reclaimBuffer.Add(entry.Key);
            }
        }

        for (int i = 0; i < reclaimBuffer.Count; i++)
        {
            RemoveCoinLocal(reclaimBuffer[i]);
            RemoveCoinClientRpc(reclaimBuffer[i]);
        }
    }

    private void SpawnWaveOnServer()
    {
        int toSpawn = maxActiveCoins - activeCoins.Count;
        if (toSpawn <= 0)
        {
            return;
        }

        // 아직 무너지지 않은 발판만 후보로 쓴다(안전구역 바닥은 FallingPlatform이 아니라 자동 제외 →
        // 코인은 항상 위험 지역에만 생긴다).
        FallingPlatform[] platforms = FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None);
        List<FallingPlatform> candidates = new List<FallingPlatform>(platforms.Length);
        float maxDistance = 0.01f;
        for (int i = 0; i < platforms.Length; i++)
        {
            if (platforms[i] == null || platforms[i].IsFalling)
            {
                continue;
            }

            candidates.Add(platforms[i]);
            maxDistance = Mathf.Max(maxDistance,
                Vector3.Distance(platforms[i].transform.position, transform.position));
        }

        // 무작위 순서로 뽑되 기존 코인과 너무 가까운 자리는 건너뛴다.
        for (int i = candidates.Count - 1; i > 0 && toSpawn > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);

            FallingPlatform platform = candidates[i];
            Vector3 position = platform.transform.position + Vector3.up * coinHeightOffset;
            if (IsNearExistingCoin(position))
            {
                continue;
            }

            float normalizedDistance = Vector3.Distance(platform.transform.position, transform.position) / maxDistance;
            int value = normalizedDistance >= outerDistanceThreshold ? outerCoinValue : innerCoinValue;

            int id = nextCoinId++;
            AddCoinLocal(id, position, value);
            if (activeCoins.TryGetValue(id, out CoinData coin))
            {
                coin.platform = platform;
            }
            SpawnCoinClientRpc(id, position, value);
            toSpawn--;
        }
    }

    private bool IsNearExistingCoin(Vector3 position)
    {
        float sqrSpacing = minCoinSpacing * minCoinSpacing;
        foreach (CoinData coin in activeCoins.Values)
        {
            if ((coin.position - position).sqrMagnitude < sqrSpacing)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearAllCoinsOnServer()
    {
        ClearAllCoinsLocal();
        ClearAllCoinsClientRpc();
    }

    // ─────────────────────────────────────────────────────────────
    // 클라: 줍기 감지 (로컬 플레이어 → 서버 요청)
    // ─────────────────────────────────────────────────────────────
    private void UpdateLocalPickup()
    {
        if (!IsSpawned || activeCoins.Count == 0 || NetworkManager == null || !NetworkManager.IsClient)
        {
            return;
        }

        SurvivalGameManager gameManager = SurvivalGameManager.Instance;
        if (gameManager == null || gameManager.State != SurvivalGameManager.MatchState.Playing
            || gameManager.LocalEliminated)
        {
            return;
        }

        NetworkObject playerObject = NetworkManager.LocalClient?.PlayerObject;
        if (playerObject == null)
        {
            return;
        }

        Vector3 playerPosition = playerObject.transform.position;
        float sqrRadius = pickupRadius * pickupRadius;

        foreach (KeyValuePair<int, CoinData> entry in activeCoins)
        {
            if (pendingPickups.Contains(entry.Key))
            {
                continue;
            }

            // 코인은 발 위에 떠 있으므로 수평/수직 모두 감안한 단순 구 판정이면 충분하다.
            if ((entry.Value.position - (playerPosition + Vector3.up * coinHeightOffset)).sqrMagnitude > sqrRadius
                && (entry.Value.position - playerPosition).sqrMagnitude > sqrRadius)
            {
                continue;
            }

            pendingPickups.Add(entry.Key);
            RequestPickupServerRpc(entry.Key);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(int coinId, ServerRpcParams rpcParams = default)
    {
        if (!activeCoins.TryGetValue(coinId, out CoinData coin))
        {
            return; // 이미 다른 플레이어가 주움
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        // 위치 검증: NOA 동기화로 서버에도 플레이어 트랜스폼이 반영되므로 거리로 판별한다.
        // (지연 동안의 이동을 감안해 넉넉한 반경. 신원은 SenderClientId라 남의 코인 대리 획득은 불가.)
        if (NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
            && client.PlayerObject != null)
        {
            float sqrDistance = (client.PlayerObject.transform.position - coin.position).sqrMagnitude;
            if (sqrDistance > serverValidationRadius * serverValidationRadius)
            {
                return;
            }
        }

        scores.TryGetValue(senderClientId, out int score);
        score += coin.value;
        scores[senderClientId] = score;

        Vector3 coinPosition = coin.position;
        RemoveCoinLocal(coinId);
        CoinCollectedClientRpc(coinId, senderClientId, score, coinPosition);
    }

    // ─────────────────────────────────────────────────────────────
    // ClientRpc (호스트에서도 실행되므로 서버 선반영분은 가드로 건너뛴다)
    // ─────────────────────────────────────────────────────────────
    [ClientRpc]
    private void SpawnCoinClientRpc(int coinId, Vector3 position, int value)
    {
        AddCoinLocal(coinId, position, value);
    }

    [ClientRpc]
    private void CoinCollectedClientRpc(int coinId, ulong clientId, int newScore, Vector3 position)
    {
        scores[clientId] = newScore;
        RemoveCoinLocal(coinId);
        pendingPickups.Remove(coinId);

        // 획득 지점 반짝임/사운드는 모든 클라이언트에서 재생(누가 먹었는지 주변에서도 보인다).
        // 위치는 파라미터로 받는다 — 호스트는 서버가 이미 코인을 제거한 뒤라 로컬 조회가 안 된다.
        GameFeedback.CoinPickupAt(position);
    }

    // 발판 낙하 등으로 서버가 코인을 회수할 때(획득 아님 — 연출 없이 조용히 제거).
    [ClientRpc]
    private void RemoveCoinClientRpc(int coinId)
    {
        RemoveCoinLocal(coinId);
        pendingPickups.Remove(coinId);
    }

    [ClientRpc]
    private void ClearAllCoinsClientRpc()
    {
        ClearAllCoinsLocal();
    }

    // ─────────────────────────────────────────────────────────────
    // 공통 로컬 상태/비주얼
    // ─────────────────────────────────────────────────────────────
    private void AddCoinLocal(int coinId, Vector3 position, int value)
    {
        if (activeCoins.ContainsKey(coinId))
        {
            return;
        }

        CoinData coin = new CoinData { position = position, value = value };

        // 헤드리스 데디케이티드 서버에서는 비주얼을 만들지 않는다(상태만 유지).
        if (NetworkManager != null && NetworkManager.IsClient)
        {
            coin.visual = CreateCoinVisual(position, value);
        }

        activeCoins[coinId] = coin;
    }

    private void RemoveCoinLocal(int coinId)
    {
        if (!activeCoins.TryGetValue(coinId, out CoinData coin))
        {
            return;
        }

        if (coin.visual != null)
        {
            Destroy(coin.visual);
        }

        activeCoins.Remove(coinId);
    }

    private void ClearAllCoinsLocal()
    {
        foreach (CoinData coin in activeCoins.Values)
        {
            if (coin.visual != null)
            {
                Destroy(coin.visual);
            }
        }

        activeCoins.Clear();
        pendingPickups.Clear();
    }

    private GameObject CreateCoinVisual(Vector3 position, int value)
    {
        GameObject root = new GameObject($"Coin ({value})");
        root.transform.position = position;

        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Collider primitiveCollider = disc.GetComponent<Collider>();
        if (primitiveCollider != null)
        {
            Destroy(primitiveCollider); // 줍기는 거리 판정이라 콜라이더 불필요(물리 간섭 방지)
        }

        disc.transform.SetParent(root.transform, false);
        // 세워진 동전 모양: 눕힌 원판을 X축으로 90° 세우고 루트를 Y축으로 회전시킨다.
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        float diameter = value >= outerCoinValue ? 0.6f : 0.45f; // 고가치 코인은 조금 크게
        disc.transform.localScale = new Vector3(diameter, 0.04f, diameter);

        if (coinMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            coinMaterial = shader != null ? new Material(shader) : new Material(disc.GetComponent<Renderer>().sharedMaterial);
            coinMaterial.color = coinColor;
            if (coinMaterial.HasProperty("_EmissionColor"))
            {
                coinMaterial.EnableKeyword("_EMISSION");
                coinMaterial.SetColor("_EmissionColor", coinColor * 0.8f);
            }
        }

        disc.GetComponent<Renderer>().sharedMaterial = coinMaterial;
        return root;
    }

    private void UpdateCoinVisuals()
    {
        if (activeCoins.Count == 0)
        {
            return;
        }

        float spin = coinSpinSpeed * Time.deltaTime;
        float bob = Mathf.Sin(Time.time * coinBobFrequency * Mathf.PI * 2f) * coinBobAmplitude;

        foreach (CoinData coin in activeCoins.Values)
        {
            if (coin.visual == null)
            {
                continue;
            }

            coin.visual.transform.position = coin.position + Vector3.up * bob;
            coin.visual.transform.Rotate(0f, spin, 0f, Space.World);
        }
    }
}
