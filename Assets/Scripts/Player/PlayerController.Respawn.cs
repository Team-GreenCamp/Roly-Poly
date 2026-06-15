using UnityEngine;

public partial class PlayerController
{
    public void SetCheckpoint(Transform checkpointPoint)
    {
        if (checkpointPoint != null)
        {
            currentCheckpoint = checkpointPoint;
        }
    }

    public void RespawnAtCheckpoint()
    {
        Vector3 spawnPosition = currentCheckpoint != null ? currentCheckpoint.position : initialSpawnPosition;
        Quaternion spawnRotation = currentCheckpoint != null ? currentCheckpoint.rotation : initialSpawnRotation;

        // 물리 연산 초기화 및 위치 즉시 이동
        if (physicsBody != null)
        {
            physicsBody.position = spawnPosition;
            physicsBody.rotation = spawnRotation;
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
        }

        transform.position = spawnPosition;
        transform.rotation = spawnRotation;

        currentHorizontalVelocity = Vector3.zero;
        lastVerticalVelocity = 0f;
        isKnockedDown = false;
        knockdownTimer = 0f;

        // 낙사 시 들고 있던 물건을 놓도록 PlayerInteractor가 있다면 비활성화 후 활성화하거나 내부 상태를 초기화할 수 있음
        PlayerInteractor interactor = GetComponent<PlayerInteractor>();
        if (interactor != null)
        {
            // PlayerInteractor의 OnDisable에서 놓기가 호출되므로 껐다 켜서 들고 있던 걸 초기화
            interactor.enabled = false;
            interactor.enabled = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector3 spherePosition = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawWireSphere(spherePosition, groundedRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + (transform.forward * 2.2f));

        Gizmos.color = Color.blue;
        Vector3 normalOrigin = transform.position + Vector3.up * groundedOffset;
        Gizmos.DrawLine(normalOrigin, normalOrigin + (groundNormal.normalized * 1.2f));
    }
}
