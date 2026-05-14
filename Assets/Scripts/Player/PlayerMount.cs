using UnityEngine;
using Unity.Netcode;

public class PlayerMount : MonoBehaviour
{
    private PlayerController playerController;
    private Rigidbody rb;
    private Collider col;
    
    [Header("마운트 상태")]
    public bool isMounted = false;
    private VehicleController currentVehicle;
    private Transform currentSeat;
    private float mountTime;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    public void Mount(VehicleController vehicle, Transform seat)
    {
        if (isMounted) return;

        // 좌석(seatPoint)이 할당되지 않았다면 임시로 차량 루트를 사용
        Transform actualSeat = seat != null ? seat : vehicle.transform;

        isMounted = true;
        currentVehicle = vehicle;
        currentSeat = actualSeat;
        mountTime = Time.time;

        // 플레이어 조작 비활성화 (Input 권한 위임)
        if (playerController != null) playerController.enabled = false;
        
        // 물리 연산 비활성화
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 모든 충돌체 비활성화 (차량과 겹침 및 트리거 오작동 방지)
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders) c.enabled = false;

        // 넷코드(Netcode) 부모 설정 대응
        // 오프라인 상태(IsSpawned = false)일 때 transform.SetParent를 부르면 NGO 내부 버그(NullReferenceException)가 발생하므로 제외합니다.
        if (TryGetComponent(out NetworkObject netObj))
        {
            if (netObj.IsSpawned)
            {
                netObj.TrySetParent(actualSeat, false);
            }
        }
        else
        {
            // NetworkObject가 아예 없는 순수 로컬 오브젝트일 경우에만 기본 부모 설정
            transform.SetParent(actualSeat);
        }

        // 월드 좌표를 좌석 위치로 즉시 이동 (0,0,0 워프 버그 방지)
        transform.position = actualSeat.position;
        transform.rotation = actualSeat.rotation;
    }

    private void LateUpdate()
    {
        // 넷코드 부모 설정이 실패했더라도 매 프레임 위치를 강제로 좌석에 고정합니다.
        if (isMounted && currentSeat != null)
        {
            transform.position = currentSeat.position;
            transform.rotation = currentSeat.rotation;
        }
    }

    public void Unmount()
    {
        if (!isMounted) return;
        
        // E키(상호작용)로 탑승할 때, 내리기 키가 같은 키라면 같은 프레임에 바로 내려지는 현상 방지
        if (Time.time - mountTime < 0.2f) return;

        // 차량 밖으로 안전하게 이동
        if (currentSeat != null && currentVehicle != null)
        {
            Vector3 carRight = currentVehicle.transform.right;
            Vector3 toSeat = currentSeat.position - currentVehicle.transform.position;
            float sideSign = Vector3.Dot(carRight, toSeat) >= 0 ? 1f : -1f;
            
            // 월드 스페이스 기준으로 확실하게 차량 중심에서 1.5m 옆으로 밀어냅니다. (스케일 무시)
            Vector3 exitPos = currentVehicle.transform.position + (carRight * sideSign * 1.5f);
            exitPos.y = currentSeat.position.y + 0.5f; 
            
            transform.position = exitPos;
            
            // [핵심 버그 수정] 물리 엔진(PhysX) 싱크 문제 해결
            // transform.position만 바꾸고 콜라이더를 켜면, 물리 엔진이 위치 변경을 1프레임 늦게 알아채서 
            // 원래 있던 좌석 위치에서 충돌 처리를 해버립니다. 이 때문에 차 지붕으로 튕겨 올라갔던 것입니다.
            if (rb != null) 
            {
                rb.position = exitPos;
            }
            Physics.SyncTransforms(); // 유니티 물리 엔진에 즉시 위치 변경을 강제 적용
        }

        isMounted = false;
        currentVehicle = null;
        currentSeat = null;

        if (TryGetComponent(out NetworkObject netObj))
        {
            if (netObj.IsSpawned)
            {
                netObj.TryRemoveParent();
            }
        }
        else
        {
            transform.SetParent(null);
        }

        // 플레이어 조작, 물리, 충돌체 원상복구
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders) c.enabled = true;
        
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = false; // PlayerController가 커스텀 중력을 사용하므로 false 유지
        }

        if (playerController != null) playerController.enabled = true;
    }
}
