using UnityEngine;
using UnityEngine.Events;

public class PressurePlateGimmick : MonoBehaviour
{
    [Header("발판 연출 설정")]
    [Tooltip("실제로 오르락내리락 할 자식 오브젝트(발판 뚜껑)를 연결하세요.")]
    public Transform movingPart;

    [Tooltip("발판이 눌리는 축과 방향입니다. Y축 아래로 눌리게 하려면 (0, -1, 0)")]
    public Vector3 pressDirection = new Vector3(0, -1, 0);

    public float pressDistance = 0.1f;
    public float moveSpeed = 5.0f; // 버튼보다 약간 빠르게 설정하는 것이 밟는 맛이 좋습니다.

    [Header("상태")]
    public bool isActivated = false;
    private int objectsOnPlate = 0; // 발판 위에 있는 물체의 수

    private Vector3 originalPos;
    private Vector3 pressedPos;
    private Vector3 targetPos;

    [Header("작동 이벤트")]
    public UnityEvent onActivate;
    public UnityEvent onDeactivate;

    private void Start()
    {
        if (movingPart != null)
        {
            originalPos = movingPart.localPosition;
            pressedPos = originalPos + (pressDirection.normalized * pressDistance);

            // 처음 목표 위치는 원래 위치(위쪽)로 설정합니다.
            targetPos = originalPos;
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] 발판의 Moving Part가 할당되지 않았습니다!");
        }
    }

    private void Update()
    {
        // 매 프레임마다 movingPart를 현재 targetPos를 향해 부드럽게 이동시킵니다.
        if (movingPart != null && movingPart.localPosition != targetPos)
        {
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, targetPos, moveSpeed * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 밟은 오브젝트의 태그가 Player이거나 Box일 때만 인식합니다.
        if (other.CompareTag("Player") || other.CompareTag("Box"))
        {
            objectsOnPlate++;
            UpdatePlateState();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Box"))
        {
            objectsOnPlate--;
            if (objectsOnPlate < 0) objectsOnPlate = 0; // 오류 방지용 최소값 고정
            UpdatePlateState();
        }
    }

    private void UpdatePlateState()
    {
        // 발판 위에 물체가 1개라도 있다면 활성화되어야 합니다.
        bool shouldBeActivated = objectsOnPlate > 0;

        // 상태가 변했을 때만 실행합니다.
        if (isActivated != shouldBeActivated)
        {
            isActivated = shouldBeActivated;

            if (isActivated)
            {
                Debug.Log($"⬇️ [{gameObject.name}] 발판이 눌렸습니다! (현재 위 숫자: {objectsOnPlate})");
                targetPos = pressedPos; // 목표 위치를 아래로 변경

                onActivate.Invoke();
            }
            else
            {
                Debug.Log($"⬆️ [{gameObject.name}] 발판이 원상복구 되었습니다. (현재 위 숫자: {objectsOnPlate})");
                targetPos = originalPos; // 목표 위치를 위로 변경

                onDeactivate.Invoke();
            }
        }
    }
}