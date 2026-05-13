using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class CoopGrabbable : NetworkBehaviour, IInteractable
{
    [Header("협동 운반 설정")]
    public int requiredPlayers = 2;
    public float coopMoveForce = 50f;

    // 잡고 있는 플레이어들의 NetworkObjectId 리스트
    private List<ulong> grabbingPlayers = new List<ulong>();
    private List<Transform> playerTransforms = new List<Transform>();
    private List<PlayerController> playerControllers = new List<PlayerController>();

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void RequestInteract(GameObject interactor)
    {
        NetworkObject netObj = interactor.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            // 서버에 잡기(혹은 놓기) 요청
            RequestGrabServerRpc(netObj.NetworkObjectId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestGrabServerRpc(ulong playerId)
    {
        if (grabbingPlayers.Contains(playerId))
        {
            grabbingPlayers.Remove(playerId);
            RemovePlayerRef(playerId);
        }
        else if (grabbingPlayers.Count < requiredPlayers)
        {
            grabbingPlayers.Add(playerId);
            AddPlayerRef(playerId);
        }
    }

    private void AddPlayerRef(ulong playerId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerId, out NetworkObject playerObj))
        {
            playerTransforms.Add(playerObj.transform);
            playerControllers.Add(playerObj.GetComponent<PlayerController>());
        }
    }

    private void RemovePlayerRef(ulong playerId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerId, out NetworkObject playerObj))
        {
            playerTransforms.Remove(playerObj.transform);
            playerControllers.Remove(playerObj.GetComponent<PlayerController>());
        }
    }

    private void FixedUpdate()
    {
        // 물리 연산 및 동기화는 호스트/서버 권한으로 꼬이지 않게 중앙 제어
        if (!IsServer) return;

        if (grabbingPlayers.Count >= requiredPlayers)
        {
            rb.isKinematic = false;

            // 1. 양쪽 플레이어 사이의 평균(중앙) 지점 계산
            Vector3 centerPoint = Vector3.zero;
            foreach (Transform t in playerTransforms)
            {
                centerPoint += t.position;
            }
            centerPoint /= playerTransforms.Count;

            // 물체의 위치를 두 플레이어 사이의 중앙으로 동기화 (높이는 물체 본연의 높이 유지)
            Vector3 targetPos = new Vector3(centerPoint.x, rb.position.y, centerPoint.z);
            rb.MovePosition(Vector3.Lerp(rb.position, targetPos, Time.fixedDeltaTime * 10f));

            // 2. 양쪽 플레이어의 이동 Input 벡터 합산
            Vector3 totalMoveInput = Vector3.zero;
            foreach (PlayerController pc in playerControllers)
            {
                if (pc != null)
                {
                    Vector2 input = pc.MoveInput;
                    
                    // 각 플레이어의 전방 기준 방향으로 변환
                    Transform moveRef = pc.transform; 
                    Vector3 forward = Vector3.ProjectOnPlane(moveRef.forward, Vector3.up).normalized;
                    Vector3 right = Vector3.ProjectOnPlane(moveRef.right, Vector3.up).normalized;
                    
                    totalMoveInput += (forward * input.y + right * input.x);
                }
            }

            // 합산된 벡터 방향으로 힘 가하기 (이동)
            if (totalMoveInput.sqrMagnitude > 0.01f)
            {
                rb.AddForce(totalMoveInput.normalized * coopMoveForce, ForceMode.Force);
            }
        }
        else
        {
            // 인원이 부족하면 무거워서 안 움직이게 Kinematic 활성화 또는 마찰 극대화
            // rb.isKinematic = true;
        }
    }
}
