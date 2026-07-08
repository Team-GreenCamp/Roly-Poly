using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 플레이어 잡아 던지기(Gang Beasts식). 서바이벌에서 상대를 붙잡아 링 밖으로 던지는 근접 상호작용.
//
// 입력(입력 에셋 수정 없이 기존 액션 재사용, 손이 비었을 때만 → 물체 상호작용과 충돌 안 함)
//   • 잡기  : Grab(우클릭) — 앞의 플레이어를 붙잡는다.
//   • 던지기: Throw(좌클릭) 또는 Grab 다시 — 붙잡은 상대를 바라보는 방향으로 던진다.
//
// 네트워킹(기존 골든룰 재사용)
//   • 상태는 서버 권한 NetworkVariable: 피해자의 grabbedByClientId, 공격자의 grabbingVictimNetId.
//   • 붙잡힌 피해자는 '자기 소유자'가 공격자 앞으로 자기 몸을 이동(owner 권한 → NOA가 동기화).
//   • 던지기 임펄스/넉다운은 전투와 동일하게 서버 → 피해자 소유자 ClientRpc(ApplyCombatHitOwnerClientRpc).
public partial class PlayerController
{
    private const ulong NoGrabber = ulong.MaxValue;

    [Header("Grapple (플레이어 잡아 던지기)")]
    [Tooltip("이 거리 안의 앞쪽 플레이어를 붙잡을 수 있습니다(m).")]
    [SerializeField] private float grappleRange = 2.3f;
    [SerializeField] private float grappleRadius = 0.8f;
    [Tooltip("붙잡은 뒤 자동으로 놓기까지의 시간(초).")]
    [SerializeField] private float grappleHoldDuration = 2.5f;
    [Tooltip("잡기 재사용 대기시간(초).")]
    [SerializeField] private float grappleCooldown = 1.5f;
    [Tooltip("입(주둥이) 기준점을 직접 지정할 때 사용(선택). 비우면 활성 캐릭터 모델 크기에서 자동 산출합니다.")]
    [SerializeField] private Transform grappleMouthAnchor;
    [Tooltip("입 기준점에서 앞으로 이만큼 떨어진 곳에 상대를 뭅니다(m).")]
    [SerializeField] private float grappleHoldDistance = 0.15f;
    [Tooltip("입 기준점 대비 상대 루트의 높이 보정(m). 음수면 몸통이 입에 걸리도록 내려갑니다.")]
    [SerializeField] private float grappleHoldHeight = -0.25f;
    [Tooltip("물린 상대가 축 처지듯 기울어지는 각도(도). 0이면 수평 유지.")]
    [SerializeField] private float grappleHoldTiltDegrees = 20f;
    [Tooltip("붙잡은 상대를 던지는 전방 세기.")]
    [SerializeField] private float grappleThrowForce = 13f;
    [Tooltip("던질 때 위로 띄우는 세기.")]
    [SerializeField] private float grappleThrowUp = 8f;
    [Tooltip("붙잡고 있는 동안 공격자의 이동 속도 배율(느려짐).")]
    [SerializeField] private float grappleCarryMultiplier = 0.5f;
    [Tooltip("잡기/던지기 순간 Attack 애니메이션을 재생하는 시간(초).")]
    [SerializeField] private float grappleAttackAnimDuration = 0.35f;
    [SerializeField] private LayerMask grapplePlayerMask = ~0;

    [Header("Grapple Outline (잡기 대상 표시)")]
    [Tooltip("잡을 수 있는 상대에게 표시할 아웃라인 색.")]
    [SerializeField] private Color grappleTargetOutlineColor = new Color(1f, 0.35f, 0.3f, 1f);
    [Tooltip("붙잡고 있는 상대에게 표시할 아웃라인 색.")]
    [SerializeField] private Color grappleHeldOutlineColor = new Color(1f, 0.15f, 0.1f, 1f);
    [SerializeField] private float grappleOutlineWidth = 5f;

