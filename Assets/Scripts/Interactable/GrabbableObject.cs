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

    private Rigidbody rb;
    private Collider[] colliders;

    private List<PlayerInteractor> interactors = new List<PlayerInteractor>();
    private Dictionary<PlayerInteractor, SpringJoint> dragJoints = new Dictionary<PlayerInteractor, SpringJoint>();

    public bool IsBeingHeld => interactors.Count > 0;
    public int InteractorCount => interactors.Count;

    // 원본 물리 상태 저장
    private bool originalUseGravity;
    private bool originalIsKinematic;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;
    private float originalMass;
    private float originalLinearDamping;
    private float originalAngularDamping;
    private RigidbodyConstraints originalConstraints;
    
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
            // 무거운 상자: 같이 끌기 (스프링 조인트 다중 연결)
            rb.isKinematic = false;
            rb.useGravity = true;

            // 💡 무거운 물체 끄기 시 질감 극대화를 위한 물리 튜닝
            rb.mass = 40f; // 끄기 알맞은 묵직한 질량 설정 (플레이어가 튕기지 않는 최적선)
            rb.linearDamping = 8f; // 질질 끌리는 뻑뻑한 선형 저항
            rb.angularDamping = 10f; // 상자가 팽이처럼 돌지 않도록 회전 저항 증가
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; // 수평 회전(Y)만 허용하고 뒹굴기 차단

            Transform dragPoint = interactor.dragPoint;
            if (dragPoint != null)
            {
                Rigidbody dragRb = dragPoint.GetComponent<Rigidbody>();
                if (dragRb == null)
                {
                    dragRb = dragPoint.gameObject.AddComponent<Rigidbody>();
                    dragRb.isKinematic = true;
                }

                SpringJoint joint = gameObject.AddComponent<SpringJoint>();
                joint.connectedBody = dragRb;
                
                // 다시 false로 변경하여 플레이어의 dragPoint(머리/몸) 쪽으로 착 달라붙게 합니다.
                joint.autoConfigureConnectedAnchor = false; 
                joint.connectedAnchor = Vector3.zero; 
                
                // 부모 피벗이 아닌 실제 자식 모델(Collider)의 중심을 스프링의 끝단으로 설정하여 오프셋 버그 완벽 해결
                joint.anchor = localMeshOffset;
                
                // 💡 [수정] 캐릭터 뒤에 찰지게 밀착되어 끌려오도록 거리 소폭 축소 조율 (겹치지 않는 최적선)
                joint.minDistance = 0.55f; // 플레이어 중심에서 최소 0.55m 거리 유지
                joint.maxDistance = 0.7f; // 최대 0.7m 범위 내에서 끌려옴
                
                // 💡 [수정] 강해진 질량과 지면 저항을 끌어당길 수 있도록 스프링 장력과 댐퍼 대폭 상향
                joint.spring = 1200f;
                joint.damper = 50f;

                dragJoints[interactor] = joint;
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Debug.Log($"📦 무거운 상자를 플레이어가 잡았습니다. (현재 {interactors.Count}명)");
        }
        else
        {
            // 가벼운 상자: 같이 들기 (머리 위)
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
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

        if (isHeavy && dragJoints.TryGetValue(interactor, out SpringJoint joint))
        {
            if (joint != null) Destroy(joint);
            dragJoints.Remove(interactor);
        }

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

    private void LateUpdate()
    {
        // 1. 플레이어가 너무 멀어지면 강제로 놓도록 거리 제한 추가 (버그 방지)
        for (int i = interactors.Count - 1; i >= 0; i--)
        {
            PlayerInteractor interactor = interactors[i];
            if (interactor != null)
            {
                float dist = Vector3.Distance(transform.position, interactor.transform.position);
                if (dist > 4.5f) // 물체와 플레이어가 4.5m 이상 멀어지면 끈이 끊어지며 놓음
                {
                    interactor.SendMessage("ForceDropHeldObject", SendMessageOptions.DontRequireReceiver);
                    continue;
                }
            }
        }

        // 가벼운 물체: 스케일 버그를 피하기 위해 부모자식 관계를 맺지 않고 매 프레임 강제로 위치 동기화
        if (isHeavy || interactors.Count == 0) return;

        Vector3 center = Vector3.zero;
        Vector3 forward = Vector3.zero;

        foreach (var interactor in interactors)
        {
            center += interactor.holdPoint.position;
            forward += interactor.holdPoint.forward;
        }

        center /= interactors.Count;
        if (forward.sqrMagnitude > 0.001f) forward.Normalize();
        else forward = transform.forward;

        // 1. 회전을 먼저 적용
        transform.rotation = Quaternion.LookRotation(forward);
        
        // 2. 부모 피벗이 아니라 실제 모델(자식 콜라이더)이 플레이어의 머리(center)에 오도록 역산하여 위치 적용
        Vector3 worldMeshOffset = transform.TransformPoint(localMeshOffset) - transform.position;
        
        // 3. 상자가 플레이어 머리를 파고들지 않도록, 콜라이더의 절반 높이(meshExtentsY)만큼 강제로 위로 띄워줍니다.
        Vector3 verticalCorrection = Vector3.up * meshExtentsY;
        
        transform.position = center - worldMeshOffset + verticalCorrection;
    }

    private void StoreOriginalState()
    {
        originalUseGravity = rb.useGravity;
        originalIsKinematic = rb.isKinematic;
        originalInterpolation = rb.interpolation;
        originalCollisionMode = rb.collisionDetectionMode;
        originalMass = rb.mass;
        originalLinearDamping = rb.linearDamping;
        originalAngularDamping = rb.angularDamping;
        originalConstraints = rb.constraints;
    }

    private void RestoreOriginalState()
    {
        rb.useGravity = originalUseGravity;
        rb.isKinematic = originalIsKinematic;
        rb.interpolation = originalInterpolation;
        rb.collisionDetectionMode = originalCollisionMode;
        rb.mass = originalMass;
        rb.linearDamping = originalLinearDamping;
        rb.angularDamping = originalAngularDamping;
        rb.constraints = originalConstraints;
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