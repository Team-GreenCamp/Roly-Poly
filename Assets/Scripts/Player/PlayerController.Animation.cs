using Unity.Netcode;
using UnityEngine;

// 활성 캐릭터의 Animator를 "상태 이름"으로 구동하는 단일 드라이버.
//
// 13종 캐릭터 컨트롤러(AC_Cat, AC_Zebra…)가 상태 이름을 동일하게 공유하므로(Idle_A/Walk/Run/Jump/Hit…),
// 캐릭터 분기 없이 이 코드 한 벌로 전부 구동된다.
//
// 네트워킹: 플레이어는 Owner 권한이고 원격 사본은 kinematic이라 속도를 신뢰할 수 없다.
// 따라서 "소유자가 상태를 판정 → animState NetworkVariable(Owner write)로 동기화 → 모든 인스턴스가 그 값으로 재생".
public partial class PlayerController
{
    private const byte AnimIdle = 0;
    private const byte AnimWalk = 1;
    private const byte AnimRun = 2;
    private const byte AnimJump = 3;
    private const byte AnimHit = 4;
    private const byte AnimRoll = 5;   // 대시(F) 구르기
    private const byte AnimFear = 6;   // 붙잡힌 동안 버둥거림
    private const byte AnimAttack = 7; // 잡기/던지기 순간
    private const byte AnimSpin = 8;   // 승자 세리머니

    [Header("Animation Driver")]
    [Tooltip("이 평면 속도 이상이면 걷기(Walk)로 판정합니다(m/s).")]
    [SerializeField] private float animWalkThreshold = 0.2f;
    [Tooltip("이 평면 속도 이상이면 달리기(Run)로 판정합니다(m/s).")]
    [SerializeField] private float animRunThreshold = 4.5f;
    [Tooltip("애니메이션 전환(CrossFade) 시간(초).")]
    [SerializeField] private float animCrossFadeDuration = 0.12f;

    [Header("Eye Reaction (눈 표정 연출)")]
    [Tooltip("넉다운/스턴 시 재생할 눈 표정 상태 이름(Shapekey 레이어). 비우면 눈 연출 비활성.")]
    [SerializeField] private string eyeReactionState = "Eyes_Spin";
    [Tooltip("붙잡힌 동안 재생할 눈 표정(울상).")]
    [SerializeField] private string grabbedEyeState = "Eyes_Cry";
    [Tooltip("상대를 붙잡고 있는 동안 재생할 눈 표정(광폭).")]
    [SerializeField] private string grabbingEyeState = "Eyes_Rabid";
    [Tooltip("승자 세리머니 동안 재생할 눈 표정.")]
    [SerializeField] private string winEyeState = "Eyes_Happy";
    [Tooltip("눈 표정 레이어 가중치가 0↔1로 켜지고 꺼지는 속도.")]
    [SerializeField] private float eyeReactionWeightLerpSpeed = 8f;

    [Tooltip("눈 깜빡임 상태 이름(Shapekey 레이어). 비우면 깜빡임 비활성.")]
    [SerializeField] private string blinkState = "Eyes_Blink";
    [Tooltip("깜빡임 사이 최소 간격(초).")]
    [SerializeField] private float blinkIntervalMin = 2.5f;
    [Tooltip("깜빡임 사이 최대 간격(초).")]
    [SerializeField] private float blinkIntervalMax = 6f;
    [Tooltip("한 번 깜빡일 때 눈 레이어가 켜져 있는 시간(초). 깜빡임 클립 길이에 맞추세요.")]
    [SerializeField] private float blinkDuration = 0.25f;

    [Tooltip("애니메이션/눈 구동 진단 로그. 필요할 때만 켜세요.")]
    [SerializeField] private bool logAnimationDebug = false;

