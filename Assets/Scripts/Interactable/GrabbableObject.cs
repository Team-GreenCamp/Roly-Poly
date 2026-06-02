using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : MonoBehaviour
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

    private List<PlayerInteractor> interactors = new List<PlayerInteractor>();

    public bool IsBeingHeld => interactors.Count > 0;
    public int InteractorCount => interactors.Count;

    // 원본 물리 상태 저장
    private bool originalUseGravity;
    private bool originalIsKinematic;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;
    
    private Vector3 localMeshOffset = Vector3.zero;
    private float meshExtentsY = 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        
        // 💡 상자 기믹 등에 의해 물리 상태가 강제로 변경되기 전에
        // 게임 시작 첫 프레임(Awake)에 원래의 완전한 물리 원본 상태를 먼저 캐싱합니다.
        StoreOriginalState();
    }

    private void Start()
    {
        // 유저의 프리팹 구조(부모 피벗과 자식 모델의 위치가 어긋난 경우)를 보정하기 위해
        // 실제 모델(모든 Collider의 결합된 중심점)을 찾아 로컬 좌표로 저장합니다.
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
                meshExtentsY = combinedBounds.extents.y; // 모델의 절반 높이 저장 (머리 겹침 방지용)
            }
        }
    }

    public void AddInteractor(PlayerInteractor interactor)
    {
        if (interactors.Contains(interactor)) return;

        interactors.Add(interactor);

        if (isHeavy)
        {
            // 무거운 상자: 여러 조인트 대신 잡은 플레이어들의 평균 지점을 향해 무겁게 끌립니다.
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (interactors.Count == 1)
            {
                rb.angularVelocity = Vector3.zero;
            }
            Debug.Log($"📦 무거운 상자를 플레이어가 잡았습니다. (현재 {interactors.Count}명)");
        }
        else
        {
            // 가벼운 상자: 같이 들기 (머리 위)
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            Debug.Log($"📦 가벼운 상자를 플레이어가 들었습니다. (현재 {interactors.Count}명)");
        }

        IgnoreCollisionWith(interactor, true);
        UpdateInteractorsSpeed();
    }

    public void RemoveInteractor(PlayerInteractor interactor, bool isSnapping = false)
    {
        if (!interactors.Contains(interactor)) return;

        interactors.Remove(interactor);

        IgnoreCollisionWith(interactor, false);

        if (interactors.Count == 0)
        {
            if (!isSnapping)
            {
                RestoreOriginalState();
                if (!isHeavy)
                {
                    // 물리 엔진 버그 방지 (살짝 위로)
                    transform.position += Vector3.up * 0.5f;
                }
                
                try
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }
            Debug.Log("📦 상자를 완전히 내려놓았습니다.");
        }
        else
        {
            UpdateInteractorsSpeed();
        }
        
        // 놓은 사람의 속도 정상화
        PlayerController pc = interactor.GetComponent<PlayerController>();
        if (pc != null) pc.ResetCarrySpeedMultiplier();
    }

    private void FixedUpdate()
    {
        // 1. 플레이어가 너무 멀어지면 강제로 놓도록 거리 제한 추가 (버그 방지)
        for (int i = interactors.Count - 1; i >= 0; i--)
        {
            PlayerInteractor interactor = interactors[i];
            if (interactor != null)
            {
                float dist = Vector3.Distance(transform.position, interactor.transform.position);
                if (dist > Mathf.Max(0.1f, heldFollowMaxDistance)) // 물체와 플레이어가 일정 거리 이상 멀어지면 끈이 끊어지며 놓음
                {
                    interactor.SendMessage("ForceDropHeldObject", SendMessageOptions.DontRequireReceiver);
                    continue;
                }
            }
        }

        if (interactors.Count == 0) return;

        if (isHeavy)
        {
            UpdateHeavyDragMotion();
            return;
        }

        UpdateLightHoldMotion();
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

        // 혼자 잡으면 천천히 질질 끌리고, 여러 명이 잡으면 평균 지점으로 더 안정적으로 따라오게 합니다.
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

        // 1. 회전을 먼저 적용
        Quaternion targetRotation = Quaternion.LookRotation(forward);
        rb.MoveRotation(targetRotation);
        
        // 2. 부모 피벗이 아니라 실제 모델(자식 콜라이더)이 플레이어의 머리(center)에 오도록 역산하여 위치 적용
        Vector3 scaledMeshOffset = Vector3.Scale(localMeshOffset, transform.lossyScale);
        Vector3 worldMeshOffset = targetRotation * scaledMeshOffset;
        
        // 3. 상자가 플레이어 머리를 파고들지 않도록, 콜라이더의 절반 높이(meshExtentsY)만큼 강제로 위로 띄워줍니다.
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
            if (interactor == null)
            {
                continue;
            }

            Transform followPoint = useDragPoint ? interactor.dragPoint : interactor.holdPoint;
            if (followPoint == null)
            {
                continue;
            }

            center += followPoint.position;
            forward += followPoint.forward;
            validInteractorCount++;
        }

        if (validInteractorCount == 0)
        {
            return false;
        }

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

    private void IgnoreCollisionWith(PlayerInteractor interactor, bool ignore)
    {
        // PlayerInteractor의 Awake 순서 문제로 Collider를 캐싱하지 못한 경우를 대비하여 실시간으로 확실하게 검색
        Collider[] playerCols = interactor.GetComponentsInChildren<Collider>(true);
        if (playerCols != null && colliders != null)
        {
            foreach (var pCol in playerCols)
            {
                if (pCol.isTrigger) continue; // 트리거는 물리 밀어냄이 없으므로 제외
                
                foreach (var col in colliders)
                {
                    if (col != null && pCol != null) 
                    {
                        Physics.IgnoreCollision(pCol, col, ignore);
                    }
                }
            }
        }
    }

    private void UpdateInteractorsSpeed()
    {
        float multiplier = baseSpeedMultiplier + (interactors.Count - 1) * multiPlayerSpeedBonus;
        multiplier = Mathf.Clamp(multiplier, 0.1f, 1f);

        foreach (var interactor in interactors)
        {
            PlayerController pc = interactor.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.SetCarrySpeedMultiplier(multiplier);
            }
        }
    }
}
