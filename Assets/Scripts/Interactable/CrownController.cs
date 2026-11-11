using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 왕관 잡기(Hold the Crown) 왕관. 들고 있는 동안 서버가 일정 간격으로 점수를 준다.
// 점수·순위·HUD는 SurvivalCoinManager 점수판을 그대로 쓴다(코인 스폰은 끈 상태).
// 넘어지거나 밟히면 들고 있던 물체를 놓는 기존 규칙 덕분에 때리고 밟아서 뺏을 수 있다.
[DisallowMultipleComponent]
[RequireComponent(typeof(GrabbableObject))]
public class CrownController : NetworkBehaviour
{
    [SerializeField] private float pointInterval = 1f;
    [SerializeField] private int pointsPerInterval = 1;
    [Tooltip("이 높이 아래로 떨어지면 경기장 가운데로 되돌린다.")]
    [SerializeField] private float resetBelowY = -5f;

    [Header("연출")]
    [Tooltip("아무도 들고 있지 않을 때 위아래로 떠다니며 도는 표시(빛기둥 등).")]
    [SerializeField] private Transform beacon;
    [SerializeField] private float beaconSpinSpeed = 90f;

    private GrabbableObject grabbable;
    private Rigidbody body;
    private NetworkTransform networkTransform;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private float nextPointTime;
    private Quaternion beaconBaseRotation = Quaternion.identity;

    private void Awake()
    {
        if (beacon != null)
        {
            beaconBaseRotation = beacon.rotation;
        }
        grabbable = GetComponent<GrabbableObject>();
        TryGetComponent(out body);
        TryGetComponent(out networkTransform);
        homePosition = transform.position;
        homeRotation = transform.rotation;
    }

    private void Update()
    {
        if (beacon != null)
        {
            bool free = !grabbable.IsBeingHeld;
            if (beacon.gameObject.activeSelf != free)
            {
                beacon.gameObject.SetActive(free);
            }

            if (free)
            {
                beacon.position = transform.position + Vector3.up * 1.6f;
                beacon.rotation = Quaternion.Euler(0f, Time.time * beaconSpinSpeed, 0f) * beaconBaseRotation;
            }
        }

        if (IsSpawned && IsServer)
        {
            UpdateServer();
        }
    }

    private void UpdateServer()
    {
        if (transform.position.y < resetBelowY)
        {
            ResetToHome();
            return;
        }

        var manager = SurvivalGameManager.Instance;
        var scoreBoard = SurvivalCoinManager.Instance;
        if (manager == null || scoreBoard == null || manager.State != SurvivalGameManager.MatchState.Playing)
        {
            nextPointTime = Time.time + pointInterval;
            return;
        }

        if (Time.time < nextPointTime)
        {
            return;
        }

        nextPointTime = Time.time + pointInterval;
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (manager.IsAliveOnServer(clientId) && grabbable.IsHeldByClientOnServer(clientId))
            {
                scoreBoard.AddScoreOnServer(clientId, pointsPerInterval);
            }
        }
    }

    private void ResetToHome()
    {
        grabbable.ServerReleaseAll();
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (body != null)
        {
            body.position = homePosition;
            body.rotation = homeRotation;
        }

        transform.SetPositionAndRotation(homePosition, homeRotation);
        if (networkTransform != null)
        {
            networkTransform.Teleport(homePosition, homeRotation, transform.localScale);
        }
    }
}