    // 소유자가 쓰고 모두가 읽는 애니메이션 상태(위 Anim* 상수).
    private readonly NetworkVariable<byte> animState =
        new NetworkVariable<byte>(AnimIdle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Animator activeAnimator;
    private Transform animRootCached;
    private Animator lastAppliedAnimator;
    private int lastAppliedAnimState = -1;

    // 눈 표정(Shapekey) 레이어 구동 상태.
    private Animator cachedEyeAnimator;
    private int shapekeyLayerIndex = -1;
    private string activeEyeReaction; // 현재 재생 중인 리액션 눈 상태(null=중립/깜빡임)
    private float nextBlinkTime = -1f;
    private float blinkActiveUntil = -1f;

    // 승자 세리머니(Spin) — SurvivalGameManager가 승자 소유자 클라이언트에서 켜고, 씬 언로드 시 끈다.
    private bool isCelebrating;
    public void SetCelebrating(bool celebrating)
    {
        isCelebrating = celebrating;
    }

    // PlayerController.Update에서 모든 인스턴스가 매 프레임 호출.
    private void DriveAnimation()
    {
        bool networked = networkObject != null && networkObject.IsSpawned;

        byte effectiveState;
        if (!networked)
        {
            // 오프라인/단독 테스트: 로컬에서 판정.
            effectiveState = ComputeAnimState();
        }
        else
        {
            // 소유자만 실제 물리 상태로 판정해 동기화 변수에 기록한다.
            if (IsOwner)
            {
                byte desired = ComputeAnimState();
                if (animState.Value != desired)
                {
                    animState.Value = desired;
                }
            }
            effectiveState = animState.Value;
        }

        // 모든 인스턴스(소유자/원격)가 동기화된 상태로 활성 캐릭터를 재생한다.
        Animator anim = ResolveActiveAnimator();
        if (anim == null)
        {
            if (logAnimationDebug && Time.frameCount % 120 == 0)
            {
                Transform r = characterView != null ? characterView.GetActiveCharacterRoot() : null;
                Debug.LogWarning($"[Anim] 활성 캐릭터 Animator를 찾지 못함. activeRoot={(r != null ? r.name : "null")}");
            }
            return;
        }

        ApplyBodyState(anim, effectiveState);
        UpdateEyeExpression(anim, effectiveState);
    }

    // 권한(소유자/오프라인) 인스턴스에서만 유효한 상태 판정.
    private byte ComputeAnimState()
    {
        // 승자 세리머니는 매치 종료로 입력이 잠긴 동안 재생되므로 입력 상태보다 먼저 본다.
        if (isCelebrating)
        {
            return AnimSpin;
        }

        // 로비 등 게임플레이 입력이 꺼진 상태에서는 항상 Idle.(로비에선 접지 계산도 돌지 않음)
        if (!gameplayInputEnabled)
        {
            return AnimIdle;
        }

        if (isKnockedDown || IsStunned)
        {
            return AnimHit;
        }

        // 붙잡힌 동안에는 버둥거림(Fear).
        if (IsGrabbed)
        {
            return AnimFear;
        }

        // 잡기/던지기 순간의 짧은 공격 모션.
        if (Time.time < grappleAttackAnimUntil)
        {
            return AnimAttack;
        }

        // 대시(F) 직후에는 구르기. (공중/지상 무관 — 대시 연출이 우선)
        if (Time.time < dashRollAnimUntil)
        {
            return AnimRoll;
        }

        if (!isGrounded)
        {
            return AnimJump;
        }

        float planarSpeed = physicsBody != null
            ? Vector3.ProjectOnPlane(physicsBody.linearVelocity, Vector3.up).magnitude
            : 0f;

        if (planarSpeed >= animRunThreshold) return AnimRun;
        if (planarSpeed >= animWalkThreshold) return AnimWalk;
        return AnimIdle;
    }

    private void ApplyBodyState(Animator anim, byte state)
    {
        // 상태가 바뀌었거나 캐릭터(=Animator)가 바뀐 경우에만 다시 CrossFade.(Base Layer=0)
        if (anim == lastAppliedAnimator && lastAppliedAnimState == state)
        {
            return;
        }

        byte previousState = (byte)(lastAppliedAnimState < 0 ? AnimIdle : lastAppliedAnimState);
        lastAppliedAnimator = anim;
        lastAppliedAnimState = state;
        anim.CrossFadeInFixedTime(AnimStateName(state), Mathf.Max(0f, animCrossFadeDuration), 0);

        // 상태 '진입' 시 SFX/VFX. animState는 동기화 변수라 이 지점은 모든 클라이언트에서
        // 정확히 한 번씩 실행된다 — 추가 네트워킹 없이 전원에게 연출이 재생되는 훅.
        if (previousState != state)
        {
            switch (state)
            {
                case AnimHit:
                    GameFeedback.HitAt(BodyCenter);
                    break;
                case AnimRoll:
                    GameFeedback.DashAt(transform.position);
                    break;
                case AnimAttack:
                    GameFeedback.SwingAt(BodyCenter);
                    break;
                case AnimSpin:
                    GameFeedback.WinnerAt(transform.position);
                    break;
            }
        }
    }

    // 눈(Shapekey) 레이어 구동:
    //  • 넉다운/스턴(Hit): 어질어질 표정(eyeReactionState)을 우선 재생.
    //  • 평상시: 가중치 0(중립)이되, 랜덤 간격으로 잠깐 깜빡임(blinkState) 재생.
    // 깜빡임은 연출이라 각 클라가 로컬에서 독립적으로 처리한다(동기화 불필요).
    private void UpdateEyeExpression(Animator anim, byte state)
    {
        // 캐릭터가 바뀌면 Shapekey 레이어 인덱스를 다시 찾는다.
        if (anim != cachedEyeAnimator)
        {
            cachedEyeAnimator = anim;
            shapekeyLayerIndex = -1;
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < anim.layerCount; i++)
            {
                names.Append(i == 0 ? "" : ",").Append(anim.GetLayerName(i));
                if (anim.GetLayerName(i) == "Shapekey")
                {
                    shapekeyLayerIndex = i;
                }
            }
            activeEyeReaction = null;
            blinkActiveUntil = -1f;

            if (logAnimationDebug)
            {
                Debug.Log($"[AnimEye] animator='{anim.runtimeAnimatorController?.name}' enabled={anim.enabled} layers={anim.layerCount} names=[{names}] shapekeyIdx={shapekeyLayerIndex}");
            }
        }

        if (shapekeyLayerIndex < 0)
        {
            return;
        }

        // 몸 상태/잡기 상태에 따라 리액션 눈 표정을 고른다. (null이면 중립+깜빡임)
        string desiredEye = null;
        if (state == AnimHit) desiredEye = eyeReactionState;                 // 어질어질
        else if (state == AnimFear) desiredEye = grabbedEyeState;            // 붙잡힘: 울상
        else if (state == AnimSpin) desiredEye = winEyeState;                // 승자: 행복
        else if (IsGrabbingSomeone) desiredEye = grabbingEyeState;           // 붙잡는 중: 광폭 (동기화 변수라 원격도 표시)

        bool reaction = !string.IsNullOrEmpty(desiredEye);
        float target;
        bool snap = false;

        if (reaction)
        {
            // 리액션 표정 우선. 표정이 바뀌면 다시 크로스페이드. 깜빡임 일정은 뒤로 미룬다.
            if (activeEyeReaction != desiredEye)
            {
                anim.CrossFadeInFixedTime(desiredEye, 0.08f, shapekeyLayerIndex);
                activeEyeReaction = desiredEye;
            }
            blinkActiveUntil = -1f;
            nextBlinkTime = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
            target = 1f;
        }
        else
        {
            activeEyeReaction = null;
            target = 0f;

            if (!string.IsNullOrEmpty(blinkState))
            {
                if (nextBlinkTime < 0f)
                {
                    nextBlinkTime = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
                }

                // 깜빡일 차례가 되면 짧게 재생.
                if (Time.time >= blinkActiveUntil && Time.time >= nextBlinkTime)
                {
                    anim.CrossFadeInFixedTime(blinkState, 0.03f, shapekeyLayerIndex);
                    blinkActiveUntil = Time.time + Mathf.Max(0.05f, blinkDuration);
                    nextBlinkTime = blinkActiveUntil + Random.Range(blinkIntervalMin, blinkIntervalMax);
                    if (logAnimationDebug)
                    {
                        Debug.Log($"[AnimEye] BLINK t={Time.time:F1} layer={shapekeyLayerIndex} state='{blinkState}' (weight->1 for {blinkDuration:F2}s)");
                    }
                }

                if (Time.time < blinkActiveUntil)
                {
                    target = 1f;
                    snap = true; // 깜빡임은 짧으니 가중치를 즉시 올린다.
                }
            }
        }

        float current = anim.GetLayerWeight(shapekeyLayerIndex);
        float next = snap
            ? target
            : Mathf.MoveTowards(current, target, Time.deltaTime * Mathf.Max(0.01f, eyeReactionWeightLerpSpeed));
        anim.SetLayerWeight(shapekeyLayerIndex, next);

        if (logAnimationDebug && Time.frameCount % 60 == 0)
        {
            var baseInfo = anim.GetCurrentAnimatorStateInfo(0);
            var eyeInfo = anim.GetCurrentAnimatorStateInfo(shapekeyLayerIndex);
            Debug.Log($"[AnimTick] baseNorm={baseInfo.normalizedTime:F2} eyeLayerW={anim.GetLayerWeight(shapekeyLayerIndex):F2} " +
                      $"eyeIsBlink={eyeInfo.IsName(blinkState)} eyeNorm={eyeInfo.normalizedTime:F2} cull={anim.cullingMode} animEnabled={anim.enabled}");
        }
    }

    private Animator ResolveActiveAnimator()
    {
        if (characterView == null)
        {
            characterView = GetComponent<PlayerCharacterView>();
        }

        Transform root = characterView != null ? characterView.GetActiveCharacterRoot() : null;
        if (root == null)
        {
            return null;
        }

        // 캐릭터가 바뀌면 그 모델의 Animator를 다시 찾는다.
        if (root != animRootCached)
        {
            animRootCached = root;
            activeAnimator = root.GetComponent<Animator>();
            if (activeAnimator == null)
            {
                activeAnimator = root.GetComponentInChildren<Animator>(true);
            }

            if (activeAnimator != null)
            {
                // 화면 밖/LOD 컬링으로 블렌드셰이프(눈) 갱신이 멈추지 않도록 항상 애니메이트.
                activeAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        return activeAnimator;
    }

    private static string AnimStateName(byte state)
    {
        switch (state)
        {
            case AnimWalk: return "Walk";
            case AnimRun: return "Run";
            case AnimJump: return "Jump";
            case AnimHit: return "Hit";
            case AnimRoll: return "Roll";
            case AnimFear: return "Fear";
            case AnimAttack: return "Attack";
            case AnimSpin: return "Spin";
            default: return "Idle_A";
        }
    }
}
