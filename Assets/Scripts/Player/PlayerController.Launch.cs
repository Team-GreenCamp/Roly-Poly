using UnityEngine;

// 범퍼가 플레이어를 날려 보내는 경로(소유자 로컬).
// 플레이어는 소유자 권한 물리라 장치와 부딪힌 소유자 머신이 직접 속도를 정하고, 위치는 기존 동기화로 퍼진다.
// 날아가는 동안에는 공중 조작이 수평 속도를 깎지 않도록 궤적을 그대로 유지한다.
public partial class PlayerController
{
    private float launchUntil;
    private float launchStartTime;
    private float nextBumperHitTime;

    public bool IsLaunched => Time.time < launchUntil;

    // 속도를 직접 정하고, 착지할 때까지(최대 maxSeconds) 공중 조작으로 감속하지 않는다.
    public void LaunchLocal(Vector3 velocity, float maxSeconds)
    {
        if (!HasInputAuthority || physicsBody == null || physicsBody.isKinematic || !CanProcessInput())
        {
            return;
        }

        physicsBody.linearVelocity = velocity;
        isGrounded = false;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        landingSpeedPreserveTimer = 0f;
        launchStartTime = Time.time;
        launchUntil = Time.time + Mathf.Max(0.1f, maxSeconds);
    }

    // UpdateMovement 앞에서 호출. true면 이번 프레임은 일반 이동 처리를 건너뛴다.
    private bool UpdateLaunchFlight()
    {
        if (Time.time >= launchUntil)
        {
            return false;
        }

        // 막 뜬 직후의 접지 판정은 무시하고, 그 뒤 착지하면 발사 상태를 끝낸다.
        if (isGrounded && Time.time - launchStartTime > 0.15f)
        {
            launchUntil = 0f;
            return false;
        }

        Vector3 planar = Vector3.ProjectOnPlane(physicsBody.linearVelocity, Vector3.up);
        currentHorizontalVelocity = planar;
        if (planar.sqrMagnitude > 0.01f)
        {
            ApplyTurnTorque(planar.normalized);
        }

        return true;
    }

    // 범퍼에 부딪힘(소유자 로컬 판정): 범퍼 중심 반대쪽으로 튕겨 나간다. 넘어지지는 않는다.
    private void HandleBumperHit(Bumper bumper)
    {
        if (Time.time < nextBumperHitTime)
        {
            return;
        }

        nextBumperHitTime = Time.time + 0.2f;
        Vector3 away = Vector3.ProjectOnPlane(BodyCenter - bumper.transform.position, Vector3.up);
        if (away.sqrMagnitude < 0.0001f)
        {
            away = -Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        LaunchLocal(away.normalized * bumper.PushSpeed + Vector3.up * bumper.LiftSpeed, bumper.ControlLockSeconds);
        bumper.PlayBump();
    }
}
