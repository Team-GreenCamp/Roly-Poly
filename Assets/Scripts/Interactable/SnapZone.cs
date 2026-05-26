using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class SnapZone : MonoBehaviour
{
    [Header("스냅 설정")]
    [Tooltip("스냅할 오브젝트의 태그 (비어있으면 모든 Grabbable 허용)")]
    public string targetId = "Plank"; 
    public Transform snapPoint;
    public float snapDuration = 0.3f;
    public float snapDistance = 1.0f;

    [Header("시각적 피드백")]
    [Tooltip("오브젝트가 근처에 있을 때 보여줄 고스트 메시 (선택 사항)")]
    public GameObject ghostPreview;

    [Header("이벤트")]
    public UnityEvent onSnapped;
    public UnityEvent onUnsnapped;

    private GrabbableObject snappedObject;
    public bool IsOccupied => snappedObject != null;

    private void Awake()
    {
        if (ghostPreview != null) ghostPreview.SetActive(false);
        if (snapPoint == null) snapPoint = transform;
    }

    public bool CanSnap(GrabbableObject grabbable)
    {
        if (IsOccupied || grabbable == null) return false;
        
        // 특정 태그 검사
        bool tagMatch = string.IsNullOrEmpty(targetId) || grabbable.CompareTag(targetId);
        return tagMatch;
    }

    public void SnapObject(Rigidbody targetBody)
    {
        if (IsOccupied) return;

        GrabbableObject grabbable = targetBody.GetComponent<GrabbableObject>();
        if (grabbable != null && !grabbable.IsBeingHeld)
        {
            snappedObject = grabbable;
            // 스냅된 상태임을 오브젝트에 알림
            StartCoroutine(SnapCoroutine(targetBody));
            onSnapped?.Invoke();
            
            if (ghostPreview != null) ghostPreview.SetActive(false);
        }
    }

    private IEnumerator SnapCoroutine(Rigidbody targetBody)
    {
        targetBody.isKinematic = true;
        targetBody.useGravity = false;
        
        // 물리 엔진 충돌 방해를 최소화하기 위해 잠시 트리거로 변경하거나 레이어 조정 가능
        // 여기서는 기본적으로 kinematic 처리를 신뢰합니다.

        Vector3 startPos = targetBody.transform.position;
        Quaternion startRot = targetBody.transform.rotation;
        
        Vector3 targetPos = snapPoint.position;
        Quaternion targetRot = snapPoint.rotation;

        float elapsedTime = 0f;
        while (elapsedTime < snapDuration)
        {
            if (snappedObject == null) yield break; // 중간에 탈출(그럴 일은 거의 없지만)

            float t = elapsedTime / snapDuration;
            // 부드러운 가속/감속 커브
            t = Mathf.SmoothStep(0, 1, t);
            
            targetBody.transform.position = Vector3.Lerp(startPos, targetPos, t);
            targetBody.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        targetBody.transform.position = targetPos;
        targetBody.transform.rotation = targetRot;
    }

    public void ReleaseObject()
    {
        if (snappedObject != null)
        {
            snappedObject = null;
            onUnsnapped?.Invoke();
        }
    }

    private void Update()
    {
        // 만약 스냅된 물체를 누군가 다시 잡았다면 즉시 스냅 상태 해제
        if (IsOccupied && snappedObject.IsBeingHeld)
        {
            ReleaseObject();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsOccupied) return;

        GrabbableObject grabbable = other.GetComponentInParent<GrabbableObject>();
        if (grabbable != null && CanSnap(grabbable))
        {
            if (ghostPreview != null) ghostPreview.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        GrabbableObject grabbable = other.GetComponentInParent<GrabbableObject>();
        if (grabbable != null)
        {
            if (ghostPreview != null) ghostPreview.SetActive(false);
            
            // 만약 스냅되어 있던 물체가 나간 것이라면 (플레이어가 다시 집어갔을 때)
            if (grabbable == snappedObject)
            {
                ReleaseObject();
            }
        }
    }
}
