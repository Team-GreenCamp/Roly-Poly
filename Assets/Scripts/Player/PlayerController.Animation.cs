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

    [Tooltip("애니메이션/눈 구동 진단 로그. 확인 후 끄세요.")]
    [SerializeField] private bool logAnimationDebug = true;

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
    private bool eyeReactionActive;
    private float nextBlinkTime = -1f;
    private float blinkActiveUntil = -1f;

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
        // 로비 등 게임플레이 입력이 꺼진 상태에서는 항상 Idle.(로비에선 접지 계산도 돌지 않음)
        if (!gameplayInputEnabled)
        {
            return AnimIdle;
        }

        if (isKnockedDown || IsStunned)
        {
            return AnimHit;
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

        lastAppliedAnimator = anim;
        lastAppliedAnimState = state;
        anim.CrossFadeInFixedTime(AnimStateName(state), Mathf.Max(0f, animCrossFadeDuration), 0);
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
            eyeReactionActive = false;
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

        bool reaction = state == AnimHit && !string.IsNullOrEmpty(eyeReactionState);
        float target;
        bool snap = false;

        if (reaction)
        {
            // 스턴/넉다운: 어질어질 표정 우선. 깜빡임 일정은 뒤로 미룬다.
            if (!eyeReactionActive)
            {
                anim.CrossFadeInFixedTime(eyeReactionState, 0.08f, shapekeyLayerIndex);
                eyeReactionActive = true;
            }
            blinkActiveUntil = -1f;
            nextBlinkTime = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
            target = 1f;
        }
        else
        {
            eyeReactionActive = false;
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
            default: return "Idle_A";
        }
    }
}
