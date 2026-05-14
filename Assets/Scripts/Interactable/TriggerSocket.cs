using UnityEngine;
using UnityEngine.Events;

public class TriggerSocket : MonoBehaviour
{
    [Header("소켓 설정")]
    public string targetTag = "RollingBall"; // 굴러오는 공(목표물)의 태그
    
    [Header("이벤트 (퍼즐 완료 시 실행할 동작들)")]
    public UnityEvent onTargetEntered;
    public UnityEvent onTargetExited;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            Debug.Log($"[TriggerSocket] {other.name} 이(가) 목표 지점(소켓)에 도달했습니다!");
            onTargetEntered?.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            onTargetExited?.Invoke();
        }
    }
}
