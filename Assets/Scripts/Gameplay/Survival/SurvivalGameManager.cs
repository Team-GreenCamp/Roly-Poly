using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 최후 1인 서바이벌 매치 관리자. 아레나 씬에만 배치되는 인씬 NetworkObject.
//
// 역할 경계
//  • 세션/씬 전환은 NetworkSessionManager(NSM)가 담당 — 매치는 StartGame()으로 들어와 ReturnToLobby()로 나간다.
//  • 이 컴포넌트는 매치 한 판의 상태만 소유한다. 씬이 언로드되면 함께 파괴되어 상태 초기화가 자동으로 된다.
//  • 판정은 전부 서버, 낙사 감지는 소유자(EliminationZone) — Combat/RespawnHazard와 동일한 패턴.
//
// 흐름: Waiting(전원 스폰 대기) → Countdown(입력 잠금) → Playing → 생존자 1명 → Finished(승자 패널) → 로비 복귀.
// 접속 종료 = 탈락. 호스트 단독(solo) 테스트에서는 생존 1명으로 종료하지 않고, 자신이 떨어져야 종료된다.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class SurvivalGameManager : NetworkBehaviour
{
    public enum MatchState : byte { Waiting = 0, Countdown = 1, Playing = 2, Finished = 3 }

    [Header("Match")]
    [Tooltip("시작 카운트다운 길이(초).")]
    [SerializeField] private float countdownSeconds = 3f;
    [Tooltip("승자 발표 후 로비로 복귀하기까지의 시간(초).")]
    [SerializeField] private float returnToLobbyDelaySeconds = 5f;
    [Tooltip("전원 스폰을 기다리는 최대 시간(초). 초과하면 스폰된 인원만으로 시작합니다.")]
    [SerializeField] private float maxWaitForSpawnSeconds = 10f;
    [Tooltip("로비 복귀 실패 시 폴백으로 로드할 씬 이름.")]
    [SerializeField] private string lobbySceneName = "Lobby Scene";

    [Header("Elimination")]
    [Tooltip("서버 안전망: 이 높이(y) 아래의 생존자는 소유자 보고가 유실돼도 서버가 직접 탈락 처리합니다.")]
    [SerializeField] private float killY = -30f;
    [SerializeField] private float killSweepInterval = 0.5f;
    [Tooltip("이 시간(초) 안에 맞은 공격이 있으면 탈락 크레딧(킬)을 그 공격자에게 줍니다.")]
    [SerializeField] private float killCreditWindowSeconds = 5f;

    [Header("Sudden Death (정체 방지)")]
    [Tooltip("매치 시작 후 이 시간(초)이 지나면 서든데스 — 발판이 바깥쪽부터 순차적으로 무너집니다.")]
    [SerializeField] private float suddenDeathStartSeconds = 60f;
    [Tooltip("발판이 하나씩 무너지는 간격(초).")]
    [SerializeField] private float suddenDeathTileInterval = 0.8f;

    // ───── 동기화 상태 (서버 쓰기, 전원 읽기) ─────
    private readonly NetworkVariable<byte> matchState = new NetworkVariable<byte>((byte)MatchState.Waiting);
    private readonly NetworkVariable<int> aliveCount = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> totalPlayerCount = new NetworkVariable<int>(0);
    private readonly NetworkVariable<double> countdownEndTime = new NetworkVariable<double>(0);
    private readonly NetworkVariable<ulong> winnerClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    // 서든데스가 시작되는 서버 시각(HUD 카운트다운용). 0이면 미정.
    private readonly NetworkVariable<double> suddenDeathStartTime = new NetworkVariable<double>(0);

    // ───── 서버 로컬 ─────
    private readonly HashSet<ulong> aliveClients = new HashSet<ulong>();
    private bool soloMode;
    private ulong lastEliminatedClientId = ulong.MaxValue;
    private float nextKillSweepTime;

    // ───── 클라 로컬 (유령화/복원, 관전) ─────
    private readonly HashSet<ulong> eliminatedClients = new HashSet<ulong>();
    private readonly List<(Renderer renderer, bool wasEnabled)> hiddenRenderers = new List<(Renderer, bool)>();
    private readonly List<(Collider collider, bool wasEnabled)> hiddenColliders = new List<(Collider, bool)>();
    private readonly List<(Behaviour behaviour, bool wasEnabled)> disabledBehaviours = new List<(Behaviour, bool)>();
    private PlayerController localPlayerController;
    private Rigidbody localPlayerBody;
    private bool localEliminated;
    private bool localBodyMadeKinematic;
    private SpectatorController spectator;

    public static SurvivalGameManager Instance { get; private set; }

    // HUD 연출 트리거용 이벤트(상태 표시는 아래 프로퍼티 폴링 권장 — 초기값 콜백 미발화 문제 회피).
    public event Action OnLocalEliminated;
    public event Action<ulong> OnWinnerDecided;
    // 킬 피드용: (피해자 clientId, 킬러 clientId — 없으면 ulong.MaxValue)
    public event Action<ulong, ulong> OnPlayerEliminated;

    public MatchState State => (MatchState)matchState.Value;
    public int AliveCount => aliveCount.Value;
    public int TotalPlayerCount => totalPlayerCount.Value;
    public ulong WinnerClientId => winnerClientId.Value;
    public bool LocalEliminated => localEliminated;
    public double CountdownRemaining =>
        NetworkManager != null ? countdownEndTime.Value - NetworkManager.ServerTime.Time : 0.0;
    // 서든데스까지 남은 시간(초). 음수면 이미 시작됨, 0 반환이면 미정(Waiting 등).
    public double SuddenDeathRemaining =>
        NetworkManager != null && suddenDeathStartTime.Value > 0
            ? suddenDeathStartTime.Value - NetworkManager.ServerTime.Time
            : double.MaxValue;
    public bool IsSuddenDeath =>
        NetworkManager != null && suddenDeathStartTime.Value > 0
        && NetworkManager.ServerTime.Time >= suddenDeathStartTime.Value;

    public bool IsEliminated(ulong clientId) => eliminatedClients.Contains(clientId);

    private void Awake()
    {
        Instance = this;
        spectator = GetComponent<SpectatorController>();
    }

    public override void OnNetworkSpawn()
    {
        matchState.OnValueChanged += HandleMatchStateChanged;

        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            StartCoroutine(ServerMatchFlow());
        }
    }

    public override void OnNetworkDespawn()
    {
        matchState.OnValueChanged -= HandleMatchStateChanged;

        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    public override void OnDestroy()
    {
        // 플레이어 NetworkObject는 씬 전환을 넘어 유지되므로, 씬 언로드 시(로비 복귀 포함)
        // 매치에서 적용한 유령화/kinematic을 반드시 되돌린다. 안 하면 로비에 투명 캐릭터로 돌아간다.
        RestoreAllGhosting();

        // 승자 세리머니(Spin)도 로비까지 이어지지 않게 끈다.
        if (localPlayerController != null)
        {
            localPlayerController.SetCelebrating(false);
        }

        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void Update()
    {
        UpdateLocalInputLock();
        UpdateServerKillSweep();
    }

    // ─────────────────────────────────────────────────────────────
    // 서버: 매치 진행
    // ─────────────────────────────────────────────────────────────
    private IEnumerator ServerMatchFlow()
    {
        // Waiting: NGO 씬 전환으로 유지 스폰되는 플레이어 오브젝트들이 전부 준비될 때까지 대기.
        float waitDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, maxWaitForSpawnSeconds);
        while (!AllPlayersSpawned() && Time.realtimeSinceStartup < waitDeadline)
        {
            yield return null;
        }

        yield return new WaitForSeconds(1f); // 스폰 배치/카메라 바인딩 정리 유예

        // Countdown: 생존자 스냅샷 확정.
        aliveClients.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            aliveClients.Add(clientId);
        }

        soloMode = aliveClients.Count <= 1;
        aliveCount.Value = aliveClients.Count;
        totalPlayerCount.Value = aliveClients.Count;
        countdownEndTime.Value = NetworkManager.ServerTime.Time + Mathf.Max(1f, countdownSeconds);
        matchState.Value = (byte)MatchState.Countdown;

        while (NetworkManager.ServerTime.Time < countdownEndTime.Value)
        {
            yield return null;
        }

        matchState.Value = (byte)MatchState.Playing;

        // 서든데스 예약: 정체(둘 다 싸움을 피함) 방지. 시간이 되면 발판을 바깥쪽부터 무너뜨린다.
        if (suddenDeathStartSeconds > 0f)
        {
            suddenDeathStartTime.Value = NetworkManager.ServerTime.Time + suddenDeathStartSeconds;
            StartCoroutine(ServerSuddenDeathFlow());
        }
    }

    // 서든데스(서버): 남은 발판/바닥 타일을 중심에서 먼 순서대로 하나씩 강제로 무너뜨린다.
    private IEnumerator ServerSuddenDeathFlow()
    {
        while (State == MatchState.Playing && NetworkManager.ServerTime.Time < suddenDeathStartTime.Value)
        {
            yield return null;
        }

        if (State != MatchState.Playing)
        {
            yield break;
        }

        FallingPlatform[] platforms = FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None);
        System.Array.Sort(platforms, (a, b) =>
        {
            float da = a != null ? (a.transform.position - transform.position).sqrMagnitude : 0f;
            float db = b != null ? (b.transform.position - transform.position).sqrMagnitude : 0f;
            return db.CompareTo(da); // 바깥쪽(먼 것)부터
        });

        for (int i = 0; i < platforms.Length; i++)
        {
            if (State != MatchState.Playing)
            {
                yield break;
            }

            if (platforms[i] != null)
            {
                platforms[i].ServerForceFall();
            }

            yield return new WaitForSeconds(Mathf.Max(0.2f, suddenDeathTileInterval));
        }
    }

    private bool AllPlayersSpawned()
    {
        if (NetworkManager == null)
        {
            return false;
        }

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null
                || !client.PlayerObject.IsSpawned)
            {
                return false;
            }
        }

        return true;
    }

    // 서버 안전망: 소유자 보고가 유실돼도 killY 아래 생존자를 직접 탈락 확정한다.
    // (원격 플레이어 위치는 NOA 동기화가 서버 transform에도 반영하므로 서버에서 판정 가능.)
    private void UpdateServerKillSweep()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        MatchState state = State;
        if (state != MatchState.Countdown && state != MatchState.Playing)
        {
            return;
        }

        if (Time.time < nextKillSweepTime)
        {
            return;
        }

        nextKillSweepTime = Time.time + Mathf.Max(0.1f, killSweepInterval);

        // EliminateOnServer가 aliveClients를 수정하므로 복사본으로 순회한다.
        ulong[] aliveSnapshot = new ulong[aliveClients.Count];
        aliveClients.CopyTo(aliveSnapshot);

        for (int i = 0; i < aliveSnapshot.Length; i++)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(aliveSnapshot[i], out NetworkClient client)
                || client.PlayerObject == null)
            {
                continue;
            }

            if (client.PlayerObject.transform.position.y < killY)
            {
                EliminateOnServer(aliveSnapshot[i]);
            }
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            // 접속 종료 = 탈락.
            EliminateOnServer(clientId);
        }
    }

    // 탈락 확정(서버 전용). aliveClients에서 제거 성공했을 때만 진행하므로 중복 보고는 자연 무시된다.
    private void EliminateOnServer(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        MatchState state = State;
        if (state != MatchState.Countdown && state != MatchState.Playing)
        {
            return;
        }

        if (!aliveClients.Remove(clientId))
        {
            return;
        }

        // 킬 크레딧: 최근에 이 플레이어를 때린 공격자가 있으면 킬로 인정.
        ulong killerClientId = ulong.MaxValue;
        if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient victimClient)
            && victimClient.PlayerObject != null)
        {
            PlayerController victimPlayer = victimClient.PlayerObject.GetComponent<PlayerController>();
            if (victimPlayer != null && !victimPlayer.TryGetRecentAttacker(killCreditWindowSeconds, out killerClientId))
            {
                killerClientId = ulong.MaxValue; // 스스로 떨어짐
            }
        }

        lastEliminatedClientId = clientId;
        aliveCount.Value = aliveClients.Count;
        EliminatePlayerClientRpc(clientId, killerClientId);
        CheckWinnerOnServer();
    }

    private void CheckWinnerOnServer()
    {
        if (soloMode)
        {
            // 단독 테스트: 생존 1명으로는 절대 종료하지 않는다(시작 즉시 승리 방지).
            // 자신이 떨어져 0명이 되면 그를 승자로 종료해 혼자서도 전체 루프를 검증할 수 있다.
            if (aliveClients.Count == 0)
            {
                FinishMatchOnServer(lastEliminatedClientId);
            }

            return;
        }

        if (aliveClients.Count > 1)
        {
            return;
        }

        // 남은 1명이 승자. 동시 낙사로 0명이 되면 마지막 탈락자를 승자로 처리한다.
        ulong winner = lastEliminatedClientId;
        foreach (ulong clientId in aliveClients)
        {
            winner = clientId;
        }

        FinishMatchOnServer(winner);
    }

    private void FinishMatchOnServer(ulong winner)
    {
        winnerClientId.Value = winner;
        matchState.Value = (byte)MatchState.Finished;

        // 세션 승수 갱신 + 전 클라이언트 동기화(로비 이름표 ★ 표시용).
        int totalWins = SurvivalWinTracker.AddWin(winner);
        AnnounceWinnerClientRpc(winner, totalWins);

        StartCoroutine(ReturnToLobbyAfterDelay());
    }

    [ClientRpc]
    private void AnnounceWinnerClientRpc(ulong winner, int totalWins)
    {
        // 호스트에서도 실행되지만 SetWins는 멱등이라 무해하다.
        SurvivalWinTracker.SetWins(winner, totalWins);
    }

    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, returnToLobbyDelaySeconds));

        NetworkSessionManager sessionManager = FindFirstObjectByType<NetworkSessionManager>();
        if (sessionManager != null && sessionManager.ReturnToLobby())
        {
            yield break;
        }

        // 폴백: NSM이 없거나 실패하면 NGO SceneManager로 직접 로비를 로드한다.
        if (NetworkManager != null && NetworkManager.SceneManager != null && !string.IsNullOrWhiteSpace(lobbySceneName))
        {
            NetworkManager.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 낙사 보고 (소유자 → 서버)
    // ─────────────────────────────────────────────────────────────
    // EliminationZone(소유자 감지)에서 호출. 신원은 서버가 SenderClientId로만 판별하므로
    // 조작된 클라이언트도 자기 자신만 탈락시킬 수 있다.
    public void ReportLocalPlayerFell()
    {
        if (!IsSpawned || NetworkManager == null)
        {
            return;
        }

        if (IsServer)
        {
            EliminateOnServer(NetworkManager.LocalClientId);
        }
        else
        {
            ReportFallServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportFallServerRpc(ServerRpcParams rpcParams = default)
    {
        EliminateOnServer(rpcParams.Receive.SenderClientId);
    }

    // ─────────────────────────────────────────────────────────────
    // 클라: 탈락 연출(유령화) + 관전 진입
    // ─────────────────────────────────────────────────────────────
    [ClientRpc]
    private void EliminatePlayerClientRpc(ulong clientId, ulong killerClientId)
    {
        if (!eliminatedClients.Add(clientId))
        {
            return;
        }

        GhostPlayer(clientId);

        if (NetworkManager != null && clientId == NetworkManager.LocalClientId)
        {
            BeginLocalElimination();
        }

        // 킬 피드(HUD)용 알림. killer가 없으면(스스로 낙사) ulong.MaxValue.
        OnPlayerEliminated?.Invoke(clientId, killerClientId);

        // 관전 중이던 대상이 탈락했으면 다음 생존자로 넘긴다.
        if (spectator != null)
        {
            spectator.HandleAliveListChanged();
        }
    }

    // 탈락자 캐릭터를 모든 클라이언트에서 숨긴다. PlayerObject는 NGO 소유 구조/로비 복귀를 위해
    // Despawn하지 않고 렌더러/콜라이더만 끈다. 원래 값은 기록해 씬 언로드 시 복원한다.
    private void GhostPlayer(ulong clientId)
    {
        NetworkOwnedObjectActivator target = FindPlayerActivator(clientId);
        if (target == null)
        {
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            hiddenRenderers.Add((renderers[i], renderers[i].enabled));
            renderers[i].enabled = false;
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
            {
                continue;
            }

            hiddenColliders.Add((colliders[i], colliders[i].enabled));
            colliders[i].enabled = false;
        }
    }

    private void BeginLocalElimination()
    {
        localEliminated = true;

        PlayerController player = ResolveLocalPlayer();
        if (player != null)
        {
            player.SetGameplayInputEnabled(false);

            // 상호작용/등반 입력은 gameplayInputEnabled와 무관하게 동작하므로 함께 꺼 둔다(복원 대상 기록).
            DisableLocalBehaviour(player.GetComponent<PlayerInteractor>());
            DisableLocalBehaviour(player.GetComponent<PlayerClimber>());

            if (localPlayerBody == null)
            {
                localPlayerBody = player.GetComponent<Rigidbody>();
            }

            if (localPlayerBody != null && !localPlayerBody.isKinematic)
            {
                // 탈락 지점에 고정(커스텀 중력 정지). NOA 위치 동기화는 계속 돌지만 무해.
                localPlayerBody.linearVelocity = Vector3.zero;
                localPlayerBody.angularVelocity = Vector3.zero;
                localPlayerBody.isKinematic = true;
                localBodyMadeKinematic = true;
            }
        }

        if (spectator != null)
        {
            spectator.BeginSpectating();
        }

        OnLocalEliminated?.Invoke();
    }

    private void DisableLocalBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled)
        {
            return;
        }

        disabledBehaviours.Add((behaviour, true));
        behaviour.enabled = false;
    }

    private void RestoreAllGhosting()
    {
        for (int i = 0; i < hiddenRenderers.Count; i++)
        {
            if (hiddenRenderers[i].renderer != null)
            {
                hiddenRenderers[i].renderer.enabled = hiddenRenderers[i].wasEnabled;
            }
        }

        for (int i = 0; i < hiddenColliders.Count; i++)
        {
            if (hiddenColliders[i].collider != null)
            {
                hiddenColliders[i].collider.enabled = hiddenColliders[i].wasEnabled;
            }
        }

        for (int i = 0; i < disabledBehaviours.Count; i++)
        {
            if (disabledBehaviours[i].behaviour != null)
            {
                disabledBehaviours[i].behaviour.enabled = disabledBehaviours[i].wasEnabled;
            }
        }

        hiddenRenderers.Clear();
        hiddenColliders.Clear();
        disabledBehaviours.Clear();

        if (localBodyMadeKinematic && localPlayerBody != null)
        {
            localPlayerBody.isKinematic = false;
            localBodyMadeKinematic = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 클라: 카운트다운/종료 입력 잠금
    // ─────────────────────────────────────────────────────────────
    // NOA가 씬 로드/스폰 시 SetGameplayInputEnabled(true)로 덮어쓰므로 콜백 순서에 의존하지 않고
    // 매 프레임 강제한다. SetGameplayInputEnabled는 값 변화 가드가 있어 반복 호출 비용이 없다.
    private void UpdateLocalInputLock()
    {
        if (!IsSpawned || localEliminated)
        {
            return;
        }

        PlayerController player = ResolveLocalPlayer();
        if (player != null)
        {
            player.SetGameplayInputEnabled(State == MatchState.Playing);
        }
    }

    private PlayerController ResolveLocalPlayer()
    {
        if (localPlayerController != null)
        {
            return localPlayerController;
        }

        NetworkManager networkManager = NetworkManager;
        if (networkManager == null || networkManager.LocalClient == null || networkManager.LocalClient.PlayerObject == null)
        {
            return null;
        }

        localPlayerController = networkManager.LocalClient.PlayerObject.GetComponent<PlayerController>();
        if (localPlayerController != null)
        {
            localPlayerBody = localPlayerController.GetComponent<Rigidbody>();
        }

        return localPlayerController;
    }

    private static NetworkOwnedObjectActivator FindPlayerActivator(ulong clientId)
    {
        NetworkOwnedObjectActivator[] activators =
            FindObjectsByType<NetworkOwnedObjectActivator>(FindObjectsSortMode.None);

        for (int i = 0; i < activators.Length; i++)
        {
            if (activators[i] != null && activators[i].OwnerClientId == clientId)
            {
                return activators[i];
            }
        }

        return null;
    }

    private void HandleMatchStateChanged(byte previousValue, byte newValue)
    {
        if ((MatchState)newValue == MatchState.Finished)
        {
            // 서버는 winnerClientId를 matchState보다 먼저 세팅하므로 이 시점에 값이 유효하다.
            OnWinnerDecided?.Invoke(winnerClientId.Value);

            // 승자 세리머니: 승자의 소유자 클라이언트가 Spin 상태를 켠다(animState 동기화로 모두에게 보임).
            if (NetworkManager != null && winnerClientId.Value == NetworkManager.LocalClientId)
            {
                PlayerController localPlayer = ResolveLocalPlayer();
                if (localPlayer != null)
                {
                    localPlayer.SetCelebrating(true);
                }
            }
        }
    }
}
