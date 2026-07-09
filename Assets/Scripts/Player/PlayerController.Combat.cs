using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 플레이어 간 물리 상호작용(머리 밟기 스톰프 + 돌진/밀치기)의 공통 부품.
//
// 네트워킹 골든룰: 플레이어는 Owner 권한(원격 사본은 kinematic)이므로 "A가 B에게 효과"를 A 머신에서
// 직접 B에 줄 수 없다. 공격자는 자기 효과(바운스/돌진 가속)만 로컬 적용하고, 피해자 효과는
// 서버를 거쳐 "피해자 소유자"에게 적용한다.
//   • 스턴(찌부): 서버가 피해자의 isSquashed NetworkVariable을 켠다 → 모든 클라 찌부 비주얼 + 소유자 입력잠금.
//   • 임펄스/넉다운: 서버가 피해자 소유자에게만 ClientRpc를 보내 로컬에서 ApplyExternalImpulse/StartKnockdown.
public partial class PlayerController
{
    private const byte CombatEffectImpulseOnly = 0;
    private const byte CombatEffectStun = 1;       // 찌부(머리 밟기)
    private const byte CombatEffectKnockdown = 2;  // 넘어짐(돌진/밀치기)

    [Header("Stomp (머리 밟기)")]
    [Tooltip("이 속도 이상으로 하강 중일 때만 머리 밟기로 인정합니다(m/s).")]
    [SerializeField] private float stompMinDownSpeed = 3f;
    [Tooltip("머리를 밟았을 때 밟은 쪽이 튀어오르는 높이(m). 일반 점프보다 크게 두면 됩니다.")]
    [SerializeField] private float stompBounceHeight = 2.2f;
    [Tooltip("접촉점이 피해자 기준 이 높이 이상이어야 '머리'로 인정합니다(로컬 y 오프셋). 동물 캐릭터 키(~0.9m) 기준.")]
    [SerializeField] private float stompHeadHeight = 0.4f;
    [Tooltip("밟힌 쪽에 추가로 눌러주는 아래 방향 임펄스 세기.")]
    [SerializeField] private float stompDownImpulse = 2f;
    [Tooltip("밟힌 쪽이 찌부/스턴되는 시간(초).")]
    [SerializeField] private float stompStunDuration = 1.2f;
    [Tooltip("연속 밟기 방지 쿨다운(초).")]
    [SerializeField] private float stompCooldown = 0.3f;

    [Header("Squash Visual (찌부 연출)")]
    [Tooltip("찌부 시 캐릭터 세로(Y) 스케일 배율.")]
    [SerializeField] private float squashScaleY = 0.45f;
    [Tooltip("찌부 시 캐릭터 가로(XZ) 스케일 배율.")]
    [SerializeField] private float squashScaleXZ = 1.35f;
    [Tooltip("찌부 스케일 보간 속도.")]
    [SerializeField] private float squashLerpSpeed = 12f;

    [Header("Dash / Shove (돌진·밀치기)")]
    [Tooltip("돌진 시 전방으로 가하는 임펄스 세기.")]
    [SerializeField] private float dashForce = 9f;
    [Tooltip("돌진 재사용 대기시간(초).")]
    [SerializeField] private float dashCooldown = 0.8f;
    [Tooltip("돌진 판정이 유효한 시간(초). 이 창 안에 상대와 부딪히면 밀칩니다.")]
    [SerializeField] private float dashWindow = 0.25f;
    [Tooltip("돌진 중 상대와 충돌 시 상대에게 가하는 넉백 세기.")]
    [SerializeField] private float dashShoveStrength = 12f;
    [Tooltip("돌진 시 구르기(Roll) 애니메이션을 재생하는 시간(초).")]
    [SerializeField] private float dashRollDuration = 0.5f;

    private float lastStompTime = -999f;
    private bool dashQueued;
    private float lastDashTime = -999f;
    private float dashActiveUntil = -999f;
    private float dashRollAnimUntil = -999f; // 이 시각까지 Roll 애니메이션 재생
    private InputAction dashAction;

    // 서버가 켜고 끄는 찌부 상태(모든 클라가 읽어 비주얼 표시, 소유자는 입력잠금에 사용).
    private readonly NetworkVariable<bool> isSquashed =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Coroutine squashResetRoutine;
    private float localSquashUntil = -999f; // 오프라인(비네트워크) 폴백용

    private PlayerCharacterView characterView;
    private NetworkOwnedObjectActivator squashLobbyActivator;
    private Transform squashVisualRoot;
    private Vector3 squashVisualBaseScale = Vector3.one;
    private bool squashBaseScaleCaptured;

    // 피해자 중심 좌표 비교/충격점 계산용.
    public Vector3 BodyCenter => physicsBody != null ? physicsBody.worldCenterOfMass : transform.position;

