using Unity.Netcode;
using UnityEngine;

// 서버 권한(Server-authoritative) bool 상태 기믹의 공통 뼈대. (예: 레버, 버튼)
// 표준 패턴 설명은 LeverGimmick 참고.
//
// 설계 노트(중요):
//  • 상태 NetworkVariable<bool>는 "각 파생 클래스가 자기 필드로" 선언/소유합니다.
//    파생 클래스는 StateVariable 프로퍼티로 그 필드를 노출만 합니다.
//    → NGO의 NetworkVariable 등록 동작을 그대로 유지해(기존과 동일) 동기화가 깨질 위험을 피합니다.
//  • 스폰/디스폰 구독, 늦은 합류 스냅, 서버 상태 변경 헬퍼만 이 base가 담당해 중복을 제거합니다.
[RequireComponent(typeof(NetworkObject))]
public abstract class NetworkToggleGimmick : NetworkBehaviour, IInteractable
{
    private NetworkObject cachedNetworkObject;

    // NetworkObject가 붙어 있고 실제로 스폰된 경우에만 네트워크 경로를 사용합니다.
    protected bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    // 파생 클래스가 소유한 동기화 상태 변수(서버 쓰기 / 모두 읽기)를 반환합니다.
    protected abstract NetworkVariable<bool> StateVariable { get; }

    protected virtual void Awake()
    {
        TryGetComponent(out cachedNetworkObject);
        OnGimmickAwake();
    }

    // 위치/회전 캐싱 등 파생 클래스의 Awake 작업(스냅보다 먼저 끝나야 함)을 여기서 처리합니다.
    protected virtual void OnGimmickAwake() { }

    // 서버가 스폰 시 시드할 초기 상태. 기본은 현재 값(=시드 안 함).
    // Lever처럼 인스펙터 초기값을 반영하려면 override 합니다.
    protected virtual bool GetServerInitialState() => StateVariable.Value;

    public override void OnNetworkSpawn()
    {
        // 초기 상태 시드는 구독 전에 처리해 스폰 시점에 이벤트가 튀지 않게 합니다.
        if (IsServer)
        {
            bool initial = GetServerInitialState();
            if (StateVariable.Value != initial)
            {
                StateVariable.Value = initial;
            }
        }

        StateVariable.OnValueChanged += HandleStateChangedInternal;

        // 늦게 들어온 클라이언트도 현재 상태로 즉시 맞춥니다.(연출/이벤트 없이 스냅)
        ApplyStateInstant(StateVariable.Value);

        OnGimmickSpawned();
    }

    public override void OnNetworkDespawn()
    {
        StateVariable.OnValueChanged -= HandleStateChangedInternal;
        OnGimmickDespawned();
    }

    protected virtual void OnGimmickSpawned() { }
    protected virtual void OnGimmickDespawned() { }

    private void HandleStateChangedInternal(bool previous, bool current) => OnStateChanged(previous, current);

    // 상태가 바뀌면 모든 클라이언트(+서버)가 동일하게 실행하는 연출/이벤트 처리.
    protected abstract void OnStateChanged(bool previous, bool current);

    // 스폰/늦은 합류 시 연출 없이 현재 상태로 스냅합니다.
    protected abstract void ApplyStateInstant(bool state);

    // ───── 서버 상태 변경 헬퍼 ─────
    protected void ToggleStateOnServer()
    {
        if (IsServer)
        {
            StateVariable.Value = !StateVariable.Value;
        }
    }

    protected void SetStateOnServer(bool value)
    {
        if (IsServer && StateVariable.Value != value)
        {
            StateVariable.Value = value;
        }
    }

    public abstract void RequestInteract(GameObject interactor);
}
