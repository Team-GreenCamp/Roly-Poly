using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
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

    [Header("Mode (씬마다 다르게)")]
    [Tooltip("HUD 등에 쓸 모드 이름(예: 서바이벌, 스모 링아웃, 떨어지는 바닥).")]
    [SerializeField] private string modeName = "서바이벌";
    [Tooltip("이 씬의 넉백 세기 배율. 스모 링아웃처럼 서로 밀어내는 맵은 크게(예: 2~3).")]
    [SerializeField] private float knockbackMultiplier = 1f;

    [Header("Match")]
    [Tooltip("시작 카운트다운 길이(초).")]
    [SerializeField] private float countdownSeconds = 3f;
    [Tooltip("승자 발표 후 로비로 복귀하기까지의 시간(초).")]
    [SerializeField] private float returnToLobbyDelaySeconds = 5f;
    [Tooltip("전원 스폰을 기다리는 최대 시간(초). 초과하면 스폰된 인원만으로 시작합니다.")]
    [SerializeField] private float maxWaitForSpawnSeconds = 10f;
    [Tooltip("데디케이티드 서버 전용: 이 인원 이상 접속할 때까지 매치를 시작하지 않습니다(호스트/로컬 플레이엔 영향 없음).")]
    [SerializeField] private int minPlayersToStart = 2;
    [Tooltip("로비 복귀 실패 시 폴백으로 로드할 씬 이름.")]
    [SerializeField] private string lobbySceneName = "Lobby Scene";

    [Header("Elimination")]
    [Tooltip("서버 안전망: 이 높이(y) 아래의 생존자는 소유자 보고가 유실돼도 서버가 직접 탈락 처리합니다.")]
    [SerializeField] private float killY = -30f;
    [SerializeField] private float killSweepInterval = 0.5f;
    [Tooltip("이 시간(초) 안에 맞은 공격이 있으면 탈락 크레딧(킬)을 그 공격자에게 줍니다.")]
    [SerializeField] private float killCreditWindowSeconds = 5f;

    [Header("Sudden Death (정체 방지)")]
    [Tooltip("매치 시작 후 이 시간(초)이 지나면 서든데스 — 가장 바깥 발판부터 중앙 안전구역까지 빠르게 무너집니다.")]
    [SerializeField] private float suddenDeathStartSeconds = 60f;
    [Tooltip("서든데스 시작 후 가장 바깥 발판부터 안쪽 끝(안전구역 경계)까지 전부 무너지는 데 걸리는 총 시간(초).")]
    [SerializeField] private float suddenDeathCollapseSeconds = 12f;

    [Header("시상대 (승자 발표)")]
    [Tooltip("시상대 발판/장식 루트. 평소엔 꺼두고 매치 종료 시 켠다(비워도 동작).")]
    [SerializeField] private GameObject podiumRoot;
    [Tooltip("1·2·3등이 설 위치. [0]=가운데(1등), [1]=왼쪽(2등), [2]=오른쪽(3등).")]
    [SerializeField] private Transform[] podiumStands;
    [Tooltip("시상대 고정 카메라(있으면 종료 시 이 카메라로 전환). 비우면 기존 승자 추적 카메라 유지.")]
    [SerializeField] private GameObject podiumCamera;
    [Tooltip("게임플레이 카메라(FreeLook). 시상대 전환 시 끈다. 비우면 이름으로 자동 탐색.")]
    [SerializeField] private GameObject gameplayCamera;
    [Tooltip("화면 페이드용 검은 오버레이(CanvasGroup). 비우면 페이드 없이 즉시 전환.")]
    [SerializeField] private CanvasGroup fadeOverlay;
    [Tooltip("페이드 인/아웃 각각의 시간(초).")]
    [SerializeField] private float fadeDuration = 0.45f;
    [Tooltip("승자 확정 후 winner 패널을 보여주는 시간(초). 이 시간이 지나면 시상식으로 전환한다.")]
    [SerializeField] private float winnerPanelSeconds = 3f;

    // ───── 동기화 상태 (서버 쓰기, 전원 읽기) ─────
    private readonly NetworkVariable<byte> matchState = new NetworkVariable<byte>((byte)MatchState.Waiting);
    private readonly NetworkVariable<int> aliveCount = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> totalPlayerCount = new NetworkVariable<int>(0);
    private readonly NetworkVariable<double> countdownEndTime = new NetworkVariable<double>(0);
    // 데디케이티드 서버 대기실: 필요 인원(>0이면 대기 중)과 현재 접속 인원. HUD 표시용.
    private readonly NetworkVariable<int> waitingRequired = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> waitingConnected = new NetworkVariable<int>(0);
    private readonly NetworkVariable<ulong> winnerClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    // 서든데스가 시작되는 서버 시각(HUD 카운트다운용). 0이면 미정.
    private readonly NetworkVariable<double> suddenDeathStartTime = new NetworkVariable<double>(0);

    // ───── 서버 로컬 ─────
    private readonly HashSet<ulong> aliveClients = new HashSet<ulong>();
    private readonly List<ulong> eliminationOrder = new List<ulong>(); // 탈락 순서(빠른 순). 시상대 2·3등 산출용.
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

    // 시상식 진행 중 여부(로컬). HUD가 이 값을 보고 winner 패널을 끈다.
    private bool podiumActive;
    public bool PodiumActive => podiumActive;

    public MatchState State => (MatchState)matchState.Value;
    public int WaitingRequired => waitingRequired.Value;   // >0이면 인원 대기 중(데디케이티드)
    public int WaitingConnected => waitingConnected.Value;
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

        // 씬(모드)마다 넉백 세기를 전역에 반영. 직렬화 값이라 각 클라 동일 → 네트워킹 불필요.
        // 씬 언로드 후 다른 씬(로비 등)에 잔존하지 않도록 OnDestroy에서 1로 복귀.
        PlayerController.ArenaKnockbackMultiplier = Mathf.Max(0.1f, knockbackMultiplier);
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

        // 넉백 배율을 기본으로 복귀(로비/다른 모드에 잔존 방지).
        PlayerController.ArenaKnockbackMultiplier = 1f;

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
        // 데디케이티드 서버: 최소 인원이 접속할 때까지 매치를 시작하지 않는다(빈/1인 매치 방지).
        // 이 동안 아레나가 대기실 역할을 하고, HUD가 "플레이어 대기 중 n/m"을 표시한다.
        // 호스트/로컬 플레이는 이 블록을 건너뛰므로 기존 동작에 영향 없음.
        if (DedicatedServerBootstrap.IsDedicatedServer)
        {
            int need = Mathf.Max(1, minPlayersToStart);
            waitingRequired.Value = need;
            while (NetworkManager.ConnectedClientsIds.Count < need)
            {
                waitingConnected.Value = NetworkManager.ConnectedClientsIds.Count;
                yield return new WaitForSeconds(0.5f);
            }

            waitingConnected.Value = NetworkManager.ConnectedClientsIds.Count;
            waitingRequired.Value = 0; // 대기 종료 → HUD 표시 해제
        }

        // Waiting: NGO 씬 전환으로 유지 스폰되는 플레이어 오브젝트들이 전부 준비될 때까지 대기.
        float waitDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, maxWaitForSpawnSeconds);
        while (!AllPlayersSpawned() && Time.realtimeSinceStartup < waitDeadline)
        {
            yield return null;
        }

        yield return new WaitForSeconds(1f); // 스폰 배치/카메라 바인딩 정리 유예

        // Countdown: 생존자 스냅샷 확정.
        aliveClients.Clear();
        eliminationOrder.Clear();
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

    // 서든데스(서버): 붕괴 전선(반경)이 가장 바깥 발판에서 안쪽 끝까지 suddenDeathCollapseSeconds에 걸쳐
    // 이동하며, 전선이 지나간 발판을 전부 무너뜨린다. 발판 수와 무관하게 총 붕괴 시간이 일정하다.
    // (이전의 '한 장씩 간격' 방식은 발판이 수백 장인 맵에서 붕괴가 수 분씩 걸렸음)
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
        if (platforms.Length == 0)
        {
            yield break;
        }

        float[] distances = new float[platforms.Length];
        for (int i = 0; i < platforms.Length; i++)
        {
            distances[i] = platforms[i] != null
                ? Vector3.Distance(platforms[i].transform.position, transform.position)
                : 0f;
        }
        System.Array.Sort(distances, platforms);
        System.Array.Reverse(distances);
        System.Array.Reverse(platforms); // 바깥쪽(먼 것)부터

        float maxDistance = distances[0];
        float minDistance = distances[distances.Length - 1];
        float sweepRange = Mathf.Max(0.01f, maxDistance - minDistance);
        float duration = Mathf.Max(1f, suddenDeathCollapseSeconds);
        float sweepStartTime = Time.time;
        int nextIndex = 0;

        while (nextIndex < platforms.Length)
        {
            if (State != MatchState.Playing)
            {
                yield break;
            }

            float progress = Mathf.Clamp01((Time.time - sweepStartTime) / duration);
            float frontRadius = maxDistance - progress * sweepRange;

            while (nextIndex < platforms.Length && distances[nextIndex] >= frontRadius)
            {
                if (platforms[nextIndex] != null)
                {
                    platforms[nextIndex].ServerForceFall();
                }
                nextIndex++;
            }

            yield return null;
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

        // 같은 스윕에서 여러 명이 killY 아래면 전원을 먼저 탈락 확정한 뒤 승자를 판정한다.
        // (한 명씩 판정하면 첫 탈락 직후 "생존 1명"이 되어, 아직 처리 전인 — 이미 맵 밖인 —
        //  나머지 낙사자가 순회 순서에 따라 승자가 되는 오판이 있었음.)
        bool anyEliminated = false;
        for (int i = 0; i < aliveSnapshot.Length; i++)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(aliveSnapshot[i], out NetworkClient client)
                || client.PlayerObject == null)
            {
                continue;
            }

            if (client.PlayerObject.transform.position.y < killY)
            {
                EliminateOnServer(aliveSnapshot[i], checkWinner: false);
                anyEliminated = true;
            }
        }

        if (anyEliminated)
        {
            CheckWinnerOnServer();
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
    // checkWinner=false는 동시 탈락 일괄 처리용 — 호출자가 전원 제거 후 CheckWinnerOnServer를 직접 부른다.
    private void EliminateOnServer(ulong clientId, bool checkWinner = true)
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
        eliminationOrder.Add(clientId); // 시상대 순위 산출용(나중에 탈락할수록 높은 등수)
        aliveCount.Value = aliveClients.Count;
        EliminatePlayerClientRpc(clientId, killerClientId);
        if (checkWinner)
        {
            CheckWinnerOnServer();
        }
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

        // 시상대: 1등=승자, 2등=가장 늦게 탈락, 3등=그 다음. 없으면 ulong.MaxValue.
        ulong second = ulong.MaxValue, third = ulong.MaxValue;
        int found = 0;
        for (int i = eliminationOrder.Count - 1; i >= 0 && found < 2; i--)
        {
            if (eliminationOrder[i] == winner) continue;
            if (found == 0) second = eliminationOrder[i];
            else third = eliminationOrder[i];
            found++;
        }
        ShowPodiumClientRpc(winner, second, third);

        StartCoroutine(ReturnToLobbyAfterDelay());
    }

    [ClientRpc]
    private void AnnounceWinnerClientRpc(ulong winner, int totalWins)
    {
        // 호스트에서도 실행되지만 SetWins는 멱등이라 무해하다.
        SurvivalWinTracker.SetWins(winner, totalWins);
    }

    // 상위 3인을 시상대에 세운다(전 클라이언트). 각 캐릭터는 소유 클라에서 위치가 확정되고,
    // 렌더러(고스트 해제)는 모든 클라에서 켠다. 카메라는 기존대로 승자를 따라가므로 별도 전환 없음.
    [ClientRpc]
    private void ShowPodiumClientRpc(ulong first, ulong second, ulong third)
    {
        StartCoroutine(PodiumSequence(first, second, third));
    }

    // winner 패널 표시 → 페이드아웃(검게) → 시상대로 전환/배치(패널 끔) → 페이드인.
    private IEnumerator PodiumSequence(ulong first, ulong second, ulong third)
    {
        // 먼저 winner 패널을 잠시 보여준다(matchState=Finished라 HUD가 자동 표시).
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, winnerPanelSeconds));

        // 시상식 진입 — HUD가 winner 패널을 끄도록 플래그 설정.
        podiumActive = true;

        yield return FadeOverlayTo(1f); // 검게

        if (podiumRoot != null)
        {
            podiumRoot.SetActive(true);
        }

        SwitchToPodiumCamera();

        PlacePodiumWinner(first, 0, celebrate: true);
        PlacePodiumWinner(second, 1, celebrate: false);
        PlacePodiumWinner(third, 2, celebrate: false);

        // 배치/카메라 블렌드가 자리잡을 짧은 여유(검은 화면 동안).
        yield return new WaitForSecondsRealtime(0.15f);

        yield return FadeOverlayTo(0f); // 밝게
    }

    private void SwitchToPodiumCamera()
    {
        if (podiumCamera == null)
        {
            return; // 전용 카메라 없으면 기존 승자 추적 카메라 유지
        }

        // 관전 카메라가 FreeLook을 계속 재조준하지 않도록 멈춘다.
        if (spectator != null)
        {
            spectator.enabled = false;
        }

        // 브레인 기본 블렌드가 EaseInOut 2초라 전환이 눈에 보인다 → 컷(즉시)으로 바꿔
        // 검은 화면 뒤에서 순간 이동하게 한다. 매치 후 로비 재로드로 초기화되므로 복원 불필요.
        CinemachineBrain brain = FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
        {
            brain.DefaultBlend.Style = CinemachineBlendDefinition.Styles.Cut;
        }

        if (gameplayCamera == null)
        {
            gameplayCamera = GameObject.Find("FreeLook Camera");
        }
        if (gameplayCamera != null)
        {
            gameplayCamera.SetActive(false);
        }

        podiumCamera.SetActive(true);
    }

    private IEnumerator FadeOverlayTo(float targetAlpha)
    {
        if (fadeOverlay == null)
        {
            yield break;
        }

        fadeOverlay.gameObject.SetActive(true);
        fadeOverlay.blocksRaycasts = targetAlpha > 0.5f;

        float start = fadeOverlay.alpha;
        float dur = Mathf.Max(0.05f, fadeDuration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            fadeOverlay.alpha = Mathf.Lerp(start, targetAlpha, t / dur);
            yield return null;
        }
        fadeOverlay.alpha = targetAlpha;
    }

    private void PlacePodiumWinner(ulong clientId, int standIndex, bool celebrate)
    {
        if (clientId == ulong.MaxValue || podiumStands == null
            || standIndex >= podiumStands.Length || podiumStands[standIndex] == null)
        {
            return;
        }

        NetworkOwnedObjectActivator activator = FindPlayerActivator(clientId);
        if (activator == null)
        {
            return;
        }

        // 고스트 해제: 탈락자(2·3등)의 렌더러를 다시 켠다(모든 클라).
        Renderer[] renderers = activator.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = true;
        }

        // 머리 위 닉네임 표시(모든 클라에서 — 각자 로컬 카메라를 향해 빌보드됨).
        activator.SetPodiumNameLabel(true);

        // 위치/물리 고정은 소유 클라에서만(그래야 syncedPosition으로 전 클라에 전파).
        if (NetworkManager != null && clientId == NetworkManager.LocalClientId)
        {
            Transform stand = podiumStands[standIndex];
            activator.PlaceForPodium(stand.position, stand.rotation);

            // 시상대 고정으로 kinematic이 된 로컬 몸은 씬 언로드(OnDestroy) 때 반드시 해제한다.
            // (해제 없이 로비로 가면 몸이 얼어붙어 입력이 안 먹는 버그가 있었음)
            if (localPlayerBody == null)
            {
                localPlayerBody = activator.GetComponent<Rigidbody>();
            }
            localBodyMadeKinematic = true;

            PlayerController pc = activator.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.SetGameplayInputEnabled(false);
                pc.SetCelebrating(celebrate); // 1등만 Spin, 2·3등은 idle
            }
        }
    }

    private IEnumerator ReturnToLobbyAfterDelay()
    {
        // winner 패널(winnerPanelSeconds) + 시상식(returnToLobbyDelaySeconds)만큼 머문 뒤 복귀.
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, winnerPanelSeconds) + Mathf.Max(1f, returnToLobbyDelaySeconds));

        // 로비 로드 직전 전 클라이언트를 검게 페이드아웃(씬 전환을 가린다).
        FadeOutBeforeLobbyClientRpc();
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, fadeDuration) + 0.1f);

        // 데디케이티드 서버: 로비로 나가지 않고 같은 아레나를 다시 로드해 다음 라운드를 돈다.
        if (DedicatedServerBootstrap.IsDedicatedServer && NetworkManager != null && NetworkManager.SceneManager != null)
        {
            string arena = string.IsNullOrWhiteSpace(DedicatedServerBootstrap.ServerGameScene)
                ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                : DedicatedServerBootstrap.ServerGameScene;
            NetworkManager.SceneManager.LoadScene(arena, UnityEngine.SceneManagement.LoadSceneMode.Single);
            yield break;
        }

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

    [ClientRpc]
    private void FadeOutBeforeLobbyClientRpc()
    {
        StartCoroutine(FadeOverlayTo(1f));
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

        // 탈락 연출(비명 + UI 팝).
        GameFeedback.Elimination();

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
