using UnityEngine;
using System.Collections;
using Unity.Netcode;
using UnityEngine.Events;

// 서버 권한 스냅존. 잡기 시스템(GrabbableObject)과 연동됩니다.
// 동기화하려면 NetworkObject가 필요합니다. 스냅 모션은 서버가 박스 트랜스폼을 옮기고 NetworkTransform이 전파합니다.
// (NetworkObject가 없으면 기존처럼 로컬에서 스냅합니다.)
[RequireComponent(typeof(NetworkObject))]
public class SnapZone : NetworkBehaviour
{
    [Header("스냅 설정")]
    [Tooltip("스냅할 오브젝트의 태그 (비어있으면 모든 Grabbable 허용)")]
    public string targetId = "Plank";
    public Transform snapPoint;
    public float snapDuration = 0.3f;
    public float snapDistance = 1.0f;

    [Header("시각적 피드백")]
    [Tooltip("오브젝트가 근처에 있을 때 보여줄 고스트 메시 (선택 사항)")]
    public GameObject ghostPreview;

    [Header("이벤트")]
    public UnityEvent onSnapped;
    public UnityEvent onUnsnapped;

    private GrabbableObject snappedObject; // 권한 측 참조
    private Coroutine snapCoroutine;

    private readonly NetworkVariable<bool> networkOccupied =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;
    private bool HasSnapAuthority => !IsNetworkActive || IsServer;

    public bool IsOccupied => IsNetworkActive ? networkOccupied.Value : snappedObject != null;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);
        if (ghostPreview != null) ghostPreview.SetActive(false);
        if (snapPoint == null) snapPoint = transform;
    }

    public override void OnNetworkSpawn()
    {
        networkOccupied.OnValueChanged += HandleOccupiedChanged;
        if (networkOccupied.Value && ghostPreview != null) ghostPreview.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        networkOccupied.OnValueChanged -= HandleOccupiedChanged;
    }

    public bool CanSnap(GrabbableObject grabbable)
    {
        if (IsOccupied || grabbable == null) return false;
        return string.IsNullOrEmpty(targetId) || grabbable.CompareTag(targetId);
    }

    // PlayerInteractor가 물체를 스냅존에 넘길 때 호출.
    public void RequestSnap(GrabbableObject grabbable)
    {
        if (grabbable == null) return;

        if (!IsNetworkActive)
        {
            SnapLocal(grabbable);
            return;
        }

        if (IsServer)
        {
            SnapOnServer(grabbable);
        }
        else if (grabbable.TryGetComponent(out NetworkObject grabbableNo))
        {
            RequestSnapServerRpc(grabbableNo);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSnapServerRpc(NetworkObjectReference grabbableRef, ServerRpcParams rpcParams = default)
    {
        if (grabbableRef.TryGet(out NetworkObject grabbableNo))
        {
            GrabbableObject grabbable = grabbableNo.GetComponent<GrabbableObject>();
            if (grabbable != null) SnapOnServer(grabbable);
        }
    }

    private void SnapOnServer(GrabbableObject grabbable)
    {
        if (networkOccupied.Value || grabbable.IsBeingHeld) return;

        snappedObject = grabbable;
        grabbable.DetachForSnap();
        networkOccupied.Value = true; // OnValueChanged가 모두에게 onSnapped/고스트 처리

        BeginSnapMotion(grabbable.GetComponent<Rigidbody>());
    }

    // ───── 오프라인(비네트워크) 폴백 ─────
    private void SnapLocal(GrabbableObject grabbable)
    {
        if (snappedObject != null || grabbable.IsBeingHeld) return;
        snappedObject = grabbable;
        BeginSnapMotion(grabbable.GetComponent<Rigidbody>());
        onSnapped?.Invoke();
        if (ghostPreview != null) ghostPreview.SetActive(false);
    }

    private void HandleOccupiedChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            onSnapped?.Invoke();
            if (ghostPreview != null) ghostPreview.SetActive(false);
        }
        else
        {
            onUnsnapped?.Invoke();
        }
    }

    // 권한 측에서만 박스를 스냅 지점으로 이동시킵니다.
    private void BeginSnapMotion(Rigidbody targetBody)
    {
        if (!HasSnapAuthority || targetBody == null) return;
        if (snapCoroutine != null) StopCoroutine(snapCoroutine);
        snapCoroutine = StartCoroutine(SnapCoroutine(targetBody));
    }

    private IEnumerator SnapCoroutine(Rigidbody targetBody)
    {
        targetBody.isKinematic = true;
        targetBody.useGravity = false;

        Vector3 startPos = targetBody.transform.position;
        Quaternion startRot = targetBody.transform.rotation;
        Vector3 targetPos = snapPoint.position;
        Quaternion targetRot = snapPoint.rotation;

        float elapsedTime = 0f;
        while (elapsedTime < snapDuration)
        {
            if (snappedObject == null) yield break;

            float t = Mathf.SmoothStep(0, 1, elapsedTime / snapDuration);
            targetBody.transform.position = Vector3.Lerp(startPos, targetPos, t);
            targetBody.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        targetBody.transform.SetPositionAndRotation(targetPos, targetRot);
        snapCoroutine = null;
    }

    private void ReleaseObject()
    {
        snappedObject = null;

        if (IsNetworkActive)
        {
            if (IsServer && networkOccupied.Value) networkOccupied.Value = false;
        }
        else
        {
            onUnsnapped?.Invoke();
        }
    }

    private void Update()
    {
        // 스냅된 물체를 누군가 다시 잡았다면 스냅 해제 (판정은 권한 측에서)
        if (!HasSnapAuthority) return;
        if (snappedObject != null && snappedObject.IsBeingHeld)
        {
            ReleaseObject();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 고스트 미리보기는 로컬 시각 힌트
        if (IsOccupied) return;

        GrabbableObject grabbable = other.GetComponentInParent<GrabbableObject>();
        if (grabbable != null && CanSnap(grabbable))
        {
            if (ghostPreview != null) ghostPreview.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        GrabbableObject grabbable = other.GetComponentInParent<GrabbableObject>();
        if (grabbable != null)
        {
            if (ghostPreview != null) ghostPreview.SetActive(false);

            if (HasSnapAuthority && grabbable == snappedObject)
            {
                ReleaseObject();
            }
        }
    }
}
