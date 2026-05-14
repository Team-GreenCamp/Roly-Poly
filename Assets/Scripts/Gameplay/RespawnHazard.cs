using UnityEngine;

[DisallowMultipleComponent]
public class RespawnHazard : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
        {
            return;
        }

        // 탑승 중인 플레이어는 리스폰 위험 구역 트리거를 무시합니다.
        PlayerMount mount = player.GetComponent<PlayerMount>();
        if (mount != null && mount.isMounted)
        {
            return;
        }

        // 낙사 영역이나 위험 구역에 닿으면 최근 체크포인트로 되돌린다.
        player.RespawnAtCheckpoint();
    }
}
