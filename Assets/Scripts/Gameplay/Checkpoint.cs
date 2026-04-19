using UnityEngine;

[DisallowMultipleComponent]
public class Checkpoint : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;

    private void Reset()
    {
        respawnPoint = transform;
    }

    private void Awake()
    {
        if (respawnPoint == null)
        {
            respawnPoint = transform;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
        {
            return;
        }

        // 체크포인트 트리거에 닿은 플레이어의 복귀 위치를 갱신한다.
        player.SetCheckpoint(respawnPoint);
    }
}
