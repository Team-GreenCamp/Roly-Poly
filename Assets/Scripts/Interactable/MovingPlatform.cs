using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 3번: 이동 발판 (서버 권한).
//
// 동기화하려면 이 오브젝트에 NetworkObject + NetworkTransform(Authority: Server)이 필요합니다.
//  • 발판 이동: 서버만 좌표를 전진시키고 NetworkTransform이 모두에게 전파합니다.
//  • 탑승자 운반: 각 클라이언트가 "자기 소유 플레이어만" 발판 이동량(delta)만큼 같이 옮깁니다.
//    (네트워크 플레이어는 소유자가 위치를 동기화하므로, 남의 플레이어를 옮기면 동기화와 충돌합니다.
//     게다가 원격 프록시는 kinematic이라 그 클라이언트에서는 발판과 충돌 이벤트 자체가 잡히지 않습니다.)
[RequireComponent(typeof(NetworkObject))]
public class MovingPlatform : NetworkBehaviour
{
    [Header("이동 설정")]
    public Vector3 moveOffset = new Vector3(5f, 0f, 0f);
    public float moveSpeed = 2f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private bool movingToTarget = true;

    private readonly List<Transform> passengers = new List<Transform>();
    private Rigidbody rb;
    private Vector3 lastPlatformPosition;

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    // 발판 좌표를 직접 전진시킬 권한이 있는 인스턴스인가(서버 또는 오프라인). 클라이언트는 NetworkTransform이 옮깁니다.
    private bool HasMoveAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        startPosition = transform.position;
        targetPosition = startPosition + moveOffset;
        lastPlatformPosition = transform.position;

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        // 1) 권한 인스턴스만 발판을 전진시킵니다. (클라이언트는 NetworkTransform이 transform을 옮김)
        if (HasMoveAuthority)
        {
            Vector3 destination = movingToTarget ? targetPosition : startPosition;
            Vector3 nextPosition = Vector3.MoveTowards(transform.position, destination, moveSpeed * Time.fixedDeltaTime);

            if (rb != null && rb.isKinematic)
                rb.MovePosition(nextPosition);
            else
                transform.position = nextPosition;

            if (Vector3.Distance(nextPosition, destination) < 0.01f)
                movingToTarget = !movingToTarget;
        }

        // 2) 발판이 이번 스텝에 실제로 움직인 양(서버 로직이든 NetworkTransform이든)을 측정합니다.
        Vector3 delta = transform.position - lastPlatformPosition;
        lastPlatformPosition = transform.position;

        // 3) 자기 소유 플레이어만 같은 양만큼 옮깁니다.
        if (delta.sqrMagnitude > 0f)
        {
            CarryOwnedPassengers(delta);
        }
    }

    private void CarryOwnedPassengers(Vector3 delta)
    {
        for (int i = passengers.Count - 1; i >= 0; i--)
        {
            Transform passenger = passengers[i];
            if (passenger == null)
            {
                passengers.RemoveAt(i);
                continue;
            }

            // 네트워크 상태에서는 내가 소유한 플레이어만 옮깁니다. (오프라인이면 전부 옮김)
            if (IsNetworkActive)
            {
                PlayerController pc = passenger.GetComponentInParent<PlayerController>();
                if (pc == null || !pc.HasInputAuthority) continue;
            }

            passenger.position += delta;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        // 위에 올라탔는지 대략 확인 (옆에서 부딪힌 경우 제외)
        if (collision.transform.position.y <= transform.position.y) return;

        // 플레이어 루트 트랜스폼을 사용 (콜라이더가 자식에 있을 수 있음)
        PlayerController pc = collision.gameObject.GetComponentInParent<PlayerController>();
        Transform passengerRoot = pc != null ? pc.transform : collision.transform;

        if (!passengers.Contains(passengerRoot))
        {
            passengers.Add(passengerRoot);
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        PlayerController pc = collision.gameObject.GetComponentInParent<PlayerController>();
        Transform passengerRoot = pc != null ? pc.transform : collision.transform;
        passengers.Remove(passengerRoot);
    }
}