    // 이 플레이어를 붙잡고 있는 클라이언트(피해자 상태). 센티널=안 붙잡힘.
    private readonly NetworkVariable<ulong> grabbedByClientId =
        new NetworkVariable<ulong>(NoGrabber, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 이 플레이어가 붙잡고 있는 상대의 NetworkObjectId(공격자 상태). 센티널=안 붙잡음.
    private readonly NetworkVariable<ulong> grabbingVictimNetId =
        new NetworkVariable<ulong>(NoGrabber, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private InputAction grappleAction;      // Grab (RMB)
    private InputAction grappleThrowAction;  // Throw (LMB)
    private PlayerInteractor grappleInteractor;
    private Coroutine grappleReleaseRoutine;
    private float grappleReadyTime = -999f;
    private bool grappleCarryApplied;
    private InteractableOutlineHighlight grappleOutline; // 잡기 대상/붙잡은 상대 표시
    private float grappleAttackAnimUntil = -999f;        // 이 시각까지 Attack 애니메이션 재생
    // 던지기 ClientRpc가 grabbedByClientId 해제(NetworkVariable)보다 먼저 도착하면
    // UpdateGrabbedVictimMotion이 임펄스를 0으로 덮어써 던지기가 씹힌다. 이 시각까지는 로컬에서 붙잡힘을 무시한다.
    private float grappleThrowOverrideUntil = -999f;

    // 붙잡힌 상태(피해자). 이동/조작 불가 + 소유자가 공격자 앞으로 따라간다.
    public bool IsGrabbed =>
        networkObject != null && networkObject.IsSpawned
        && grabbedByClientId.Value != NoGrabber
        && Time.time >= grappleThrowOverrideUntil;
    // 누군가를 붙잡고 있는 상태(공격자).
    public bool IsGrabbingSomeone =>
        networkObject != null && networkObject.IsSpawned && grabbingVictimNetId.Value != NoGrabber;

    private void ResolveGrappleActions()
    {
        if (playerInput == null || playerInput.actions == null)
        {
            return;
        }

        grappleAction = playerInput.actions.FindAction("Grab", false);
        grappleThrowAction = playerInput.actions.FindAction("Throw", false);
    }

    // 소유자 입력 처리(Update의 입력 단계에서 호출). 붙잡힌 피해자는 여기까지 오지 않는다.
    private void UpdateGrappleInput()
    {
        if (grappleAction == null && grappleThrowAction == null)
        {
            ResolveGrappleActions();
        }

        bool grabbing = IsGrabbingSomeone;

        // 붙잡는 동안 이동 속도 감소(옮겨 나르는 느낌). 손에 물건을 든 경우는 PlayerInteractor가 관리하므로 여기선 관여 안 함.
        if (grabbing && !grappleCarryApplied)
        {
            SetCarrySpeedMultiplier(grappleCarryMultiplier);
            grappleCarryApplied = true;
        }
        else if (!grabbing && grappleCarryApplied)
        {
            ResetCarrySpeedMultiplier();
            grappleCarryApplied = false;
        }

        if (grabbing)
        {
            // 붙잡은 상대는 진한 색으로 강조.
            SetGrappleOutline(ResolvePlayer(grabbingVictimNetId.Value), grappleHeldOutlineColor);

            bool throwPressed =
                (grappleThrowAction != null && grappleThrowAction.WasPressedThisFrame()) ||
                (grappleAction != null && grappleAction.WasPressedThisFrame());

            if (throwPressed)
            {
                grappleAttackAnimUntil = Time.time + Mathf.Max(0.1f, grappleAttackAnimDuration);
                RequestGrappleThrow();
            }
            return;
        }

        // 손이 비어 있으면 잡을 수 있는 상대를 매 프레임 탐색해 아웃라인으로 알려준다.
        bool canTryGrab = Time.time >= grappleReadyTime && !IsHoldingObject();
        PlayerController candidate = canTryGrab ? FindGrappleTarget() : null;
        SetGrappleOutline(candidate, grappleTargetOutlineColor);

        // 붙잡기 시도: Grab을 눌렀을 때.
        if (candidate == null || grappleAction == null || !grappleAction.WasPressedThisFrame())
        {
            return;
        }

        grappleReadyTime = Time.time + grappleCooldown;
        grappleAttackAnimUntil = Time.time + Mathf.Max(0.1f, grappleAttackAnimDuration);
        RequestGrab(candidate.NetworkObjectId);
    }

    // 잡기 대상 아웃라인 관리(소유자 로컬 연출). 대상이 바뀌거나 없어지면 이전 하이라이트를 정리한다.
    private void SetGrappleOutline(PlayerController target, Color color)
    {
        InteractableOutlineHighlight next = null;

        if (target != null)
        {
            // 이름표(TMP) 등이 물들지 않게 플레이어 루트가 아닌 활성 캐릭터 모델에만 아웃라인을 건다.
            PlayerCharacterView view = target.GetComponent<PlayerCharacterView>();
            Transform visualRoot = view != null ? view.GetActiveCharacterRoot() : null;
            GameObject outlineTarget = visualRoot != null ? visualRoot.gameObject : target.gameObject;

            next = outlineTarget.GetComponent<InteractableOutlineHighlight>();
            if (next == null)
            {
                next = outlineTarget.AddComponent<InteractableOutlineHighlight>();
            }
            next.Configure(color, grappleOutlineWidth, Outline.Mode.OutlineVisible);
        }

        if (grappleOutline == next)
        {
            return;
        }

        if (grappleOutline != null)
        {
            grappleOutline.SetHighlighted(false);
        }

        grappleOutline = next;
        if (grappleOutline != null)
        {
            grappleOutline.SetHighlighted(true);
        }
    }

    private void ClearGrappleOutline()
    {
        SetGrappleOutline(null, Color.white);
    }

    private bool IsHoldingObject()
    {
        if (grappleInteractor == null)
        {
            grappleInteractor = GetComponent<PlayerInteractor>();
        }
        return grappleInteractor != null && grappleInteractor.CurrentHeldGrabbable != null;
    }

    private PlayerController FindGrappleTarget()
    {
        Vector3 probe = BodyCenter + transform.forward * (grappleRange * 0.6f);
        Collider[] hits = Physics.OverlapSphere(probe, grappleRadius, grapplePlayerMask, QueryTriggerInteraction.Ignore);

        PlayerController best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerController candidate = hits[i] != null ? hits[i].GetComponentInParent<PlayerController>() : null;
            if (candidate == null || candidate == this)
            {
                continue;
            }
            if (candidate.IsGrabbed || candidate.IsGrabbingSomeone)
            {
                continue;
            }

            float sqr = (candidate.BodyCenter - BodyCenter).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    private void RequestGrab(ulong victimNetId)
    {
        if (IsServer)
        {
            ServerBeginGrab(OwnerClientId, victimNetId);
        }
        else
        {
            RequestGrabServerRpc(victimNetId);
        }
    }

    private void RequestGrappleThrow()
    {
        if (IsServer)
        {
            ServerReleaseGrab(true);
        }
        else
        {
            RequestGrappleThrowServerRpc();
        }
    }

    [ServerRpc]
    private void RequestGrabServerRpc(ulong victimNetId)
    {
        ServerBeginGrab(OwnerClientId, victimNetId);
    }

    [ServerRpc]
    private void RequestGrappleThrowServerRpc()
    {
        ServerReleaseGrab(true);
    }

    private void ServerBeginGrab(ulong grabberClientId, ulong victimNetId)
    {
        if (!IsServer || grabbingVictimNetId.Value != NoGrabber)
        {
            return;
        }

        PlayerController victim = ResolvePlayer(victimNetId);
        if (victim == null || victim == this || victim.grabbedByClientId.Value != NoGrabber)
        {
            return;
        }

        float maxDistance = grappleRange * 1.5f;
        if ((victim.BodyCenter - BodyCenter).sqrMagnitude > maxDistance * maxDistance)
        {
            return;
        }

        victim.grabbedByClientId.Value = grabberClientId;
        grabbingVictimNetId.Value = victimNetId;

        if (grappleReleaseRoutine != null)
        {
            StopCoroutine(grappleReleaseRoutine);
        }
        grappleReleaseRoutine = StartCoroutine(GrappleAutoReleaseRoutine());
    }

    private IEnumerator GrappleAutoReleaseRoutine()
    {
        yield return new WaitForSeconds(Mathf.Max(0.3f, grappleHoldDuration));
        grappleReleaseRoutine = null;
        ServerReleaseGrab(false);
    }

    // 붙잡기 해제(서버). throwVictim=true면 바라보는 방향으로 던진다.
    private void ServerReleaseGrab(bool throwVictim)
    {
        if (!IsServer)
        {
            return;
        }

        ulong victimNetId = grabbingVictimNetId.Value;
        if (victimNetId == NoGrabber)
        {
            return;
        }

        grabbingVictimNetId.Value = NoGrabber;
        if (grappleReleaseRoutine != null)
        {
            StopCoroutine(grappleReleaseRoutine);
            grappleReleaseRoutine = null;
        }

        PlayerController victim = ResolvePlayer(victimNetId);
        if (victim == null)
        {
            return;
        }

        if (victim.grabbedByClientId.Value != NoGrabber)
        {
            victim.grabbedByClientId.Value = NoGrabber;
        }

        if (!throwVictim)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = transform.forward;
        }
        forward.Normalize();

        Vector3 launch = forward * grappleThrowForce + Vector3.up * grappleThrowUp;

        // 던지기는 전용 ClientRpc를 쓴다. 일반 전투 릴레이를 쓰면 grabbedByClientId 해제(NetworkVariable)가
        // 임펄스보다 늦게 도착했을 때 UpdateGrabbedVictimMotion이 속도를 0으로 덮어 던지기가 씹힌다.
        ClientRpcParams target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { victim.OwnerClientId }
            }
        };
        victim.ApplyGrappleThrowOwnerClientRpc(launch, target);
    }

