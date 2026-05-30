using System.Collections;
using UnityEngine;

// ⭐ IInteractable 인터페이스를 상속받아 플레이어의 레이저(레이캐스트)에 반응하게 만듭니다.
public class DoorController : MonoBehaviour, IInteractable
{
    public enum DoorType { Slide, Rotate }

    [Header("문 설정")]
    public DoorType doorType = DoorType.Slide;
    public float moveSpeed = 3f;
    [Tooltip("Slide는 X축, Rotate는 Y축 변경")]
    public Vector3 openOffset;

    [Header("상호작용 설정")]
    [Tooltip("체크하면 플레이어가 다가가서 직접 E키로 열고 닫을 수 있습니다.\n체크 해제하면 버튼이나 레버 같은 외부 스위치로만 열립니다.")]
    public bool canDirectInteract = false;

    [Header("열쇠 잠금 설정")]
    [Tooltip("체크하면 문을 열기 위해 특정 열쇠 오브젝트가 필요합니다.")]
    public bool needsKey = false;
    [Tooltip("열쇠로 인식할 오브젝트의 태그입니다.")]
    public string keyTag = "Key";

    private bool isLocked = false; // 현재 문이 잠겨있는지 여부

    [Header("자동 반복 타이머 (Auto Timing Trap)")]
    [Tooltip("체크하면 설정한 시간에 맞춰 자동으로 열리고 닫히기를 무한 반복합니다.")]
    public bool isAutoLoop = false;
    public float openDuration = 3f;
    public float closeDuration = 2f;

    private bool isOpen = false; // 현재 문이 열려있는지 상태 저장

    private Vector3 closedPosition;
    private Vector3 openPosition;
    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Coroutine moveCoroutine;

    private void Start()
    {
        closedPosition = transform.localPosition;
        closedRotation = transform.localRotation;

        if (doorType == DoorType.Slide)
        {
            openPosition = closedPosition + openOffset;
        }
        else
        {
            openRotation = closedRotation * Quaternion.Euler(openOffset);
        }

        // ⭐ 자동 루프가 켜져있다면 코루틴 시작
        if (isAutoLoop)
        {
            StartCoroutine(AutoLoopRoutine());
        }

        // 열쇠가 필요한 문이면 초기 상태를 잠금 상태로 설정합니다.
        if (needsKey)
        {
            isLocked = true;
        }
    }

    // ⭐ 플레이어가 문을 향해 E키를 눌렀을 때 실행되는 함수
    public void RequestInteract(GameObject interactor)
    {
        // 🔑 열쇠가 필요한 문이고 아직 잠겨있는 상태라면
        if (needsKey && isLocked)
        {
            PlayerInteractor playerInteractor = interactor.GetComponent<PlayerInteractor>();
            if (playerInteractor != null)
            {
                GrabbableObject heldObj = playerInteractor.CurrentHeldGrabbable;
                if (heldObj != null && heldObj.CompareTag(keyTag))
                {
                    Debug.Log($"🔑 [{gameObject.name}] {interactor.name}이(가) 열쇠({keyTag})를 사용하여 문을 열었습니다!");
                    
                    // 열쇠 소모
                    playerInteractor.ConsumeHeldObject();
                    isLocked = false;
                    
                    // 문 열기
                    OpenDoor();
                    return;
                }
            }

            Debug.Log($"🔒 [{gameObject.name}] 문이 잠겨있습니다! 열쇠({keyTag})가 필요합니다.");
            return;
        }

        // 직접 열 수 없는 문이라면 거절
        if (!canDirectInteract)
        {
            Debug.Log($"🔒 [{gameObject.name}] 문은 잠겨있거나 다른 장치(스위치)로 열어야 합니다!");
            // TODO: '잠김' 사운드 재생 또는 UI 텍스트 띄우기
            return;
        }

        // 직접 열 수 있다면 현재 상태의 반대로 작동 (토글)
        if (isOpen)
            CloseDoor();
        else
            OpenDoor();
    }

    // 외부 스위치(버튼, 레버)나 내부 토글에서 호출할 수 있는 함수
    public void OpenDoor()
    {
        if (isOpen) return; // 이미 열려있으면 무시
        isOpen = true;

        if (moveCoroutine != null) StopCoroutine(moveCoroutine);

        if (doorType == DoorType.Slide)
            moveCoroutine = StartCoroutine(MoveRoutine(openPosition));
        else
            moveCoroutine = StartCoroutine(RotateRoutine(openRotation));
    }

    public void CloseDoor()
    {
        if (!isOpen) return; // 이미 닫혀있으면 무시
        isOpen = false;

        if (moveCoroutine != null) StopCoroutine(moveCoroutine);

        if (doorType == DoorType.Slide)
            moveCoroutine = StartCoroutine(MoveRoutine(closedPosition));
        else
            moveCoroutine = StartCoroutine(RotateRoutine(closedRotation));
    }

    private IEnumerator MoveRoutine(Vector3 targetPos)
    {
        while (Vector3.Distance(transform.localPosition, targetPos) > 0.001f)
        {
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, targetPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.localPosition = targetPos;
    }

    private IEnumerator RotateRoutine(Quaternion targetRot)
    {
        while (Quaternion.Angle(transform.localRotation, targetRot) > 0.1f)
        {
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.localRotation = targetRot;
    }

    // ⭐ 3초 열리고 2초 닫히는 무한 루프 코루틴
    private IEnumerator AutoLoopRoutine()
    {
        while (true)
        {
            OpenDoor();
            yield return new WaitForSeconds(openDuration);

            CloseDoor();
            yield return new WaitForSeconds(closeDuration);
        }
    }
}