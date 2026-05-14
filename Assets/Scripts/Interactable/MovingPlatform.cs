using System.Collections.Generic;
using UnityEngine;

// 3번: 이동 발판 장애물 (Moving Platform)
public class MovingPlatform : MonoBehaviour
{
    [Header("이동 설정")]
    public Vector3 moveOffset = new Vector3(5f, 0f, 0f); // 출발지점 기준 목표 오프셋
    public float moveSpeed = 2f;
    
    private Vector3 startPosition;
    private Vector3 targetPosition;
    private bool movingToTarget = true;
    
    private List<Transform> passengers = new List<Transform>();
    private Rigidbody rb;

    private void Start()
    {
        startPosition = transform.position;
        targetPosition = startPosition + moveOffset;
        
        // 이동형 발판은 Kinematic Rigidbody를 가져야 합니다.
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        // 목표 위치 계산
        Vector3 destination = movingToTarget ? targetPosition : startPosition;
        
        // 이동할 다음 위치
        Vector3 nextPosition = Vector3.MoveTowards(transform.position, destination, moveSpeed * Time.fixedDeltaTime);
        Vector3 deltaPosition = nextPosition - transform.position;

        // 발판 위에 있는 플레이어들의 위치를 직접 이동시킵니다. (동기화)
        for (int i = passengers.Count - 1; i >= 0; i--)
        {
            if (passengers[i] != null)
            {
                passengers[i].position += deltaPosition;
            }
            else
            {
                passengers.RemoveAt(i);
            }
        }
        
        // 발판을 트랜스폼으로 이동시켜서 물리 엔진이 플레이어를 중복으로 미는 현상 방지
        transform.position = nextPosition;

        // 도착했는지 확인
        if (Vector3.Distance(nextPosition, destination) < 0.01f)
        {
            movingToTarget = !movingToTarget; // 방향 반전
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            // 플레이어가 발판 위쪽에 있는지 대략적으로 확인 (옆에서 부딪혔을 때 방지)
            if (collision.transform.position.y > transform.position.y)
            {
                if (!passengers.Contains(collision.transform))
                {
                    passengers.Add(collision.transform);
                }
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            // 발판에서 벗어났다면 리스트에서 제거
            passengers.Remove(collision.transform);
        }
    }
}