    [ClientRpc]
    private void ApplyGrappleThrowOwnerClientRpc(Vector3 launch, ClientRpcParams rpcParams = default)
    {
        // 타게팅되어 붙잡혔던 피해자 소유자에서만 실행됨.
        // grabbedByClientId 해제가 아직 안 왔어도 이 시간 동안은 붙잡힘 상태를 무시해 임펄스가 살아남게 한다.
        grappleThrowOverrideUntil = Time.time + 0.6f;

        // 충격점을 몸 중심보다 위로 잡아 던져지며 공중제비 도는 회전(토크)을 만든다 — 던지는 손맛의 핵심.
        Vector3 hitPoint = BodyCenter + Vector3.up * 0.45f;
        ApplyExternalImpulse(launch, hitPoint);
        StartKnockdown();
    }

    // ─────────────────────────────────────────────────────────────
    // 붙잡힘 추종: 모든 머신이 각자 로컬에서 피해자를 공격자에 부착한다.
    //  • 공격자 화면: 자기 transform 기준이라 지연 0 (이전 방식은 피해자 소유자 계산 → 재동기화라 2중 지연).
    //  • 원격/피해자 화면: 각자 보이는 공격자 위치 기준이라 항상 손에 붙어 보인다.
    // NOA의 원격 보간은 잡힘 동안 건너뛰게 처리(NetworkOwnedObjectActivator.UpdateTransformSync).
    // ─────────────────────────────────────────────────────────────
    private PlayerController grabberCache;
    private ulong grabberCacheClientId = NoGrabber;
    private PlayerController grabCollisionIgnoredWith;
    // 물린 동안 꺼둔 피해자 콜라이더들(캡슐 + 자식 전부). 복원용.
    private readonly System.Collections.Generic.List<Collider> grabDisabledColliders =
        new System.Collections.Generic.List<Collider>();
    private float grabPairRestoreAt = -1f;  // 놓은 뒤 공격자와의 충돌 무시 복원 후보 시각