    // ───── 킬 크레딧용 최근 공격자 기록(서버 전용 상태) ─────
    private ulong serverLastAttackerClientId = ulong.MaxValue;
    private float serverLastAttackTime = -999f;

    // 서버가 피해 판정을 확정할 때 호출해 "마지막으로 나를 때린 사람"을 기록한다.
    public void ServerRegisterAttacker(ulong attackerClientId)
    {
        if (!IsServer || attackerClientId == OwnerClientId || attackerClientId == ulong.MaxValue)
        {
            return;
        }

        serverLastAttackerClientId = attackerClientId;
        serverLastAttackTime = Time.time;
    }

    // 최근 window초 안에 맞은 기록이 있으면 그 공격자를 반환(탈락 크레딧 판정, 서버 전용).
    public bool TryGetRecentAttacker(float window, out ulong attackerClientId)
    {
        attackerClientId = serverLastAttackerClientId;
        return IsServer
            && serverLastAttackerClientId != ulong.MaxValue
            && Time.time - serverLastAttackTime <= window;
    }

    // 현재 찌부(스턴) 상태인지. 네트워크면 동기화 변수, 오프라인이면 로컬 타이머.
    public bool IsStunned =>
        (networkObject != null && networkObject.IsSpawned) ? isSquashed.Value : Time.time < localSquashUntil;

    public override void OnNetworkSpawn()
    {
        isSquashed.OnValueChanged += HandleSquashChanged;
        // 기준 스케일은 로비 스폰 스케일-인이 끝난 뒤(게임 씬에서) UpdateSquashVisual이 지연 캡처한다.
        // 스폰 시점(로비)에서 캡처하면 스케일-인 도중의 축소값을 잘못 잡을 수 있어 여기서는 캡처하지 않는다.
    }

    public override void OnNetworkDespawn()
    {
        isSquashed.OnValueChanged -= HandleSquashChanged;
        if (squashResetRoutine != null)
        {
            StopCoroutine(squashResetRoutine);
            squashResetRoutine = null;
        }

        // 세션 종료/소멸 시 잡기 상태 정리(자기가 붙잡고 있으면 놓고, 붙잡혀 있으면 해제).
        ServerGrappleCleanup();
    }

    private void HandleSquashChanged(bool previous, bool current)
    {
        // 비주얼은 UpdateSquashVisual에서 매 프레임 보간하므로 여기서는 특별한 처리 불필요.
        // (필요 시 사운드/파티클 훅을 여기에 추가)
    }

