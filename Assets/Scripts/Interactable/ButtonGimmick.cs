using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

// 서버 권한 기믹 패턴(순간식 버튼). 표준 설명은 LeverGimmick.cs 참고.
// 차이점: 버튼은 "눌림" 상태를 서버가 일정 시간(resetTime) 유지한 뒤 자동으로 해제합니다.
// 공통 뼈대(스폰/디스폰 구독·스냅·서버 상태 변경)는 NetworkToggleGimmick(base)이 담당합니다.
[RequireComponent(typeof(NetworkObject))]
public class ButtonGimmick : NetworkToggleGimmick
{
    public bool isActivated = false; // 런타임 표시용(동기화 상태의 로컬 캐시)

    [Header("버튼 연출 설정")]
    public Transform movingPart;

    [Tooltip("버튼이 눌리는 축과 방향입니다. Y축 아래로 넣으려면 (0, -1, 0), 위로 나오게 하려면 (0, 1, 0)으로 설정하세요.")]
    public Vector3 pressDirection = new Vector3(0, -1, 0);

    public float pressDistance = 0.1f;
    public float resetTime = 2.0f;
    public float moveSpeed = 2.0f;

    private Vector3 originalPos;
    private Vector3 pressedPos;

    [Header("작동 이벤트")]
    public UnityEvent onActivate;   // 눌렸을 때 실행할 일
    public UnityEvent onDeactivate; // 튀어나올 때 실행할 일

    private readonly NetworkVariable<bool> networkActivated =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected override NetworkVariable<bool> StateVariable => networkActivated;

    private Coroutine moveCoroutine;
    private Coroutine serverResetCoroutine;
    private Coroutine localCycleCoroutine;

    protected override void OnGimmickAwake()
    {
        // 위치 캐싱은 OnNetworkSpawn(스냅)보다 먼저 끝나야 하므로 Awake 시점에 처리합니다.
        if (movingPart != null)
        {
            originalPos = movingPart.localPosition;
            pressedPos = originalPos + (pressDirection.normalized * pressDistance);
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] 버튼의 Moving Part가 할당되지 않았습니다!");
        }
    }

    public override void RequestInteract(GameObject interactor)
    {
        if (!IsNetworkActive)
        {
            PressLocal();
            return;
        }

        if (IsServer)
        {
            PressOnServer();
            return;
        }

        RequestPressServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPressServerRpc(ServerRpcParams rpcParams = default)
    {
        PressOnServer();
    }

    private void PressOnServer()
    {
        if (networkActivated.Value) return; // 이미 눌린 상태면 무시

        SetStateOnServer(true);

        // 서버가 누름 상태를 일정 시간 유지한 뒤 자동 복귀시킵니다.
        if (serverResetCoroutine != null) StopCoroutine(serverResetCoroutine);
        serverResetCoroutine = StartCoroutine(ServerResetRoutine());
    }

    private IEnumerator ServerResetRoutine()
    {
        yield return new WaitForSeconds(resetTime);
        SetStateOnServer(false);
        serverResetCoroutine = null;
    }

    protected override void OnStateChanged(bool previousValue, bool newValue)
    {
        isActivated = newValue;

        if (newValue)
        {
            onActivate.Invoke();
            AnimatePart(pressedPos);
        }
        else
        {
            onDeactivate.Invoke();
            AnimatePart(originalPos);
        }
    }

    protected override void ApplyStateInstant(bool state)
    {
        isActivated = state;
        ApplyButtonInstant(state);
    }

    // ───── 로컬(비네트워크) 폴백: 기존 동작 그대로 ─────
    private void PressLocal()
    {
        if (isActivated) return;
        isActivated = true;
        onActivate.Invoke();

        if (localCycleCoroutine != null) StopCoroutine(localCycleCoroutine);
        localCycleCoroutine = StartCoroutine(LocalPressCycle());
    }

    private IEnumerator LocalPressCycle()
    {
        if (movingPart != null) yield return MovePartRoutine(pressedPos);
        yield return new WaitForSeconds(resetTime);
        if (movingPart != null) yield return MovePartRoutine(originalPos);

        isActivated = false;
        onDeactivate.Invoke();
        localCycleCoroutine = null;
    }

    private void AnimatePart(Vector3 target)
    {
        if (movingPart == null) return;
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        moveCoroutine = StartCoroutine(MovePartRoutine(target));
    }

    private void ApplyButtonInstant(bool activated)
    {
        if (movingPart == null) return;
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }
        movingPart.localPosition = activated ? pressedPos : originalPos;
    }

    private IEnumerator MovePartRoutine(Vector3 target)
    {
        while (Vector3.Distance(movingPart.localPosition, target) > 0.001f)
        {
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        movingPart.localPosition = target;
    }
}