    // PlayerController.Update에서 모든 인스턴스가 매 프레임 호출.
    private void UpdateGrabbedFollow()
    {
        if (!IsGrabbed)
        {
            HandleGrabReleasedLocal();
            return;
        }

        PlayerController grabber = ResolveGrabberByClientId(grabbedByClientId.Value);
        if (grabber == null)
        {
            return;
        }

        // 물린 동안: 공격자와의 충돌 무시 + 피해자 콜라이더 전체 비활성(캡슐+자식 전부).
        // (콜라이더가 켜져 있으면 나르는 동안 벽/배럴/다른 플레이어를 밀치며 떨린다)
        ApplyGrabCollisionIgnore(grabber);
        if (grabDisabledColliders.Count == 0)
        {
            Collider[] all = GetComponentsInChildren<Collider>(false);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].enabled && !all[i].isTrigger)
                {
                    all[i].enabled = false;
                    grabDisabledColliders.Add(all[i]);
                }
            }
        }
        grabPairRestoreAt = -1f;

        if (CanProcessInput())
        {
            return; // 소유자(피해자 본인)는 FixedUpdate에서 물리 바디로 처리
        }

        // 원격 사본(공격자/관전자 화면): kinematic이므로 transform을 직접 부착 — 즉시 따라붙는다.
        ComputeGrabPose(grabber, out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
    }

    // 붙잡힌 피해자(소유자)를 공격자 몸 앞으로 이동시킨다. FixedUpdate에서 호출.
    private void UpdateGrabbedVictimMotion()
    {
        if (physicsBody == null)
        {
            return;
        }

        PlayerController grabber = ResolveGrabberByClientId(grabbedByClientId.Value);
        if (grabber == null)
        {
            // 공격자가 사라졌으면 서버가 곧 해제한다. 그동안 제자리 정지.
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
            return;
        }

        ComputeGrabPose(grabber, out Vector3 pos, out Quaternion rot);
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.position = pos;
        physicsBody.rotation = rot;
    }

    private void ComputeGrabPose(PlayerController grabber, out Vector3 position, out Quaternion rotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(grabber.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = grabber.transform.forward;
        }
        forward.Normalize();

        // 캐릭터(동물)마다 입 높이/주둥이 길이가 다르므로 공격자의 입 기준점에서 위치를 잡는다.
        Vector3 mouth = grabber.GetGrappleMouthPoint(forward);
        position = mouth + forward * grappleHoldDistance + Vector3.up * grappleHoldHeight;

        // '입으로 가로로 물린' 자세: 피해자 몸이 공격자 시선과 직각(가로)으로 눕고,
        // 물린 축(공격자 전방)을 기준으로 살짝 기울어져 축 처진 느낌을 준다.
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        rotation = Quaternion.AngleAxis(grappleHoldTiltDegrees, forward)
                 * Quaternion.LookRotation(side, Vector3.up);
    }

    // 이 플레이어의 '입(주둥이)' 기준 월드 좌표. 수동 앵커가 있으면 그것을,
    // 없으면 활성 캐릭터 모델의 로컬 바운즈에서 앞·위(주둥이) 지점을 자동 산출한다(모델 전환 시 자동 갱신).
    private SkinnedMeshRenderer grappleMouthRenderer;
    private Transform grappleMouthRendererRoot;

    public Vector3 GetGrappleMouthPoint(Vector3 flatForward)
    {
        if (grappleMouthAnchor != null)
        {
            return grappleMouthAnchor.position;
        }

        if (characterView == null)
        {
            characterView = GetComponent<PlayerCharacterView>();
        }

        Transform modelRoot = characterView != null ? characterView.GetActiveCharacterRoot() : null;
        if (modelRoot != null)
        {
            if (modelRoot != grappleMouthRendererRoot)
            {
                grappleMouthRendererRoot = modelRoot;
                grappleMouthRenderer = modelRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            }

            if (grappleMouthRenderer != null)
            {
                // 모델 로컬 바운즈의 앞쪽 끝(주둥이) 근처: 위로 55%, 앞으로 85% 지점.
                Bounds bounds = grappleMouthRenderer.localBounds;
                Vector3 local = bounds.center
                    + new Vector3(0f, bounds.extents.y * 0.55f, bounds.extents.z * 0.85f);
                return grappleMouthRenderer.transform.TransformPoint(local);
            }
        }

        // 폴백: 루트 기준 고정 오프셋.
        return transform.position + flatForward * 0.4f + Vector3.up * 0.45f;
    }

    // clientId → PlayerController 조회. NetworkManager.ConnectedClients는 서버 전용이라
    // 클라이언트에서도 동작하도록 씬의 플레이어를 직접 찾고 캐시한다.
    private PlayerController ResolveGrabberByClientId(ulong clientId)
    {
        if (clientId == NoGrabber)
        {
            return null;
        }

        if (grabberCache != null && grabberCacheClientId == clientId)
        {
            return grabberCache;
        }

        grabberCache = null;
        grabberCacheClientId = clientId;

        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i] != this && players[i].OwnerClientId == clientId)
            {
                grabberCache = players[i];
                break;
            }
        }

        return grabberCache;
    }

    // 놓인 뒤(던짐 포함) 로컬 충돌 상태 복원. 콜라이더는 즉시 켜되,
    // 공격자와의 충돌 무시는 '충분히 떨어졌을 때'(또는 타임아웃) 되돌린다 —
    // 겹친 채 복원하면 물리가 둘을 강제로 밀어내 원치 않는 밀림이 생긴다.
    private void HandleGrabReleasedLocal()
    {
        if (grabDisabledColliders.Count > 0)
        {
            for (int i = 0; i < grabDisabledColliders.Count; i++)
            {
                if (grabDisabledColliders[i] != null)
                {
                    grabDisabledColliders[i].enabled = true;
                }
            }
            grabDisabledColliders.Clear();
            grabPairRestoreAt = Time.time + 0.25f;
        }

        if (grabCollisionIgnoredWith != null && grabPairRestoreAt >= 0f && Time.time >= grabPairRestoreAt)
        {
            float sqrDistance = (grabCollisionIgnoredWith.transform.position - transform.position).sqrMagnitude;
            bool farEnough = sqrDistance > 1.2f * 1.2f;
            bool timedOut = Time.time >= grabPairRestoreAt + 2f;

            if (farEnough || timedOut)
            {
                RestoreGrabCollision();
                grabPairRestoreAt = -1f;
            }
        }
    }

    private void ApplyGrabCollisionIgnore(PlayerController grabber)
    {
        if (grabCollisionIgnoredWith == grabber)
        {
            return;
        }

        RestoreGrabCollision();

        if (grabber != null && physicsCollider != null && grabber.physicsCollider != null)
        {
            Physics.IgnoreCollision(physicsCollider, grabber.physicsCollider, true);
            grabCollisionIgnoredWith = grabber;
        }
    }

    private void RestoreGrabCollision()
    {
        if (grabCollisionIgnoredWith == null)
        {
            return;
        }

        if (physicsCollider != null && grabCollisionIgnoredWith.physicsCollider != null)
        {
            Physics.IgnoreCollision(physicsCollider, grabCollisionIgnoredWith.physicsCollider, false);
        }

        grabCollisionIgnoredWith = null;
    }

    // 세션 종료/피해자·공격자 소멸 시 상태 정리(Combat.OnNetworkDespawn에서 호출).
    private void ServerGrappleCleanup()
    {
        // 로컬 정리(모든 인스턴스): 물린 동안 꺼둔 콜라이더/충돌 무시가 세션 밖까지 남지 않게 복원.
        for (int i = 0; i < grabDisabledColliders.Count; i++)
        {
            if (grabDisabledColliders[i] != null)
            {
                grabDisabledColliders[i].enabled = true;
            }
        }
        grabDisabledColliders.Clear();
        RestoreGrabCollision();
        grabPairRestoreAt = -1f;

        if (!IsServer)
        {
            return;
        }

        if (grabbingVictimNetId.Value != NoGrabber)
        {
            ServerReleaseGrab(false);
        }

        if (grabbedByClientId.Value != NoGrabber)
        {
            grabbedByClientId.Value = NoGrabber;
        }
    }
}
