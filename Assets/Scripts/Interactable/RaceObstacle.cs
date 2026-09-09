using Unity.Netcode;
using UnityEngine;

// 모든 클라이언트가 같은 서버 시각으로 장애물을 움직여 소유자 물리 충돌을 처리합니다.
[RequireComponent(typeof(Rigidbody))]
public class RaceObstacle : MonoBehaviour
{
    public bool rotate = true;
    public float speed = 65f;
    public float travel = 3.5f;
    public float phase;
    private Rigidbody body;
    private Vector3 origin;
    private Quaternion rotation;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        origin = transform.position;
        rotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        var network = NetworkManager.Singleton;
        double time = network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
        if (rotate)
            body.MoveRotation(rotation * Quaternion.Euler(0, (float)((time * speed + phase) % 360), 0));
        else
            body.MovePosition(origin + Vector3.right * (Mathf.Sin((float)(time * speed) + phase) * travel));
    }
}
