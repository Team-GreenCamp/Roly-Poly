using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using Unity.Netcode;

// 서버 권한 소켓. 태그가 맞는 오브젝트(예: 굴러온 공)가 들어오면 점유 상태가 되고 이벤트가 발생합니다.
// 굴러오는 공 등은 서버 권한 NetworkTransform을 쓰므로 "서버에서" 트리거가 정확히 잡힙니다.
// 동기화하려면 NetworkObject가 필요합니다. (없으면 기존처럼 로컬 처리)
[RequireComponent(typeof(NetworkObject))]
public class TriggerSocket : NetworkBehaviour
{
    [Header("소켓 설정")]
    [Tooltip("감지할 오브젝트의 태그")]
    public string targetTag = "RollingBall";

    [Header("스냅 설정 (선택 사항)")]
    [Tooltip("체크하면 오브젝트가 트리거에 들어왔을 때 중심으로 자동 스냅됩니다.")]
    public bool useAutoSnap = false;
    public Transform snapPoint;
    public float snapDuration = 0.5f;

    [Header("이벤트")]
    public UnityEvent onTargetEntered;
    public UnityEvent onTargetExited;

    private GameObject currentTarget; // 권한 측 참조
    private Coroutine snapCoroutine;

    private readonly NetworkVariable<bool> networkOccupied =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;
    private bool HasAuthority => !IsNetworkActive || IsServer;

    public bool IsOccupied => IsNetworkActive ? networkOccupied.Value : currentTarget != null;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);
        if (snapPoint == null) snapPoint = transform;
    }

    public override void OnNetworkSpawn()
    {
        networkOccupied.OnValueChanged += HandleOccupiedChanged;
    }

    public override void OnNetworkDespawn()
    {
        networkOccupied.OnValueChanged -= HandleOccupiedChanged;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 점유 판정은 권한(서버 또는 오프라인) 측에서만.
        if (!HasAuthority || IsOccupied) return;

        if (other.CompareTag(targetTag))
        {
            currentTarget = other.gameObject;
            Debug.Log($"[TriggerSocket] {other.name} 이(가) 목표 지점(소켓)에 도달했습니다!");

            if (IsNetworkActive)
            {
                networkOccupied.Value = true; // OnValueChanged가 모두에게 이벤트 발생
            }
            else
            {
                onTargetEntered?.Invoke();
            }

            if (useAutoSnap)
            {
                Rigidbody rb = other.attachedRigidbody;
                if (rb != null)
                {
                    if (snapCoroutine != null) StopCoroutine(snapCoroutine);
                    snapCoroutine = StartCoroutine(SnapToCenter(rb));
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!HasAuthority) return;

        if (other.gameObject == currentTarget)
        {
            currentTarget = null;

            if (IsNetworkActive)
            {
                if (IsServer && networkOccupied.Value) networkOccupied.Value = false;
            }
            else
            {
                onTargetExited?.Invoke();
            }
        }
    }

    private void HandleOccupiedChanged(bool previousValue, bool newValue)
    {
        if (newValue) onTargetEntered?.Invoke();
        else onTargetExited?.Invoke();
    }

    // 권한 측에서만 물체를 중심으로 이동시킵니다. (NetworkTransform이 전파)
    private IEnumerator SnapToCenter(Rigidbody rb)
    {
        rb.isKinematic = true;

        Vector3 startPos = rb.position;
        Quaternion startRot = rb.rotation;

        float elapsedTime = 0f;
        while (elapsedTime < snapDuration)
        {
            if (currentTarget == null) yield break;

            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsedTime / snapDuration);

            rb.position = Vector3.Lerp(startPos, snapPoint.position, t);
            rb.rotation = Quaternion.Slerp(startRot, snapPoint.rotation, t);

            yield return null;
        }

        rb.position = snapPoint.position;
        rb.rotation = snapPoint.rotation;
        snapCoroutine = null;
    }
}
