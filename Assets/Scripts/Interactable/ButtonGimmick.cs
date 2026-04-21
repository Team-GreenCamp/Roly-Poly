using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class ButtonGimmick : MonoBehaviour, IInteractable
{
    public bool isActivated = false;

    [Header("버튼 연출 설정")]
    public Transform movingPart;

    [Tooltip("버튼이 눌리는 축과 방향입니다. Y축 아래로 넣으려면 (0, -1, 0), 위로 나오게 하려면 (0, 1, 0)으로 설정하세요.")]
    public Vector3 pressDirection = new Vector3(0, -1, 0);

    public float pressDistance = 0.1f;
    public float resetTime = 2.0f;
    public float moveSpeed = 2.0f;

    private Vector3 originalPos;
    private Vector3 pressedPos;

    [Header("작동 이벤트")]
    public UnityEvent onActivate;   // 눌렸을 때 실행할 일
    public UnityEvent onDeactivate; // 튀어나올 때 실행할 일

    private void Start()
    {
        if (movingPart != null)
        {
            originalPos = movingPart.localPosition;

            // 설정한 축(pressDirection)을 기준으로 거리(pressDistance)만큼 이동한 목표 위치를 잡습니다.
            pressedPos = originalPos + (pressDirection.normalized * pressDistance);
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] 버튼의 Moving Part가 할당되지 않았습니다!");
        }
    }

    public void RequestInteract(GameObject interactor)
    {
        if (isActivated) return;

        ExecuteInteract(interactor);
    }

    public void ExecuteInteract(GameObject interactor)
    {
        isActivated = true;
        Debug.Log($"🔲 {interactor.name}이(가) [{gameObject.name}] 버튼을 눌렀습니다! ({resetTime}초 뒤 복구)");

        onActivate.Invoke();

        if (movingPart != null)
        {
            StartCoroutine(ButtonPressRoutine());
        }
    }

    private IEnumerator ButtonPressRoutine()
    {
        // 1. 설정한 축 방향으로 버튼 밀어넣기
        while (Vector3.Distance(movingPart.localPosition, pressedPos) > 0.001f)
        {
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, pressedPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        movingPart.localPosition = pressedPos;

        // 2. 대기
        yield return new WaitForSeconds(resetTime);

        // 3. 원래 위치(축)로 복구
        while (Vector3.Distance(movingPart.localPosition, originalPos) > 0.001f)
        {
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, originalPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        movingPart.localPosition = originalPos;

        isActivated = false;
        Debug.Log($"🔲 [{gameObject.name}] 버튼이 원상복구 되었습니다.");

        onDeactivate.Invoke();
    }
}