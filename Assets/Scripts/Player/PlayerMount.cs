using UnityEngine;

public class PlayerMount : MonoBehaviour
{
    private PlayerController playerController;
    private Rigidbody rb;
    private Collider col;
    
    [Header("마운트 상태")]
    public bool isMounted = false;
    private VehicleController currentVehicle;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    public void Mount(VehicleController vehicle, Transform seat)
    {
        if (isMounted) return;

        isMounted = true;
        currentVehicle = vehicle;

        // 플레이어 조작 비활성화 (Input 권한 위임)
        if (playerController != null) playerController.enabled = false;
        
        // 물리 연산 비활성화
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
        }

        // 충돌체 비활성화 (차량과 겹침 방지)
        if (col != null) col.enabled = false;

        // 운전석 위치로 고정
        transform.SetParent(seat);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void Unmount()
    {
        if (!isMounted) return;

        isMounted = false;
        currentVehicle = null;

        transform.SetParent(null);

        // 플레이어 조작 및 물리 원상복구
        if (col != null) col.enabled = true;
        
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        if (playerController != null) playerController.enabled = true;
    }
}
