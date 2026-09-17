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

        // 리스폰은 소유자(본인 클라이언트)가 자기 위치를 동기화하는 방식이므로,
        // 체크포인트 기록도 해당 플레이어를 소유한 클라이언트에서만 수행한다.
        if (!player.HasInputAuthority)
        {
            return;
        }

        // 체크포인트 트리거에 닿은 플레이어의 복귀 위치를 갱신한다.
        player.SetCheckpoint(respawnPoint);
    }
}
