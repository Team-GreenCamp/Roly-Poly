using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class LeverGimmick : MonoBehaviour, IInteractable
{
    [Header("레버 상태")]
    public bool isOn = false; // 현재 레버가 켜져 있는지 여부

    [Header("레버 연출 설정")]
    public Transform handle; // 돌아갈 막대기 부분
    public Vector3 offRotation = new Vector3(-30, 0, 0);   // 꺼졌을 때 각도
    public Vector3 onRotation = new Vector3(30, 0, 0); // 켜졌을 때 각도 (X축으로 30도 젖힘)
    public float rotateSpeed = 5f;

    [Header("작동 이벤트")]
    public UnityEvent onToggleOn;  // 켰을 때 실행할 일
    public UnityEvent onToggleOff; // 껐을 때 실행할 일

    private Coroutine rotateCoroutine;

    public void RequestInteract(GameObject interactor)
    {
        // 상태를 뒤집습니다. (On -> Off, Off -> On)
        isOn = !isOn;

        if (isOn)
        {
            Debug.Log($"🎚️ {interactor.name}이(가) [{gameObject.name}] 레버를 켰습니다!");
            onToggleOn.Invoke(); // 켜짐 신호 발송
        }
        else
        {
            Debug.Log($"🎚️ {interactor.name}이(가) [{gameObject.name}] 레버를 껐습니다!");
            onToggleOff.Invoke(); // 꺼짐 신호 발송
        }

        // 손잡이 회전 연출 실행
        if (handle != null)
        {
            if (rotateCoroutine != null) StopCoroutine(rotateCoroutine);
            rotateCoroutine = StartCoroutine(RotateHandleRoutine(isOn ? onRotation : offRotation));
        }
    }

    private IEnumerator RotateHandleRoutine(Vector3 targetEulerAngles)
    {
        Quaternion targetRotation = Quaternion.Euler(targetEulerAngles);

        // 목표 각도에 도달할 때까지 부드럽게 회전
        while (Quaternion.Angle(handle.localRotation, targetRotation) > 0.01f)
        {
            handle.localRotation = Quaternion.Slerp(handle.localRotation, targetRotation, rotateSpeed * Time.deltaTime);
            yield return null;
        }
        handle.localRotation = targetRotation;
    }
}