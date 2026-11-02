using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 숫자키 1~4 감정표현.
// 소유자가 고른 감정표현을 animState(AnimEmoteFirst + 번호)로 동기화하므로 추가 RPC 없이 모든 클라이언트에 보인다.
// 경기 중에는 서 있을 때만 시작되고, 움직이거나 점프·피격 등 다른 동작이 시작되면 끊긴다.
public partial class PlayerController
{
    [System.Serializable]
    private struct EmoteDefinition
    {
        [Tooltip("Base Layer 상태 이름(13종 캐릭터 공통).")]
        public string bodyState;
        [Tooltip("Shapekey 레이어 눈 표정 상태 이름. 비우면 중립 눈.")]
        public string eyeState;
        [Tooltip("재생 시간(초). 0이면 움직이거나 다른 동작을 할 때까지 유지한다.")]
        public float duration;

        public EmoteDefinition(string bodyState, string eyeState, float duration)
        {
            this.bodyState = bodyState;
            this.eyeState = eyeState;
            this.duration = duration;
        }
    }

    [Header("Emotes (숫자키 1~4)")]
    [SerializeField] private EmoteDefinition[] emotes =
    {
        new EmoteDefinition("Fly", "Eyes_Excited", 3f),   // 1: 손 흔들기
        new EmoteDefinition("Attack", "Eyes_Rabid", 3f),  // 2: 도발
        new EmoteDefinition("Fear", "Eyes_Cry", 3f),      // 3: 엉엉
        new EmoteDefinition("Lay", "Eyes_Sleep", 5f),     // 4: 잠자기
    };

    private int activeEmote = -1;
    private float activeEmoteUntil;

    // 소유자(또는 오프라인) 인스턴스에서만 호출한다.
    private void ReadEmoteInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || InGamePauseMenu.IsOpen || IsTypingInTextField())
        {
            return;
        }

        int pressed = -1;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) pressed = 0;
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) pressed = 1;
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) pressed = 2;
        else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) pressed = 3;

        if (pressed < 0 || emotes == null || pressed >= emotes.Length)
        {
            return;
        }

        // 같은 감정표현을 다시 누르면 재생 중인 동작은 그대로 두고 지속 시간만 갱신한다.
        if (pressed != activeEmote && IsBusyForEmote())
        {
            return;
        }

        activeEmote = pressed;
        float duration = emotes[pressed].duration;
        activeEmoteUntil = duration > 0f ? Time.time + duration : float.PositiveInfinity;
    }

    // ComputeAnimState에서 호출: 진행 중인 감정표현이 있으면 그 animState를 돌려준다.
    private bool TryGetEmoteAnimState(out byte state)
    {
        state = AnimIdle;
        if (activeEmote < 0)
        {
            return false;
        }

        if (emotes == null || activeEmote >= emotes.Length || Time.time >= activeEmoteUntil || IsBusyForEmote())
        {
            activeEmote = -1;
            return false;
        }

        state = (byte)(AnimEmoteFirst + activeEmote);
        return true;
    }

    // 로비·카운트다운처럼 게임플레이 입력이 꺼진 동안은 언제든, 경기 중에는 가만히 서 있을 때만 감정표현을 한다.
    private bool IsBusyForEmote()
    {
        if (!gameplayInputEnabled)
        {
            return false;
        }

        // 키를 놓아도 감속 중이거나 밀려나는 중이면 걷기로 보이므로 실제 속도도 본다.
        float planarSpeed = physicsBody != null
            ? Vector3.ProjectOnPlane(physicsBody.linearVelocity, Vector3.up).magnitude
            : 0f;

        return moveInput.sqrMagnitude > 0.01f
            || planarSpeed >= animWalkThreshold
            || !isGrounded
            || isKnockedDown
            || IsStunned
            || IsGrabbed
            || IsGrabbingSomeone
            || Time.time < grappleAttackAnimUntil
            || Time.time < dashRollAnimUntil;
    }

    private bool TryGetEmote(byte state, out EmoteDefinition emote)
    {
        int index = state - AnimEmoteFirst;
        if (emotes != null && index >= 0 && index < emotes.Length)
        {
            emote = emotes[index];
            return true;
        }

        emote = default;
        return false;
    }

    // 닉네임 입력 중 숫자를 쳐도 감정표현이 나가지 않게 한다.
    private static bool IsTypingInTextField()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.TryGetComponent(out TMP_InputField field) && field.isFocused;
    }
}
