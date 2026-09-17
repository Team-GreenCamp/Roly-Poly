using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

// ───────────────────────────────────────────────────────────────────────────
// 서버 권한(Server-authoritative) 기믹 패턴 — 이 파일이 다른 기믹의 표준 예시입니다.
//
//  1) 상태(isOn)는 NetworkVariable로 두고 "서버만" 변경합니다.
//  2) 클라이언트는 ServerRpc로 "토글 요청"만 보냅니다.
//  3) 상태가 바뀌면 OnStateChanged에서 연출/이벤트를 "모든 클라이언트"가 동일하게 실행합니다.
//  4) NetworkObject가 없거나 아직 스폰되지 않았다면(에디터 단독 테스트 등) 기존처럼 로컬에서 동작합니다.
//
// 스폰/디스폰 구독·스냅·서버 상태 변경 헬퍼 같은 공통 뼈대는 NetworkToggleGimmick(base)이 담당합니다.
// ───────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(NetworkObject))]
public class LeverGimmick : NetworkToggleGimmick
{
    [Header("레버 상태")]
    public bool isOn = false; // 인스펙터에서 지정하는 초기 상태(런타임에는 동기화 상태의 로컬 캐시)

    [Header("레버 연출 설정")]
    public Transform handle; // 돌아갈 막대기 부분
    public Vector3 offRotation = new Vector3(-30, 0, 0);   // 꺼졌을 때 각도
    public Vector3 onRotation = new Vector3(30, 0, 0);     // 켜졌을 때 각도 (X축으로 30도 젖힘)
    public float rotateSpeed = 5f;

    [Header("작동 이벤트")]
    public UnityEvent onToggleOn;  // 켰을 때 실행할 일
    public UnityEvent onToggleOff; // 껐을 때 실행할 일

    // 서버만 쓰고 모두가 읽는 동기화 상태. (NGO 등록 안정성을 위해 NetworkVariable는 파생 클래스 필드로 유지)
    private readonly NetworkVariable<bool> networkIsOn =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected override NetworkVariable<bool> StateVariable => networkIsOn;

    private Coroutine rotateCoroutine;

    // 인스펙터에서 지정한 isOn을 서버가 스폰 시 초기 상태로 시드합니다.
    protected override bool GetServerInitialState() => isOn;

    public override void RequestInteract(GameObject interactor)
    {
        if (!IsNetworkActive)
        {
            // 오프라인/네트워크 미구성: 기존처럼 로컬에서 토글
            ToggleLocal();
            return;
        }

        if (IsServer)
        {
            ToggleStateOnServer();
            return;
        }

        // 클라이언트는 서버에 토글을 요청만 합니다. (레버는 누구나 조작 가능하므로 소유권 불필요)
        RequestToggleServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleServerRpc(ServerRpcParams rpcParams = default)
    {
        ToggleStateOnServer();
    }

    protected override void OnStateChanged(bool previousValue, bool newValue)
    {
        // 모든 클라이언트(+서버)에서 동일하게 실행되는 연출/이벤트 처리.
        isOn = newValue;
        FireToggleEvents(newValue);
        AnimateHandle(newValue);
    }

    protected override void ApplyStateInstant(bool state)
    {
        isOn = state;
        ApplyHandleInstant(state);
    }

    // ───── 로컬(비네트워크) 폴백 ─────
    private void ToggleLocal()
    {
        isOn = !isOn;
        FireToggleEvents(isOn);
        AnimateHandle(isOn);
    }

    private void FireToggleEvents(bool on)
    {
        if (on) onToggleOn.Invoke();
        else onToggleOff.Invoke();
    }

    private void AnimateHandle(bool on)
    {
        if (handle == null) return;
        if (rotateCoroutine != null) StopCoroutine(rotateCoroutine);
        rotateCoroutine = StartCoroutine(RotateHandleRoutine(on ? onRotation : offRotation));
    }

    private void ApplyHandleInstant(bool on)
    {
        if (handle == null) return;
        if (rotateCoroutine != null)
        {
            StopCoroutine(rotateCoroutine);
            rotateCoroutine = null;
        }
        handle.localRotation = Quaternion.Euler(on ? onRotation : offRotation);
    }

    private IEnumerator RotateHandleRoutine(Vector3 targetEulerAngles)
    {
        Quaternion targetRotation = Quaternion.Euler(targetEulerAngles);

        // 목표 각도에 도달할 때까지 부드럽게 회전
        while (Quaternion.Angle(handle.localRotation, targetRotation) > 0.01f)
        {
            handle.localRotation = Quaternion.Slerp(handle.localRotation, targetRotation, rotateSpeed * Time.deltaTime);
            yield return null;
        }
        handle.localRotation = targetRotation;
    }
}
