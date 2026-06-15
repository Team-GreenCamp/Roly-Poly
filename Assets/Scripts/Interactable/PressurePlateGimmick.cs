using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

// 서버 권한 기믹 패턴(압력판). 표준 설명은 LeverGimmick.cs 참고.
// 차이점: 버튼/레버처럼 "요청"이 아니라 트리거 감지로 동작하므로, "서버만" 판 위 물체 수를 세고
//         활성 상태를 NetworkVariable로 모든 클라이언트에 전파합니다.
// (호스트에는 모든 플레이어 프록시와 서버 권한 박스가 올바른 위치에 있으므로 서버 트리거 감지가 성립합니다.)
[RequireComponent(typeof(NetworkObject))]
public class PressurePlateGimmick : NetworkBehaviour
{
    [Header("발판 연출 설정")]
    [Tooltip("실제로 오르락내리락 할 자식 오브젝트(발판 뚜껑)를 연결하세요.")]
    public Transform movingPart;

    [Tooltip("발판이 눌리는 축과 방향입니다. Y축 아래로 눌리게 하려면 (0, -1, 0)")]
    public Vector3 pressDirection = new Vector3(0, -1, 0);

    public float pressDistance = 0.1f;
    public float moveSpeed = 5.0f;

    [Header("상태")]
    public bool isActivated = false;
    private int objectsOnPlate = 0; // 발판 위 물체 수 (네트워크 시 서버에서만 카운트)

    private Vector3 originalPos;
    private Vector3 pressedPos;
    private Vector3 targetPos;

    [Header("작동 이벤트")]
    public UnityEvent onActivate;
    public UnityEvent onDeactivate;

    private readonly NetworkVariable<bool> networkActivated =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        if (movingPart != null)
        {
            originalPos = movingPart.localPosition;
            pressedPos = originalPos + (pressDirection.normalized * pressDistance);
            targetPos = originalPos;
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] 발판의 Moving Part가 할당되지 않았습니다!");
        }
    }

    public override void OnNetworkSpawn()
    {
        networkActivated.OnValueChanged += HandleActivatedChanged;

        // 늦게 들어온 클라이언트도 현재 상태로 맞춥니다. (스냅)
        isActivated = networkActivated.Value;
        targetPos = isActivated ? pressedPos : originalPos;
        if (movingPart != null) movingPart.localPosition = targetPos;
    }

    public override void OnNetworkDespawn()
    {
        networkActivated.OnValueChanged -= HandleActivatedChanged;
    }

    private void Update()
    {
        // 연출은 모든 클라이언트에서 동일하게: 목표 위치로 부드럽게 이동
        if (movingPart != null && movingPart.localPosition != targetPos)
        {
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, targetPos, moveSpeed * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!(other.CompareTag("Player") || other.CompareTag("Box"))) return;

        if (!IsNetworkActive)
        {
            // 오프라인/네트워크 미구성: 로컬에서 카운트
            objectsOnPlate++;
            UpdatePlateState();
            return;
        }

        if (!IsServer) return; // 네트워크 시 카운트는 서버 권한
        objectsOnPlate++;
        UpdatePlateState();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!(other.CompareTag("Player") || other.CompareTag("Box"))) return;

        if (!IsNetworkActive)
        {
            objectsOnPlate = Mathf.Max(0, objectsOnPlate - 1);
            UpdatePlateState();
            return;
        }

        if (!IsServer) return;
        objectsOnPlate = Mathf.Max(0, objectsOnPlate - 1);
        UpdatePlateState();
    }

    // 서버(또는 오프라인 로컬)에서 현재 카운트로 활성 상태를 갱신합니다.
    private void UpdatePlateState()
    {
        bool shouldBeActivated = objectsOnPlate > 0;

        if (IsNetworkActive)
        {
            // 서버만 상태를 바꾸고, 연출/이벤트는 OnValueChanged에서 모두가 실행합니다.
            if (networkActivated.Value != shouldBeActivated)
            {
                networkActivated.Value = shouldBeActivated;
            }
            return;
        }

        if (isActivated != shouldBeActivated)
        {
            ApplyActivated(shouldBeActivated);
        }
    }

    private void HandleActivatedChanged(bool previousValue, bool newValue)
    {
        ApplyActivated(newValue);
    }

    private void ApplyActivated(bool activated)
    {
        isActivated = activated;
        targetPos = activated ? pressedPos : originalPos;

        if (activated)
        {
            Debug.Log($"⬇️ [{gameObject.name}] 발판이 눌렸습니다!");
            onActivate.Invoke();
        }
        else
        {
            Debug.Log($"⬆️ [{gameObject.name}] 발판이 원상복구 되었습니다.");
            onDeactivate.Invoke();
        }
    }
}
