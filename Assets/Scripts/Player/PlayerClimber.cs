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
    
    // 몽키바 자동 이동을 위한 상태 저장
    private Collider lastMonkeyBar;
    private Vector3 autoMoveDir;

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
            // 매달려 있지 않은 상태라면 닿자마자 바로 매달리기
            else if (!isHanging)
            {
                StartHanging(other);
                Debug.Log($"[Climber] 자동 매달리기 시작: {other.name}");
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
        // 물리 속도를 완전히 초기화하고 물리 계산 방식 변경
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.isKinematic = true;
        physicsBody.useGravity = false;
        
        // 매달린 동안 외부 충돌에 의해 돌아가거나 밀리지 않도록 고정
        physicsBody.constraints = RigidbodyConstraints.FreezeRotation;
        
        // 사다리를 탈 때는 워프하지 않고 '가까이 가서 E키를 누른 그 자리'에서 바로 멈춰서 매달립니다.
        // 위치 변경 코드를 완전히 삭제하여 벽에 끼이거나 꼭대기로 순간이동하는 현상 방지
    }

    private void StopHanging()
    {
        isHanging = false;
        currentLedge = null;
        
        // PlayerController 원상복구
        physicsBody.isKinematic = false;
        physicsBody.useGravity = false; // PlayerController가 자체 커스텀 중력을 쓰므로 false 유지!
        
        // 매달릴 때 생긴 회전 속도 초기화
        physicsBody.angularVelocity = Vector3.zero;
        
        // PlayerController는 AddTorque로 중심을 잡는 물리 기반 컨트롤러이므로,
        // 모든 제약을 풀어주어야 정상 작동합니다. (빙글빙글 도는 버그 방지)
        physicsBody.constraints = RigidbodyConstraints.None; 
        
        playerController.enabled = true;
    }

    private void Update()
    {
        // 1. 매달리지 않은 상태일 때
        if (!isHanging)
        {
            // OnTriggerEnter에서 닿자마자 자동으로 매달리게 변경되었습니다.
            // 만약 점프해서 강제로 떨어졌는데 아직 트리거 안에 있다면, 이동키(W/위)를 누르면 다시 매달립니다.
            if (overlappingLedges.Count > 0 && moveAction != null)
            {
                Vector2 input = moveAction.action.ReadValue<Vector2>();
                if (input.y > 0.1f) // 앞/위 방향키를 누를 때 다시 매달리기
                {
                    StartHanging(overlappingLedges[overlappingLedges.Count - 1]);
                    Debug.Log("[Climber] 트리거 내부에서 재매달리기 시작!");
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

        // 매달린 상태에서 이동 처리
        if (moveAction != null && currentLedge != null)
        {
            Vector2 input = moveAction.action.ReadValue<Vector2>();
            bool isMonkeyBar = currentLedge.CompareTag(monkeyBarTag);

            // 몽키바는 입력과 무관하게 자동 이동, 사다리는 입력이 있을 때만 이동
            if (isMonkeyBar || input.sqrMagnitude > 0.01f)
            {
                // Update 루프에서 physicsBody.position을 참조하면 물리 프레임 갱신 전까지 값이 변하지 않아 안 움직이는 현상이 발생할 수 있습니다.
                // 따라서 transform.position을 기준으로 다음 위치를 계산합니다.
                Vector3 moveDir = Vector3.zero;

                if (isMonkeyBar)
                {
                    // 몽키바 타겟이 새로 설정되었을 때 한 번만 이동 축(방향)을 계산합니다.
                    if (lastMonkeyBar != currentLedge)
                    {
                        lastMonkeyBar = currentLedge;
                        
                        // 현재 위치에서 몽키바의 중심을 향하는 벡터 (bounds.center 대신 실제 오브젝트 중심인 transform.position 사용)
                        Vector3 wireCenter = currentLedge.transform.position;
                        Vector3 dirToCenter = (wireCenter - transform.position).normalized;
                        
                        // 몽키바의 로컬 6방향 축 중 중심을 향하는 방향과 가장 일치하는 축 찾기
                        Vector3[] axes = { 
                            currentLedge.transform.forward, -currentLedge.transform.forward, 
                            currentLedge.transform.right, -currentLedge.transform.right, 
                            currentLedge.transform.up, -currentLedge.transform.up 
                        };
                        
                        float maxDot = -1f;
                        autoMoveDir = currentLedge.transform.forward; // 기본값
                        
                        foreach (Vector3 axis in axes)
                        {
                            float d = Vector3.Dot(dirToCenter, axis);
                            if (d > maxDot)
                            {
                                maxDot = d;
                                autoMoveDir = axis;
                            }
                        }
                        
                        // --- 수평선상 스냅(Snap) (높이는 현재 높이 유지) ---
                        Vector3 playerToCenter = wireCenter - transform.position;
                        
                        // 플레이어 위치에서 줄의 중심선 중 가장 가까운 점 찾기
                        float distAlongWire = Vector3.Dot(playerToCenter, autoMoveDir);
                        Vector3 closestPointOnWire = wireCenter - autoMoveDir * distAlongWire;
                        
                        // 머리가 닿았을 때의 '현재 높이'를 강제로 유지 (1.7m 억지 보정으로 인해 밑으로 떨어지는 현상 완벽 해결)
                        closestPointOnWire.y = transform.position.y;
                        
                        transform.position = closestPointOnWire;
                        
                        // 이동하는 방향을 자연스럽게 바라보도록 회전 (Y축 기준)
                        if (autoMoveDir.sqrMagnitude > 0.001f)
                        {
                            transform.rotation = Quaternion.LookRotation(new Vector3(autoMoveDir.x, 0, autoMoveDir.z).normalized, Vector3.up);
                        }
                        
                        Debug.Log($"[Climber] 몽키바 진입 - 이동 축: {autoMoveDir}, 위치 스냅 완료");
                    }
                    
                    // 몽키바: 계산된 축 방향으로 무조건 자동 전진
                    moveDir = autoMoveDir;
                }
                else
                {
                    lastMonkeyBar = null; // 사다리로 돌아가면 초기화
                    // 사다리(Ledge): W/S는 위아래로, A/D는 좌우로 이동
                    moveDir = transform.right * input.x + transform.up * input.y;
                }
                
                Vector3 nextPos = transform.position + moveDir * climbMoveSpeed * Time.deltaTime;

                // --- 장애물 통과 방지 (벽/기둥 클리핑 방지) ---
                if (moveDir.sqrMagnitude > 0.001f)
                {
                    // 플레이어 중심(가슴 높이쯤)에서 이동 방향으로 구체를 쏴서 충돌 검사
                    Vector3 origin = transform.position + Vector3.up * 1.0f;
                    float checkRadius = 0.3f;
                    float checkDist = climbMoveSpeed * Time.deltaTime + 0.1f;
                    
                    if (Physics.SphereCast(origin, checkRadius, moveDir, out RaycastHit hit, checkDist, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // 부딪힌 물체가 현재 매달린 오브젝트가 아니라면 벽이나 기둥으로 간주!
                        if (hit.collider != currentLedge)
                        {
                            Debug.Log($"[Climber] 장애물({hit.collider.name})에 막힘! 통과 방지.");
                            nextPos = transform.position; // 이동 차단
                            
                            if (isMonkeyBar)
                            {
                                // 몽키바를 타고 가다가 기둥/벽에 부딪히면 멈추고 떨어짐
                                StopHanging();
                                return;
                            }
                        }
                    }
                }

                // 사다리 오브젝트의 최고 높이와 최저 높이 가져오기
                float maxY = currentLedge.bounds.max.y;
                float minY = currentLedge.bounds.min.y;

                // 1. 사다리(Ledge) 맨 꼭대기 또는 머리가 몽키바에 닿았을 때
                if (currentLedge.CompareTag(ledgeTag))
                {
                    // 머리가 몽키바(줄)에 닿았는지 매 프레임 검사 (플레이어 키를 약 1.7m로 계산)
                    Collider monkeyBar = overlappingLedges.Find(x => x.CompareTag(monkeyBarTag));
                    if (monkeyBar != null)
                    {
                        float headY = nextPos.y + 1.7f;
                        // 머리가 몽키바의 실제 오브젝트 높이(transform.position.y)에 도달하면 즉시 갈아타기
                        if (headY >= monkeyBar.transform.position.y)
                        {
                            currentLedge = monkeyBar;
                            Debug.Log("[Climber] 머리가 몽키바에 닿아 자동 전환!");
                            return; // 이번 프레임은 이동을 멈추고 다음 프레임부터 몽키바(스냅 등) 로직 실행
                        }
                    }

                    // 머리가 닿지 않았어도, 발이 사다리 꼭대기(maxY)를 넘어가면 종료 또는 다른 곳으로 갈아타기
                    if (nextPos.y > maxY)
                    {
                        Collider nextLedge = overlappingLedges.Find(x => x != currentLedge);
                        if (nextLedge != null)
                        {
                            currentLedge = nextLedge;
                        }
                        else
                        {
                            Debug.Log("[Climber] 꼭대기 도달로 인해 종료");
                            StopHanging(); 
                            
                            // 자연스럽게 사다리 위 발판으로 점프해서 올라타도록 물리적인 속도를 부여
                            physicsBody.linearVelocity = Vector3.up * 3.5f + transform.forward * 3f;
                            return;
                        }
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
                            Debug.Log($"[Climber] 바닥 착지로 인해 종료: {hit.collider.name}");
                            nextPos.y = hit.point.y;
                            transform.position = nextPos;
                            StopHanging();
                            return;
                        }
                    }
                }

                // 3. (혹시 몰라서 남겨두는) 사다리 맨 아래 바닥 밑으로 더 못 내려가게 막기
                if (currentLedge.CompareTag(ledgeTag) && nextPos.y <= minY)
                {
                    Debug.Log("[Climber] 사다리 최하단 도달로 인해 종료");
                    nextPos.y = minY;
                    transform.position = nextPos;
                    StopHanging();
                    return;
                }

                // 최종 이동 적용 (transform.position 직접 설정)
                transform.position = nextPos;
            }
        }
    }
}
