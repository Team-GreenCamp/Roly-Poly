using System.Collections;
using Unity.Netcode;
using UnityEngine;

// 7번: 밟으면 떨어지는 발판 (서버 권한).
//
// 동기화하려면 이 오브젝트에 NetworkObject + NetworkTransform(Authority: Server)이 필요합니다.
// 이 게임의 플레이어는 소유자만 dynamic, 원격 프록시는 kinematic이라 "서버에서" 충돌이 잡히지 않습니다.
// 따라서 밟은 플레이어의 소유 클라이언트가 서버에 낙하를 요청하고, 서버만 물리를 굴려 NetworkTransform으로
// 위치를 모두에게 전파합니다. (NetworkObject가 없으면 기존처럼 각 클라이언트가 로컬로 처리합니다.)
[RequireComponent(typeof(NetworkObject))]
public class FallingPlatform : NetworkBehaviour
{
    [Header("설정")]
    [Tooltip("플레이어가 밟고 나서 떨어질 때까지의 대기 시간 (초)")]
    public float fallDelay = 1f;

    [Tooltip("떨어지고 나서 다시 원래 위치로 복귀하는 시간 (초). 0이면 복귀하지 않습니다.")]
    public float respawnDelay = 3f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Rigidbody rb;
    private bool localFalling = false; // 오프라인 폴백용

    private readonly NetworkVariable<bool> networkFalling =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject cachedNetworkObject;
    private bool IsNetworkActive => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private void Awake()
    {
        TryGetComponent(out cachedNetworkObject);

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;
        // 위에서 밟았을 때만 떨어집니다.
        if (collision.transform.position.y <= transform.position.y) return;

        if (!IsNetworkActive)
        {
            if (!localFalling) StartCoroutine(FallRoutineLocal());
            return;
        }

        if (networkFalling.Value) return;

        // 밟은 플레이어를 소유한 클라이언트만 서버에 낙하를 요청합니다.
        PlayerController pc = collision.gameObject.GetComponentInParent<PlayerController>();
        if (pc == null || !pc.HasInputAuthority) return;

        if (IsServer) BeginFallOnServer();
        else RequestFallServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestFallServerRpc(ServerRpcParams rpcParams = default)
    {
        BeginFallOnServer();
    }

    private void BeginFallOnServer()
    {
        if (networkFalling.Value) return;
        networkFalling.Value = true;
        StartCoroutine(ServerFallRoutine());
    }

    // 서버만 물리를 굴리고, 결과 위치는 NetworkTransform이 클라이언트에 전파합니다.
    private IEnumerator ServerFallRoutine()
    {
        yield return new WaitForSeconds(fallDelay);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);

            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.position = initialPosition;
            rb.rotation = initialRotation;
            transform.SetPositionAndRotation(initialPosition, initialRotation);

            networkFalling.Value = false;
        }
    }

    // ───── 오프라인(비네트워크) 폴백: 기존 동작 ─────
    private IEnumerator FallRoutineLocal()
    {
        localFalling = true;
        yield return new WaitForSeconds(fallDelay);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);

            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            transform.position = initialPosition;
            transform.rotation = initialRotation;

            localFalling = false;
        }
    }
}
