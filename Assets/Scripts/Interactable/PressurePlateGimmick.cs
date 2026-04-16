using UnityEngine;

public class PressurePlateGimmick : MonoBehaviour
{
    public bool isActivated = false;
    private int objectsOnPlate = 0; // 발판 위에 있는 물체의 수 카운트

    private void OnTriggerEnter(Collider other)
    {
        // 플레이어나 상자 등 지정된 대상만 체크합니다.
        if (other.CompareTag("Player") || other.CompareTag("Box"))
        {
            objectsOnPlate++;
            CheckStateChange();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Box"))
        {
            objectsOnPlate--;
            if (objectsOnPlate < 0) objectsOnPlate = 0; // 혹시 모를 음수값 방지
            CheckStateChange();
        }
    }

    // 상태가 변해야 하는지 검사 (요청 단계)
    private void CheckStateChange()
    {
        bool shouldBeActivated = objectsOnPlate > 0;

        // 현재 상태와 목표 상태가 다를 때만 실행
        if (isActivated != shouldBeActivated)
        {
            // [Netcode 훅 포인트] 
            // 나중에는 여기서 상태 변경을 서버에 요청합니다.
            ExecuteStateChange(shouldBeActivated);
        }
    }

    // 실제 상태 변경 적용 (실행 단계)
    public void ExecuteStateChange(bool newState)
    {
        isActivated = newState;

        if (isActivated)
        {
            Debug.Log($"{gameObject.name} 압력판 눌림! (문 열림 신호 등 발송)");
            // TODO: 플랫폼 작동 등 [cite: 117]
        }
        else
        {
            Debug.Log($"{gameObject.name} 압력판 복구됨!");
        }
    }
}