    // 모든 인스턴스(소유자/원격)에서 매 프레임 호출. 찌부 스케일을 부드럽게 적용/복구한다.
    private void UpdateSquashVisual()
    {
        if (characterView == null)
        {
            characterView = GetComponent<PlayerCharacterView>();
        }
        if (characterView == null)
        {
            return;
        }

        // 로비에서는 스폰 스케일-인 연출이 캐릭터 스케일을 소유한다. 찌부(게임플레이 전용)는 관여하지 않는다.
        // (둘이 같은 localScale을 동시에 만지면 캐릭터가 축소된 채 고정되는 문제가 있었음)
        if (squashLobbyActivator == null)
        {
            squashLobbyActivator = GetComponent<NetworkOwnedObjectActivator>();
        }
        if (squashLobbyActivator != null && squashLobbyActivator.IsInLobbyScene())
        {
            // 게임 씬에 진입하면 그때의 올바른 스케일로 다시 캡처하도록 무효화.
            squashBaseScaleCaptured = false;
            squashVisualRoot = null;
            return;
        }

        Transform root = characterView.GetActiveCharacterRoot();
        if (root == null)
        {
            return;
        }

        bool stunned = IsStunned;

        // 캐릭터 루트가 바뀌었으면(모델 전환) 스턴이 아닐 때만 새 기준 스케일을 잡는다.
        if (root != squashVisualRoot || !squashBaseScaleCaptured)
        {
            if (!stunned)
            {
                squashVisualRoot = root;
                squashVisualBaseScale = root.localScale;
                squashBaseScaleCaptured = true;
            }
            else if (!squashBaseScaleCaptured)
            {
                // 스턴 중 처음 본 루트: 일단 현재 값을 기준으로(드문 엣지 케이스).
                squashVisualRoot = root;
                squashVisualBaseScale = root.localScale;
                squashBaseScaleCaptured = true;
            }
        }

        Vector3 target = stunned
            ? new Vector3(
                squashVisualBaseScale.x * squashScaleXZ,
                squashVisualBaseScale.y * squashScaleY,
                squashVisualBaseScale.z * squashScaleXZ)
            : squashVisualBaseScale;

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, squashLerpSpeed) * Time.deltaTime);
        root.localScale = Vector3.Lerp(root.localScale, target, t);
    }

    private void ResolveDashAction()
    {
        if (playerInput == null || playerInput.actions == null)
        {
            return;
        }

        // 액션이 없으면 null → 돌진 비활성(에셋에 Dash 미추가 시 안전).
        dashAction = playerInput.actions.FindAction("Dash", false);
    }

    private void ApplyDash()
    {
        if (!dashQueued)
        {
            return;
        }

        dashQueued = false;

        if (physicsBody == null || Time.time - lastDashTime <= dashCooldown)
        {
            return;
        }

        lastDashTime = Time.time;
        dashActiveUntil = Time.time + dashWindow;
        dashRollAnimUntil = Time.time + Mathf.Max(0.1f, dashRollDuration);

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }
        flatForward.Normalize();

        physicsBody.AddForce(flatForward * dashForce, ForceMode.Impulse);
    }

    // ─────────────────────────────────────────────────────────────
    // 스톰프/밀치기 판정에서 호출하는 피해자 효과 디스패치
    // ─────────────────────────────────────────────────────────────
    private void SendCombatHit(PlayerController victim, Vector3 force, Vector3 point, byte effect, float stunDuration)
    {
        if (victim == null)
        {
            return;
        }

        bool networked = networkObject != null && networkObject.IsSpawned;
        if (!networked)
        {
            // 오프라인/단독 테스트: 피해자에 직접 적용.
            if (victim.HasInputAuthority) HitVignette.Show(); // 로컬 플레이어가 피해자일 때만 화면 연출
            if (effect == CombatEffectStun) victim.BeginSquashLocal(stunDuration);
            victim.ApplyExternalImpulse(force, point);
            if (effect == CombatEffectKnockdown) victim.StartKnockdown();
            return;
        }

        if (IsServer)
        {
            ServerApplyCombatHit(victim.NetworkObjectId, force, point, effect, stunDuration);
        }
        else
        {
            RequestCombatHitServerRpc(victim.NetworkObjectId, force, point, effect, stunDuration);
        }
    }

    [ServerRpc]
    private void RequestCombatHitServerRpc(ulong victimNetworkObjectId, Vector3 force, Vector3 point, byte effect, float stunDuration)
    {
        ServerApplyCombatHit(victimNetworkObjectId, force, point, effect, stunDuration);
    }

    private void ServerApplyCombatHit(ulong victimNetworkObjectId, Vector3 force, Vector3 point, byte effect, float stunDuration)
    {
        if (!IsServer)
        {
            return;
        }

        PlayerController victim = ResolvePlayer(victimNetworkObjectId);
        if (victim == null || victim == this)
        {
            return;
        }

        if (effect == CombatEffectStun)
        {
            victim.ServerBeginSquash(stunDuration);
        }

        // 킬 크레딧: 이 히트의 공격자(이 컴포넌트의 소유자)를 기록.
        victim.ServerRegisterAttacker(OwnerClientId);

        // 임펄스/넉다운은 피해자 소유자만 로컬로 적용(그 머신에서만 dynamic).
        ClientRpcParams target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { victim.OwnerClientId }
            }
        };
        victim.ApplyCombatHitOwnerClientRpc(force, point, effect, target);
    }

    [ClientRpc]
    private void ApplyCombatHitOwnerClientRpc(Vector3 force, Vector3 point, byte effect, ClientRpcParams rpcParams = default)
    {
        // 타게팅되어 피해자 소유자에서만 실행됨.
        // 스톰프·대시 밀치기·던진 물체 피격이 전부 이 경로로 들어오므로 피격 화면 연출도 여기서 처리.
        HitVignette.Show();

        ApplyExternalImpulse(force, point);
        if (effect == CombatEffectKnockdown)
        {
            StartKnockdown();
        }
    }

    private void ServerBeginSquash(float duration)
    {
        if (!IsServer)
        {
            return;
        }

        isSquashed.Value = true;
        if (squashResetRoutine != null)
        {
            StopCoroutine(squashResetRoutine);
        }
        squashResetRoutine = StartCoroutine(SquashResetRoutine(duration));
    }

    private IEnumerator SquashResetRoutine(float duration)
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, duration));
        isSquashed.Value = false;
        squashResetRoutine = null;
    }

    private void BeginSquashLocal(float duration)
    {
        localSquashUntil = Time.time + Mathf.Max(0.05f, duration);
    }

    private PlayerController ResolvePlayer(ulong networkObjectId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null)
        {
            return null;
        }

        if (nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj) && netObj != null)
        {
            return netObj.GetComponent<PlayerController>();
        }

        return null;
    }
}
