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
    [Tooltip("붙잡은 상대를 몸 앞 이 거리에 둡니다(m).")]
    [SerializeField] private float grappleHoldDistance = 1.2f;
    [SerializeField] private float grappleHoldHeight = 0f;
    [Tooltip("붙잡은 상대를 던지는 전방 세기.")]
    [SerializeField] private float grappleThrowForce = 13f;
    [Tooltip("던질 때 위로 띄우는 세기.")]
    [SerializeField] private float grappleThrowUp = 5f;
    [Tooltip("붙잡고 있는 동안 공격자의 이동 속도 배율(느려짐).")]
    [SerializeField] private float grappleCarryMultiplier = 0.5f;
    [SerializeField] private LayerMask grapplePlayerMask = ~0;

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

    // 붙잡힌 상태(피해자). 이동/조작 불가 + 소유자가 공격자 앞으로 따라간다.
    public bool IsGrabbed =>
        networkObject != null && networkObject.IsSpawned && grabbedByClientId.Value != NoGrabber;
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
            bool throwPressed =
                (grappleThrowAction != null && grappleThrowAction.WasPressedThisFrame()) ||
                (grappleAction != null && grappleAction.WasPressedThisFrame());

            if (throwPressed)
            {
                RequestGrappleThrow();
            }
            return;
        }

        // 붙잡기 시도: 손이 비어 있고(물체 상호작용과 분리), 쿨다운이 끝났고, Grab을 눌렀을 때.
        if (grappleAction == null || !grappleAction.WasPressedThisFrame())
        {
            return;
        }
        if (Time.time < grappleReadyTime || IsHoldingObject())
        {
            return;
        }

        PlayerController victim = FindGrappleTarget();
        if (victim == null)
        {
            return;
        }

        grappleReadyTime = Time.time + grappleCooldown;
        RequestGrab(victim.NetworkObjectId);
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

        // 임펄스/넉다운은 피해자 소유자만 로컬 적용(전투 릴레이 재사용).
        ClientRpcParams target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { victim.OwnerClientId }
            }
        };
        victim.ApplyCombatHitOwnerClientRpc(launch, victim.BodyCenter, CombatEffectKnockdown, target);
    }

    // 붙잡힌 피해자(소유자)를 공격자 몸 앞으로 이동시킨다. FixedUpdate에서 호출.
    private void UpdateGrabbedVictimMotion()
    {
        if (physicsBody == null)
        {
            return;
        }

        PlayerController grabber = ResolvePlayerByClientId(grabbedByClientId.Value);
        if (grabber == null)
        {
            // 공격자가 사라졌으면 서버가 곧 해제한다. 그동안 제자리 정지.
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(grabber.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = grabber.transform.forward;
        }
        forward.Normalize();

        Vector3 target = grabber.transform.position + forward * grappleHoldDistance;
        target.y = grabber.transform.position.y + grappleHoldHeight;

        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.position = target;
        physicsBody.rotation = Quaternion.LookRotation(forward);
    }

    private PlayerController ResolvePlayerByClientId(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
        {
            return client.PlayerObject.GetComponent<PlayerController>();
        }
        return null;
    }

    // 세션 종료/피해자·공격자 소멸 시 서버에서 상태 정리(Combat.OnNetworkDespawn에서 호출).
    private void ServerGrappleCleanup()
    {
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
