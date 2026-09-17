using Unity.Netcode;
using UnityEngine;

// 도어 대시(Door Dash) 전용 관문 도어 컴포넌트.
// 진짜 문은 플레이어가 들이받으면 부서지듯 앞으로 젖혀지며 열리고,
// 가짜 문은 단단히 잠겨 있어 들이받은 플레이어가 뒤로 튕겨 나갑니다.
// 멀티플레이어 환경(Netcode for GameObjects)을 지원하며, 서버 권한으로 모든 클라이언트에 문 열림이 동기화됩니다.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class DoorDashGate : NetworkBehaviour
{
    [Header("Door Configuration")]
    [Tooltip("진짜 문 여부 (체크: 들이받으면 열림, 해제: 단단한 가짜 문)")]
    [SerializeField] private bool isRealDoor = true;

    [Tooltip("진짜 문이 열릴 때 앞으로 날아가는 물리 힘")]
    [SerializeField] private float openPushForce = 12f;

    [Tooltip("가짜 문에 부딪혔을 때 플레이어가 튕겨 나가는 반발력")]
    [SerializeField] private float fakeBounceForce = 8f;

    [Tooltip("문이 열릴 때 회전할 힌지 축 (상단 기준 또는 하단 기준)")]
    [SerializeField] private Rigidbody doorBody;

    [Header("Visual Feedback")]
    [SerializeField] private Renderer doorRenderer;
    [SerializeField] private Material brokenOrOpenMaterial;

    private readonly NetworkVariable<bool> networkOpened =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private bool isOpened;

    public bool IsRealDoor => isRealDoor;
    public bool IsOpened => IsNetworkActive ? networkOpened.Value : isOpened;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        if (doorBody == null)
        {
            doorBody = GetComponent<Rigidbody>();
        }

        if (doorRenderer == null)
        {
            doorRenderer = GetComponent<Renderer>();
        }

        // 시작 시에는 문이 굳건히 닫혀 있도록 Kinematic 설정
        if (doorBody != null)
        {
            doorBody.isKinematic = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        networkOpened.OnValueChanged += HandleOpenedChanged;

        // 스폰 시점에 이미 문이 열린 상태(지각 접속 또는 늦은 동기화)라면 즉시 반영
        if (networkOpened.Value)
        {
            ApplyBreakOpenVisual(Vector3.forward, instant: true);
        }
    }

    public override void OnNetworkDespawn()
    {
        networkOpened.OnValueChanged -= HandleOpenedChanged;
    }

    private void HandleOpenedChanged(bool previousValue, bool newValue)
    {
        if (newValue && !isOpened)
        {
            // 네트워크 변수로 동기화된 문 열림 처리
            ApplyBreakOpenVisual(Vector3.forward, instant: false);
        }
    }

    public void Setup(bool isReal, Material openMat = null)
    {
        isRealDoor = isReal;
        if (openMat != null)
        {
            brokenOrOpenMaterial = openMat;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsOpened) return;

        // 플레이어 캐릭터 충돌 검사
        var playerBody = collision.rigidbody;
        var playerController = collision.gameObject.GetComponentInParent<PlayerController>();

        if (playerBody == null && playerController == null) return;

        // 중복 RPC 및 타인 클라이언트의 대리 호출 방지: 본인 로컬 플레이어 충돌만 처리
        if (playerController != null && !playerController.HasInputAuthority)
        {
            return;
        }

        Vector3 impactDir = collision.relativeVelocity;
        if (impactDir.sqrMagnitude < 0.01f)
        {
            impactDir = (transform.position - collision.transform.position).normalized;
        }

        if (isRealDoor)
        {
            if (!IsNetworkActive)
            {
                // 오프라인 폴백
                BreakOpen(impactDir);
            }
            else
            {
                // 네트워크 활성: 서버에 문 열림 요청
                RequestBreakOpenServerRpc(impactDir);
            }
        }
        else
        {
            // 가짜 문: 플레이어를 뒤로 강하게 튕겨냄 (우당탕탕 물리 연출)
            if (playerBody != null && !playerBody.isKinematic)
            {
                Vector3 bounceDirection = (collision.transform.position - transform.position);
                bounceDirection.y = 0.35f;
                bounceDirection.Normalize();

                playerBody.linearVelocity = Vector3.zero;
                playerBody.AddForce(bounceDirection * fakeBounceForce, ForceMode.VelocityChange);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBreakOpenServerRpc(Vector3 pushDirection, ServerRpcParams rpcParams = default)
    {
        if (!isRealDoor || networkOpened.Value) return;

        networkOpened.Value = true;
        BreakOpenClientRpc(pushDirection);
    }

    [ClientRpc]
    private void BreakOpenClientRpc(Vector3 pushDirection)
    {
        ApplyBreakOpenVisual(pushDirection, instant: false);
    }

    // 진짜 문 열림/부서짐 처리
    public void BreakOpen(Vector3 pushDirection)
    {
        ApplyBreakOpenVisual(pushDirection, instant: false);
    }

    private void ApplyBreakOpenVisual(Vector3 pushDirection, bool instant)
    {
        if (isOpened) return;
        isOpened = true;

        if (doorBody != null)
        {
            doorBody.isKinematic = false;
            doorBody.useGravity = true;

            if (!instant)
            {
                // 살짝 위쪽과 전방으로 힘을 가해 문이 덜컹 넘어지게 함
                Vector3 force = pushDirection.normalized * openPushForce + Vector3.up * (openPushForce * 0.25f);
                doorBody.AddForce(force, ForceMode.Impulse);
                doorBody.AddTorque(Random.insideUnitSphere * openPushForce * 0.5f, ForceMode.Impulse);
            }
        }

        if (doorRenderer != null && brokenOrOpenMaterial != null)
        {
            doorRenderer.sharedMaterial = brokenOrOpenMaterial;
        }

        if (!instant)
        {
            // 약간의 시간이 지나면 바닥에 안착하도록 드래그 증가
            Invoke(nameof(StabilizeDoor), 1.5f);
        }
        else
        {
            StabilizeDoor();
        }
    }

    private void StabilizeDoor()
    {
        if (doorBody != null && !doorBody.isKinematic)
        {
            doorBody.linearDamping = 3f;
            doorBody.angularDamping = 5f;
        }
    }
}
