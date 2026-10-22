using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public partial class FallingPlatform
{
    [Tooltip("재생성 페이드 시간. 0이면 기존 물리 낙하 방식을 사용합니다.")]
    public float respawnTweenSeconds;
    private readonly NetworkVariable<double> regenerationDropTime = new NetworkVariable<double>(0);
    private double localDropTime;
    private float regenerationOpacity = 1f;
    private Collider regenerationCollider;
    private double RegenerationClock => IsNetworkActive ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    private double DropTime => IsNetworkActive ? regenerationDropTime.Value : localDropTime;
    // 낙하 시점 이후(물리 낙하 중 + 제자리 복귀 페이드 중)
    private bool RegenerationHidden => respawnTweenSeconds > 0f && IsFalling && DropTime > 0 && RegenerationClock >= DropTime;
    // 낙하가 끝나 원래 자리에서 다시 나타나는 중(이때만 숨김/충돌 해제)
    private bool RegenerationReturning => RegenerationHidden && RegenerationClock >= DropTime + respawnDelay;

    // 서버 시각으로 낙하/복귀를 계산하므로 클라이언트마다 같은 시점에 발판이 돌아옵니다.
    // 낙하 구간은 사라지는 대신 실제로 떨어지고(위치는 NetworkTransform으로 전파), 복귀 때 제자리로 순간이동한 뒤 페이드인합니다.
    private IEnumerator RegenerateRoutine()
    {
        double drop = RegenerationClock + fallDelay;
        if (IsNetworkActive) regenerationDropTime.Value = drop;
        else localDropTime = drop;
        while (RegenerationClock < drop) yield return null;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        while (RegenerationClock < drop + respawnDelay) yield return null;

        ResetToInitialPose();
        while (RegenerationClock < drop + respawnDelay + respawnTweenSeconds) yield return null;
        if (IsNetworkActive) { networkFalling.Value = false; regenerationDropTime.Value = 0; }
        else { localFalling = false; localDropTime = 0; SetSteppedTint(false); }
    }

    private void ResetToInitialPose()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.position = initialPosition;
        rb.rotation = initialRotation;
        transform.SetPositionAndRotation(initialPosition, initialRotation);

        // 보간으로 아래에서 미끄러져 올라오지 않도록 서버가 순간이동으로 전파한다.
        if (IsNetworkActive && IsServer && TryGetComponent(out NetworkTransform networkTransform))
        {
            networkTransform.Teleport(initialPosition, initialRotation, transform.localScale);
        }
    }

    private void Update()
    {
        if (respawnTweenSeconds <= 0f) return;
        if (regenerationCollider == null) regenerationCollider = GetComponent<Collider>();
        bool returning = RegenerationReturning;
        double elapsed = RegenerationClock - DropTime - respawnDelay;
        // 떨어지는 동안은 그대로 보이고, 복귀 때 SmoothStep 알파 트윈으로 서서히 나타난 뒤 충돌을 활성화합니다.
        float opacity = returning ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)elapsed / respawnTweenSeconds)) : 1f;
        if (regenerationCollider != null) regenerationCollider.enabled = !returning;
        foreach (var renderer in tintRenderers) if (renderer != null) renderer.enabled = opacity > 0f;
        if (!Mathf.Approximately(opacity, regenerationOpacity))
        {
            regenerationOpacity = opacity;
            ApplyTintWeight();
        }
    }
}
