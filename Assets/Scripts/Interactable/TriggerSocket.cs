using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class TriggerSocket : MonoBehaviour
{
    [Header("소켓 설정")]
    [Tooltip("감지할 오브젝트의 태그")]
    public string targetTag = "RollingBall"; 
    
    [Header("스냅 설정 (선택 사항)")]
    [Tooltip("체크하면 오브젝트가 트리거에 들어왔을 때 중심으로 자동 스냅됩니다.")]
    public bool useAutoSnap = false;
    public Transform snapPoint;
    public float snapDuration = 0.5f;

    [Header("이벤트")]
    public UnityEvent onTargetEntered;
    public UnityEvent onTargetExited;

    private GameObject currentTarget;
    public bool IsOccupied => currentTarget != null;

    private void Awake()
    {
        if (snapPoint == null) snapPoint = transform;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsOccupied) return;

        if (other.CompareTag(targetTag))
        {
            currentTarget = other.gameObject;
            Debug.Log($"[TriggerSocket] {other.name} 이(가) 목표 지점(소켓)에 도달했습니다!");
            
            onTargetEntered?.Invoke();

            if (useAutoSnap)
            {
                Rigidbody rb = other.attachedRigidbody;
                if (rb != null)
                {
                    StartCoroutine(SnapToCenter(rb));
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject == currentTarget)
        {
            currentTarget = null;
            onTargetExited?.Invoke();
        }
    }

    private IEnumerator SnapToCenter(Rigidbody rb)
    {
        // 스냅 중에는 물리 영향을 최소화하거나 멈춤
        rb.isKinematic = true;
        
        Vector3 startPos = rb.position;
        Quaternion startRot = rb.rotation;
        
        float elapsedTime = 0f;
        while (elapsedTime < snapDuration)
        {
            if (currentTarget == null) yield break;

            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsedTime / snapDuration);
            
            rb.position = Vector3.Lerp(startPos, snapPoint.position, t);
            rb.rotation = Quaternion.Slerp(startRot, snapPoint.rotation, t);
            
            yield return null;
        }

        rb.position = snapPoint.position;
        rb.rotation = snapPoint.rotation;
    }
}
