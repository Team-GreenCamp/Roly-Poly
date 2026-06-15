using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

// ───────────────────────────────────────────────────────────────────────────
// 서버 권한(Server-authoritative) 기믹 패턴 — 이 파일이 다른 기믹의 표준 예시입니다.
//
//  1) 상태(isOn)는 NetworkVariable로 두고 "서버만" 변경합니다.
//  2) 클라이언트는 ServerRpc로 "토글 요청"만 보냅니다.
//  3) 상태가 바뀌면 OnValueChanged 콜백에서 연출/이벤트를 "모든 클라이언트"가 동일하게 실행합니다.
//  4) NetworkObject가 없거나 아직 스폰되지 않았다면(에디터 단독 테스트 등) 기존처럼 로컬에서 동작합니다.
//
// 이 구조 덕분에 한 명이 켠 레버가 모든 플레이어 화면에서 동일하게 켜지고,
// onToggleOn/Off 이벤트도 전원에게서 동시에 실행됩니다.
// ───────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(NetworkObject))]
public class LeverGimmick : NetworkBehaviour, IInteractable
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

    // 서버만 쓰고 모두가 읽는 동기화 상태. 값이 바뀌면 모든 클라이언트에서 OnValueChanged가 호출됩니다.
    private readonly NetworkVariable<bool> networkIsOn =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private Coroutine rotateCoroutine;

    // NetworkObject가 붙어 있고 실제로 스폰된 경우에만 네트워크 경로를 사용합니다.
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);
    }

    public override void OnNetworkSpawn()
    {
        // 초기 상태 시드는 구독 전에 처리해 스폰 시점에 이벤트가 튀지 않게 합니다.
        if (IsServer && networkIsOn.Value != isOn)
        {
            networkIsOn.Value = isOn;
        }

        networkIsOn.OnValueChanged += HandleNetworkIsOnChanged;

        // 늦게 들어온 클라이언트도 현재 상태로 즉시 맞춥니다. (연출/이벤트 없이 각도만 스냅)
        isOn = networkIsOn.Value;
        ApplyHandleInstant(isOn);
    }

    public override void OnNetworkDespawn()
    {
        networkIsOn.OnValueChanged -= HandleNetworkIsOnChanged;
    }

    public void RequestInteract(GameObject interactor)
    {
        if (!IsNetworkActive)
        {
            // 오프라인/네트워크 미구성: 기존처럼 로컬에서 토글
            ToggleLocal();
            return;
        }

        if (IsServer)
        {
            ToggleOnServer();
            return;
        }

        // 클라이언트는 서버에 토글을 요청만 합니다. (레버는 누구나 조작 가능하므로 소유권 불필요)
        RequestToggleServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleServerRpc(ServerRpcParams rpcParams = default)
    {
        ToggleOnServer();
    }

    private void ToggleOnServer()
    {
        // 서버에서만 상태를 뒤집습니다. 전파는 NetworkVariable가 담당합니다.
        networkIsOn.Value = !networkIsOn.Value;
    }

    private void HandleNetworkIsOnChanged(bool previousValue, bool newValue)
    {
        // 모든 클라이언트(+서버)에서 동일하게 실행되는 연출/이벤트 처리.
        isOn = newValue;
        FireToggleEvents(newValue);
        AnimateHandle(newValue);
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
