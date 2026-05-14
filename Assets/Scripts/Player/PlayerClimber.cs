using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerClimber : MonoBehaviour
{
    private PlayerController playerController;
    private Rigidbody physicsBody;
    
    [Header("입력 설정")]
    public InputActionReference interactAction; // 상호작용 (E)
    public InputActionReference moveAction;     // 매달리기 중 이동
    public InputActionReference jumpAction;     // 떨어지기 (Space)

    [Header("매달리기 설정")]
    public float climbMoveSpeed = 2f;
    public string ledgeTag = "Ledge";
    public string monkeyBarTag = "MonkeyBar";
    
    private bool isHanging = false;
    private Collider currentLedge;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        physicsBody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        // 입력 액션 활성화 보장
        if (interactAction != null) interactAction.action.Enable();
        if (moveAction != null) moveAction.action.Enable();
        if (jumpAction != null) jumpAction.action.Enable();
    }

    private List<Collider> overlappingLedges = new List<Collider>();

    private void OnTriggerEnter(Collider other)
    {
        // 디버그용: 어떤 물체와 부딪혔는지 콘솔에 출력
        Debug.Log($"[Climber] 부딪힘: {other.name} | 태그: {other.tag} | 레이어: {LayerMask.LayerToName(other.gameObject.layer)}");

        if (other.CompareTag(ledgeTag) || other.CompareTag(monkeyBarTag))
        {
            if (!overlappingLedges.Contains(other))
                overlappingLedges.Add(other);
            
            // 이미 매달려 있는 상태에서 새로운 매달릴 곳에 닿으면 자동으로 타겟 변경!
            if (isHanging && currentLedge != other)
            {
                currentLedge = other;
                Debug.Log($"[Climber] 타겟 자동 전환: {other.name}");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (overlappingLedges.Contains(other))
            overlappingLedges.Remove(other);

        // 현재 잡고 있는 곳에서 완전히 벗어났을 때 (예: 옆으로 이동해서 끝에 도달)
        if (isHanging && other == currentLedge)
        {
            if (overlappingLedges.Count > 0)
            {
                // 아직 다른 트리거 안에 있다면 그리로 갈아타기
                currentLedge = overlappingLedges[0];
            }
            else
            {
                // 더 이상 잡을 곳이 없으면 떨어지기
                StopHanging();
            }
        }
    }

    private void StartHanging(Collider ledge)
    {
        isHanging = true;
        currentLedge = ledge;
        
        // PlayerController 이동/물리 끄기 및 기존 속도 초기화
        playerController.enabled = false;
        // 물리 속도를 먼저 초기화한 뒤 Kinematic을 켜야 에러가 나지 않습니다.
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.isKinematic = true;
        physicsBody.useGravity = false;
        
        // 사다리를 탈 때는 워프하지 않고 '가까이 가서 E키를 누른 그 자리'에서 바로 멈춰서 매달립니다.
        // 위치 변경 코드를 완전히 삭제하여 벽에 끼이거나 꼭대기로 순간이동하는 현상 방지
    }

    private void StopHanging()
    {
        isHanging = false;
        currentLedge = null;
        
        // PlayerController 원상복구
        physicsBody.isKinematic = false;
        physicsBody.useGravity = false; // PlayerController가 자체 커스텀 중력을 쓰므로 false 유지! (이동 버벅임 해결)
        playerController.enabled = true;
    }

    private void Update()
    {
        // 1. 매달리지 않은 상태일 때: 근처에 매달릴 곳이 있고 상호작용 키를 누르면 매달리기
        if (!isHanging)
        {
            if (interactAction != null && interactAction.action.WasPressedThisFrame())
            {
                Debug.Log($"[Climber] E키 입력 감지됨! 현재 주변 물체 수: {overlappingLedges.Count}");
                
                if (overlappingLedges.Count > 0)
                {
                    StartHanging(overlappingLedges[overlappingLedges.Count - 1]);
                    Debug.Log("[Climber] 매달리기 시작!");
                }
            }
            return;
        }

        // 2. 매달린 상태일 때: 점프키로 떨어지기
        if (jumpAction != null && jumpAction.action.WasPressedThisFrame())
        {
            StopHanging();
            return;
        }

        // 매달린 상태에서 입력 가로채서 이동
        if (moveAction != null && currentLedge != null)
        {
            Vector2 input = moveAction.action.ReadValue<Vector2>();
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 moveDir = transform.right * input.x + transform.up * input.y;
                Vector3 nextPos = transform.position + moveDir * climbMoveSpeed * Time.deltaTime;

                // 사다리 오브젝트의 최고 높이와 최저 높이 가져오기
                float maxY = currentLedge.bounds.max.y;
                float minY = currentLedge.bounds.min.y;

                // 1. 사다리(Ledge) 맨 꼭대기에 도달했을 때
                if (currentLedge.CompareTag(ledgeTag) && nextPos.y > maxY)
                {
                    // 만약 근처에 갈아탈 수 있는 다른 곳(예: 몽키바)이 있다면 자동 전환
                    Collider nextLedge = overlappingLedges.Find(x => x != currentLedge);
                    if (nextLedge != null)
                    {
                        currentLedge = nextLedge;
                        // StopHanging을 하지 않고 계속 매달린 상태 유지
                    }
                    else
                    {
                        StopHanging(); 
                        
                        // 자연스럽게 사다리 위 발판으로 점프해서 올라타도록 물리적인 속도를 부여
                        physicsBody.linearVelocity = Vector3.up * 3.5f + transform.forward * 3f;
                        return;
                    }
                }
                
                // 2. 바닥으로 내려왔을 때 (바닥 충돌 감지)
                // 사다리의 최하단(minY)이 땅 밑에 파묻혀 있을 수 있으므로, 실제 바닥에 발이 닿으면 놓도록 Raycast 사용
                if (moveDir.y < 0) // 아래로 내려가고 있을 때
                {
                    // 플레이어 발바닥 약간 위에서 아래로 레이저를 쏴서 땅이 있는지 검사
                    if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 0.6f))
                    {
                        if (hit.collider != currentLedge && !hit.collider.isTrigger) // 사다리나 트리거가 아닌 진짜 바닥이면
                        {
                            nextPos.y = hit.point.y;
                            transform.position = nextPos;
                            StopHanging();
                            return;
                        }
                    }
                }

                // 3. (혹시 몰라서 남겨두는) 사다리 맨 아래 바닥 밑으로 더 못 내려가게 막기
                if (nextPos.y <= minY)
                {
                    nextPos.y = minY;
                    transform.position = nextPos;
                    StopHanging();
                    return;
                }

                transform.position = nextPos;
            }
        }
    }
}
