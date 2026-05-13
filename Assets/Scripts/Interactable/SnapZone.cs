using UnityEngine;
using System.Collections;

public class SnapZone : MonoBehaviour
{
    [Header("스냅 설정")]
    public string targetId = "Plank"; // 특정 태그 등 검사용
    public Transform snapPoint;
    public float snapDuration = 0.5f;

    private bool isOccupied = false;

    public bool CanSnap(GrabbableObject grabbable)
    {
        if (isOccupied || grabbable == null) return false;
        
        // 예시: 특정 태그를 가졌을 때만 스냅 허용
        // 만약 태그 상관없이 무조건 스냅하려면 return true;
        return grabbable.CompareTag(targetId) || string.IsNullOrEmpty(targetId);
    }

    public void SnapObject(Rigidbody targetBody)
    {
        isOccupied = true;
        StartCoroutine(SnapCoroutine(targetBody));
    }

    private IEnumerator SnapCoroutine(Rigidbody targetBody)
    {
        targetBody.isKinematic = true;
        targetBody.useGravity = false;

        Vector3 startPos = targetBody.transform.position;
        Quaternion startRot = targetBody.transform.rotation;
        
        Vector3 targetPos = snapPoint != null ? snapPoint.position : transform.position;
        Quaternion targetRot = snapPoint != null ? snapPoint.rotation : transform.rotation;

        float elapsedTime = 0f;
        while (elapsedTime < snapDuration)
        {
            float t = elapsedTime / snapDuration;
            // SmoothStep으로 부드럽게 감속하며 스냅
            t = t * t * (3f - 2f * t);
            
            targetBody.transform.position = Vector3.Lerp(startPos, targetPos, t);
            targetBody.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        targetBody.transform.position = targetPos;
        targetBody.transform.rotation = targetRot;
    }
}
