using System.Collections;
using UnityEngine;

// 7번: 밟으면 떨어지는/부서지는 발판 (Falling Platform)
public class FallingPlatform : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("플레이어가 밟고 나서 떨어질 때까지의 대기 시간 (초)")]
    public float fallDelay = 1f; 
    
    [Tooltip("떨어지고 나서 다시 원래 위치로 복귀하는 시간 (초). 0으로 설정하면 영원히 안 생깁니다.")]
    public float respawnDelay = 3f; 
    
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Rigidbody rb;
    private bool isFalling = false;

    private void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        
        // Rigidbody를 Kinematic으로 설정하여 안정적인 발판으로 만듭니다.
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 떨어지는 중이 아니고 플레이어와 닿았을 때
        if (!isFalling && collision.gameObject.CompareTag("Player"))
        {
            // 플레이어가 발판 위에 탔을 때 (위에서 밟았을 때만 떨어짐)
            if (collision.transform.position.y > transform.position.y)
            {
                StartCoroutine(FallRoutine());
            }
        }
    }

    private IEnumerator FallRoutine()
    {
        isFalling = true;
        
        // 1. 떨어지기 전 대기 시간
        yield return new WaitForSeconds(fallDelay);
        
        // 2. Kinematic을 해제하고 중력을 켜서 추락시킵니다.
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // 3. 리스폰 설정이 있다면 다시 생성
        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);
            
            // 물리 및 위치 초기화: 다시 Kinematic 발판으로 되돌립니다.
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            
            isFalling = false;
        }
    }
}
