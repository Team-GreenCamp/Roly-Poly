using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 서버 권한 잡기 오브젝트.
//
// 이 게임의 grabbable 프리팹은 서버 권한 NetworkTransform/NetworkRigidbody를 씁니다.
// 여러 명이 한 물체를 함께 드는 협동을 지원하려면(소유권 이전 방식으로는 불가) "서버가 추종 물리를 굴리고"
// NetworkTransform이 모든 클라이언트에 위치를 전파해야 합니다.
//
// 역할 분담
//  • 서버         : 홀더 목록 + 박스 Rigidbody 물리(추종/던지기) + heldCount 동기화
//  • 소유자 클라  : 아웃라인/기울기/충돌 무시/운반 속도/들고 있는 상태 (PlayerInteractor가 로컬 처리)
//
// NetworkObject가 없거나 미스폰이면(구 프리팹/에디터 단독 테스트) 기존처럼 로컬에서 동작합니다.
[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : NetworkBehaviour
{
    [Header("오브젝트 설정")]
    [Tooltip("체크하면 무거운 물체(끌기용), 체크 해제하면 가벼운 물체(들기용)")]
    public bool isHeavy = false;

    [Header("다중 상호작용 속도 설정")]
    [Tooltip("1명일 때의 기본 속도 배율 (1 = 정상 속도, 0.5 = 절반 속도)")]
    public float baseSpeedMultiplier = 0.6f;
    [Tooltip("1명이 추가될 때마다 증가하는 속도 배율 보너스")]
    public float multiPlayerSpeedBonus = 0.4f;

    [Header("잡기 안정화 설정")]
    [SerializeField] private float heavySingleFollowAcceleration = 12f;
    [SerializeField] private float heavyMultiFollowAccelerationBonus = 8f;
    [SerializeField] private float heavyDragDamping = 5f;
    [SerializeField] private float heavyAngularDamping = 8f;
    [SerializeField] private float heavyMaxFollowAcceleration = 40f;
    [SerializeField] private float heldFollowMaxDistance = 4.5f;

    private Rigidbody rb;
    private Collider[] colliders;

    // 권한(서버 또는 오프라인) 인스턴스에서만 채워지는 홀더 목록.
    private List<PlayerInteractor> interactors = new List<PlayerInteractor>();

    private readonly NetworkVariable<int> heldCount =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;
    // 박스 물리를 직접 굴릴 권한이 있는 인스턴스인가(서버 또는 오프라인).
    private bool HasPhysicsAuthority => !IsNetworkActive || IsServer;

    public bool IsBeingHeld => IsNetworkActive ? heldCount.Value > 0 : interactors.Count > 0;
    public int InteractorCount => IsNetworkActive ? heldCount.Value : interactors.Count;
    public float HeldFollowMaxDistance => Mathf.Max(0.1f, heldFollowMaxDistance);

    // 원본 물리 상태 저장
    private bool originalUseGravity;
    private bool originalIsKinematic;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;

    private Vector3 localMeshOffset = Vector3.zero;
    private float meshExtentsY = 0f;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        StoreOriginalState();
    }

    private void Start()
    {
        if (colliders != null && colliders.Length > 0)
        {
            Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);
            bool boundsInitialized = false;

            foreach (var col in colliders)
            {
                if (col != null && !col.isTrigger)
                {
                    if (!boundsInitialized)
                    {
                        combinedBounds = col.bounds;
                        boundsInitialized = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(col.bounds);
                    }
                }
            }

            if (boundsInitialized)
            {
                localMeshOffset = transform.InverseTransformPoint(combinedBounds.center);
                meshExtentsY = combinedBounds.extents.y;
            }
        }
    }

    // ───────────────────────────────────────────────────────────
    // PlayerInteractor(소유자)가 호출하는 요청 진입점
    // ───────────────────────────────────────────────────────────
    public void RequestAddInteractor(PlayerInteractor interactor)
    {
        if (!IsNetworkActive) { AddInteractor(interactor); return; }
        if (IsServer) AddInteractorByClient(interactor.OwnerClientId);
        else AddInteractorServerRpc(interactor.OwnerClientId);
    }

    public void RequestRemoveInteractor(PlayerInteractor interactor, bool isSnapping)
    {
        if (!IsNetworkActive) { RemoveInteractor(interactor, isSnapping); return; }
        if (IsServer) RemoveInteractorByClient(interactor.OwnerClientId, isSnapping);
        else RemoveInteractorServerRpc(interactor.OwnerClientId, isSnapping);
    }

    public void RequestThrow(PlayerInteractor interactor, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        if (!IsNetworkActive) { ApplyThrow(interactor, linearVelocity, angularVelocity); return; }
        if (IsServer) ApplyThrowByClient(interactor.OwnerClientId, linearVelocity, angularVelocity);
        else ThrowServerRpc(interactor.OwnerClientId, linearVelocity, angularVelocity);
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddInteractorServerRpc(ulong clientId) => AddInteractorByClient(clientId);

    [ServerRpc(RequireOwnership = false)]
    private void RemoveInteractorServerRpc(ulong clientId, bool isSnapping) => RemoveInteractorByClient(clientId, isSnapping);

    [ServerRpc(RequireOwnership = false)]
    private void ThrowServerRpc(ulong clientId, Vector3 linearVelocity, Vector3 angularVelocity)
        => ApplyThrowByClient(clientId, linearVelocity, angularVelocity);

    private void AddInteractorByClient(ulong clientId)
    {
        PlayerInteractor pi = ResolveInteractor(clientId);
        if (pi != null) AddInteractor(pi);
    }

    private void RemoveInteractorByClient(ulong clientId, bool isSnapping)
    {
        PlayerInteractor pi = ResolveInteractor(clientId);
        if (pi != null) RemoveInteractor(pi, isSnapping);
    }

    private void ApplyThrowByClient(ulong clientId, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        PlayerInteractor pi = ResolveInteractor(clientId);
        if (pi != null) ApplyThrow(pi, linearVelocity, angularVelocity);
    }

    private PlayerInteractor ResolveInteractor(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
        {
            return client.PlayerObject.GetComponent<PlayerInteractor>();
        }
        return null;
    }

    // ───────────────────────────────────────────────────────────
    // 권한 인스턴스에서 실행되는 실제 상태/물리 변경
    // ───────────────────────────────────────────────────────────
    public void AddInteractor(PlayerInteractor interactor)
    {
        if (interactor == null || interactors.Contains(interactor)) return;

        interactors.Add(interactor);

        if (isHeavy)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (interactors.Count == 1) rb.angularVelocity = Vector3.zero;
        }
        else
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        if (IsNetworkActive && IsServer) heldCount.Value = interactors.Count;
    }

    public void RemoveInteractor(PlayerInteractor interactor, bool isSnapping = false)
    {
        if (interactor == null || !interactors.Contains(interactor)) return;

        interactors.Remove(interactor);

        if (interactors.Count == 0 && !isSnapping)
        {
            if (!isHeavy)
            {
                EnableDroppedLightPhysics();
            }
            else
            {
                RestoreOriginalState();
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (IsNetworkActive && IsServer) heldCount.Value = interactors.Count;
    }

    private void ApplyThrow(PlayerInteractor interactor, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        // 던지기는 스냅 없이 즉시 손에서 떼고 전방 속도를 부여합니다.
        RemoveInteractor(interactor, isSnapping: true); // isSnapping=true: 아래에서 물리 직접 세팅
        interactors.Clear();
        if (IsNetworkActive && IsServer) heldCount.Value = 0;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = linearVelocity;
        rb.angularVelocity = angularVelocity;
    }

    // 스냅존이 박스를 넘겨받을 때(권한 측) 호출.
    public void DetachForSnap()
    {
        interactors.Clear();
        if (IsNetworkActive && IsServer) heldCount.Value = 0;
    }

    // 열쇠 소모처럼 오브젝트를 없앨 때. 네트워크에서는 서버만 Despawn할 수 있습니다.
    public void RequestConsume(PlayerInteractor interactor)
    {
        if (!IsNetworkActive) { Destroy(gameObject); return; }
        if (IsServer) DespawnOnServer();
        else ConsumeServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConsumeServerRpc() => DespawnOnServer();

    private void DespawnOnServer()
    {
        interactors.Clear();
        if (heldCount.Value != 0) heldCount.Value = 0;

        if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        else Destroy(gameObject);
    }

    // 서버가 특정 클라이언트가 이 물체를 들고 있는지 권위 있는 홀더 목록으로 검증합니다.
    // (예: 문이 "열쇠를 들었다"는 클라이언트의 주장을 서버가 직접 확인할 때 사용)
    public bool IsHeldByClientOnServer(ulong clientId)
    {
        for (int i = 0; i < interactors.Count; i++)
        {
            if (interactors[i] != null && interactors[i].OwnerClientId == clientId) return true;
        }
        return false;
    }

    // 서버가 이 물체를 소모(Despawn)합니다. 열쇠 사용처럼 서버에서 직접 호출하는 경로용입니다.
    public void ServerConsume()
    {
        if (!IsServer) return;
        DespawnOnServer();
    }

    private void FixedUpdate()
    {
        // 클라이언트는 NetworkTransform이 박스를 옮기므로 물리를 굴리지 않습니다.
        if (!HasPhysicsAuthority) return;

        // 권한 측에서만 추종 물리. (먼 홀더는 소유자 PlayerInteractor가 스스로 놓도록 처리)
        for (int i = interactors.Count - 1; i >= 0; i--)
        {
            if (interactors[i] == null) interactors.RemoveAt(i);
        }

        if (interactors.Count == 0) return;

        if (isHeavy) UpdateHeavyDragMotion();
        else UpdateLightHoldMotion();
    }

    private void UpdateHeavyDragMotion()
    {
        if (!TryGetAveragePoint(useDragPoint: true, out Vector3 targetPoint, out _, out int validInteractorCount))
        {
            return;
        }

        targetPoint.y = rb.worldCenterOfMass.y;
        Vector3 toTarget = targetPoint - rb.worldCenterOfMass;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        float followAcceleration = heavySingleFollowAcceleration + ((validInteractorCount - 1) * heavyMultiFollowAccelerationBonus);
        Vector3 desiredAcceleration = (toTarget * Mathf.Max(0f, followAcceleration)) - (planarVelocity * Mathf.Max(0f, heavyDragDamping));
        float maxFollowAcceleration = Mathf.Max(0f, heavyMaxFollowAcceleration);

        if (maxFollowAcceleration <= 0f)
        {
            desiredAcceleration = Vector3.zero;
        }
        else if (desiredAcceleration.magnitude > maxFollowAcceleration)
        {
            desiredAcceleration = desiredAcceleration.normalized * maxFollowAcceleration;
        }

        rb.AddForce(desiredAcceleration, ForceMode.Acceleration);
        rb.AddTorque(-rb.angularVelocity * Mathf.Max(0f, heavyAngularDamping), ForceMode.Acceleration);
    }

    private void UpdateLightHoldMotion()
    {
        if (!TryGetAveragePoint(useDragPoint: false, out Vector3 center, out Vector3 forward, out _))
        {
            return;
        }

        if (forward.sqrMagnitude > 0.001f) forward.Normalize();
        else forward = transform.forward;

        Quaternion targetRotation = Quaternion.LookRotation(forward);
        rb.MoveRotation(targetRotation);

        Vector3 scaledMeshOffset = Vector3.Scale(localMeshOffset, transform.lossyScale);
        Vector3 worldMeshOffset = targetRotation * scaledMeshOffset;
        Vector3 verticalCorrection = Vector3.up * meshExtentsY;

        rb.MovePosition(center - worldMeshOffset + verticalCorrection);
    }

    private bool TryGetAveragePoint(bool useDragPoint, out Vector3 center, out Vector3 forward, out int validInteractorCount)
    {
        center = Vector3.zero;
        forward = Vector3.zero;
        validInteractorCount = 0;

        foreach (var interactor in interactors)
        {
            if (interactor == null) continue;

            Transform followPoint = useDragPoint ? interactor.dragPoint : interactor.holdPoint;
            if (followPoint == null) continue;

            center += followPoint.position;
            forward += followPoint.forward;
            validInteractorCount++;
        }

        if (validInteractorCount == 0) return false;

        center /= validInteractorCount;
        return true;
    }

    private void StoreOriginalState()
    {
        originalUseGravity = rb.useGravity;
        originalIsKinematic = rb.isKinematic;
        originalInterpolation = rb.interpolation;
        originalCollisionMode = rb.collisionDetectionMode;
    }

    private void RestoreOriginalState()
    {
        rb.useGravity = originalUseGravity;
        rb.isKinematic = originalIsKinematic;
        rb.interpolation = originalInterpolation;
        rb.collisionDetectionMode = originalCollisionMode;
    }

    private void EnableDroppedLightPhysics()
    {
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public float GetCarrySpeedMultiplier()
    {
        float multiplier = baseSpeedMultiplier + (InteractorCount - 1) * multiPlayerSpeedBonus;
        return Mathf.Clamp(multiplier, 0.1f, 1f);
    }

    // 소유자 클라이언트가 자기 플레이어와 이 박스의 충돌을 끄고/켜는 데 사용 (각 머신 로컬 처리).
    public void SetIgnoreCollisionWith(PlayerInteractor interactor, bool ignore)
    {
        if (interactor == null || colliders == null) return;

        Collider[] playerCols = interactor.GetComponentsInChildren<Collider>(true);
        if (playerCols == null) return;

        foreach (var pCol in playerCols)
        {
            if (pCol == null || pCol.isTrigger) continue;
            foreach (var col in colliders)
            {
                if (col != null) Physics.IgnoreCollision(pCol, col, ignore);
            }
        }
    }
}
