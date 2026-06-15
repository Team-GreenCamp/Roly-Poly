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

        // 리스폰 텔레포트는 소유자가 자기 위치를 동기화하므로, 해당 플레이어를 소유한 클라이언트에서만 처리한다.
        // (원격 프록시에서 RespawnAtCheckpoint를 부르면 동기화된 위치를 덮어써 충돌이 생긴다.)
        if (!player.HasInputAuthority)
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
