using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : MonoBehaviour, IInteractable
{
    [Header("오브젝트 설정")]
    [Tooltip("체크하면 무거운 물체(끌기용), 체크 해제하면 가벼운 물체(들기용)")]
    public bool isHeavy = false;

    private Rigidbody rb;
    private Collider col;
    private SpringJoint dragJoint;
    private Collider playerCollider; // 플레이어 충돌 무시용

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    public void RequestInteract(GameObject interactor)
    {
        PlayerInteractor player = interactor.GetComponent<PlayerInteractor>();
        if (player != null)
        {
            player.Grab(this);
        }
    }

    public void PickUp(Transform holdPoint, Transform dragPoint)
    {
        if (isHeavy)
        {
            // 무거운 상자: 물리 연산 유지
            rb.isKinematic = false; 
            col.enabled = true;   

            if (dragPoint != null)
            {
                // 플레이어와의 충돌을 무시하여 덜덜거리거나 튕겨나가는 현상 완벽 차단
                playerCollider = dragPoint.GetComponentInParent<Collider>();
                if (playerCollider != null)
                {
                    Physics.IgnoreCollision(col, playerCollider, true);
                }

                Rigidbody dragRb = dragPoint.GetComponent<Rigidbody>();
                if (dragRb == null)
                {
                    dragRb = dragPoint.gameObject.AddComponent<Rigidbody>();
                    dragRb.isKinematic = true;
                }

                // 다시 스프링 조인트 사용 (바닥에 닿아서 끌고 다니는 느낌 복구)
                dragJoint = gameObject.AddComponent<SpringJoint>();
                dragJoint.connectedBody = dragRb;
                
                dragJoint.autoConfigureConnectedAnchor = false;
                dragJoint.connectedAnchor = Vector3.zero;
                
                dragJoint.spring = 200f; // 끌어당기는 힘
                dragJoint.damper = 20f;  // 덜렁거림 방지
                dragJoint.maxDistance = 0.5f; 
            }

            // 들림 방지를 위해 속도 강제 초기화
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Debug.Log("📦 무거운 상자를 완벽히 고정하여 끕니다!");
        }
        else
        {
            // 가벼운 상자: 물리 연산 중지 (캐릭터와 충돌 방지)
            rb.isKinematic = true; 
            col.enabled = false;   

            // HoldPoint 위치로 이동 및 고정
            if (holdPoint != null)
            {
                transform.SetParent(holdPoint);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity; 
            }
            Debug.Log("📦 가벼운 상자를 들었습니다!");
        }
    }

    public void Drop()
    {
        if (isHeavy)
        {
            if (dragJoint != null)
            {
                Destroy(dragJoint);
            }
            
            // 플레이어 충돌 무시 원상복구
            if (playerCollider != null)
            {
                Physics.IgnoreCollision(col, playerCollider, false);
                playerCollider = null;
            }

            rb.isKinematic = false;
            try 
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            } catch { }
            
            Debug.Log("📦 무거운 상자를 내려놓았습니다.");
        }
        else
        {
            transform.SetParent(null);
            
            // 물리 엔진 버그(콜라이더 겹침으로 인한 굳음) 방지를 위해, 놓을 때 머리 위쪽으로 살짝 빼줍니다.
            transform.position += Vector3.up * 0.5f;

            // 플레이어 충돌 무시 원상복구 (혹시 모를 상황 대비)
            if (playerCollider != null)
            {
                Physics.IgnoreCollision(col, playerCollider, false);
                playerCollider = null;
            }
            
            col.enabled = true;   
            rb.isKinematic = false; 

            try 
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            } catch { }
            
            Debug.Log("📦 가벼운 상자를 내려놓았습니다.");
        }
    }
}