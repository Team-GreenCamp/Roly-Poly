using Unity.Netcode;
using UnityEngine;

// 모든 클라이언트가 같은 서버 시각으로 장애물을 움직여 소유자 물리 충돌을 처리합니다.
[RequireComponent(typeof(Rigidbody))]
public class RaceObstacle : MonoBehaviour
{
    public enum Motion
    {
        Legacy,   // rotate 값에 따라 Y축 회전(true) 또는 X축 왕복(false)
        Pendulum, // 로컬 Z축 기준 좌우 진자(travel = 최대 각도, speed = 각속도)
        SpinX,    // 로컬 X축 기준 회전(굴러가는 통나무형)
    }

    public Motion motion = Motion.Legacy;
    public bool rotate = true;
    public float speed = 65f;
    public float travel = 3.5f;
    public float phase;

    [Header("피격")]
    [Tooltip("맞으면 넘어지며 피격 애니메이션을 재생한다.")]
    public bool knockdown = true;
    [Tooltip("맞은 방향(장애물이 움직이는 방향)으로 날려보내는 수평 임펄스.")]
    public float launchForce = 7f;
    [Tooltip("위로 띄우는 임펄스.")]
    public float launchLift = 2.5f;
    [Tooltip("장애물 표면 속도에 비례해 더하는 비율(빠르게 휘두를수록 멀리 날아감).")]
    public float velocityBoost = 0.3f;

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
        double time = CurrentTime();
        switch (motion)
        {
            case Motion.Pendulum:
                body.MoveRotation(rotation * Quaternion.Euler(0, 0, Mathf.Sin((float)(time * speed) + phase) * travel));
                break;
            case Motion.SpinX:
                body.MoveRotation(rotation * Quaternion.Euler((float)((time * speed + phase) % 360), 0, 0));
                break;
            default:
                if (rotate)
                    body.MoveRotation(rotation * Quaternion.Euler(0, (float)((time * speed + phase) % 360), 0));
                else
                    body.MovePosition(origin + Vector3.right * (Mathf.Sin((float)(time * speed) + phase) * travel));
                break;
        }
    }

    // 피격 임펄스: 접촉점에서 장애물이 움직이는 방향으로 밀고 위로 띄운다.
    // 움직임이 거의 없는 순간(진자 끝점 등)에는 장애물 중심에서 멀어지는 방향을 쓴다.
    public Vector3 ComputeHitImpulse(Vector3 contactPoint, Vector3 victimCenter)
    {
        Vector3 pointVelocity = GetPointVelocity(contactPoint);
        Vector3 planar = Vector3.ProjectOnPlane(pointVelocity, Vector3.up);
        Vector3 direction = planar.sqrMagnitude > 0.25f
            ? planar.normalized
            : Vector3.ProjectOnPlane(victimCenter - transform.position, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;
        return direction * (launchForce + planar.magnitude * velocityBoost) + Vector3.up * launchLift;
    }

    // 장애물 표면의 순간 속도를 움직임 공식에서 직접 구한다(kinematic이라 Rigidbody 속도에 의존하지 않음).
    public Vector3 GetPointVelocity(Vector3 worldPoint)
    {
        double time = CurrentTime();
        Vector3 r = worldPoint - transform.position;
        switch (motion)
        {
            case Motion.Pendulum:
            {
                float omega = Mathf.Cos((float)(time * speed) + phase) * speed * travel * Mathf.Deg2Rad;
                return Vector3.Cross(transform.forward * omega, r);
            }
            case Motion.SpinX:
                return Vector3.Cross(transform.right * (speed * Mathf.Deg2Rad), r);
            default:
                if (rotate) return Vector3.Cross(Vector3.up * (speed * Mathf.Deg2Rad), r);
                return Vector3.right * (Mathf.Cos((float)(time * speed) + phase) * speed * travel);
        }
    }

    private static double CurrentTime()
    {
        var network = NetworkManager.Singleton;
        return network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
    }
}
