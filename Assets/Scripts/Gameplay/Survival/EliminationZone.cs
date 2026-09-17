using UnityEngine;

// 아레나 아래에 배치하는 낙사 판정 존 (서바이벌 전용, 非네트워크 — RespawnHazard 패턴).
// 각 플레이어의 소유자 클라이언트가 자기 낙하만 감지해 SurvivalGameManager에 보고하고,
// 실제 탈락 확정은 서버가 한다. 원격 프록시(HasInputAuthority=false)는 보고하지 않는다.
[RequireComponent(typeof(Collider))]
public class EliminationZone : MonoBehaviour
{
    private void Reset()
    {
        Collider zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null)
        {
            zoneCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !player.HasInputAuthority)
        {
            return;
        }

        // 중복 트리거(여러 콜라이더, 탈락 후 체류)는 서버의 생존자 목록 검사로 자연 무시된다.
        if (SurvivalGameManager.Instance != null)
        {
            SurvivalGameManager.Instance.ReportLocalPlayerFell();
        }
    }
}
