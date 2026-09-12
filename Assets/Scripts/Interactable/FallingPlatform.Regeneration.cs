using System.Collections;
using Unity.Netcode;
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
    private bool RegenerationHidden => respawnTweenSeconds > 0f && IsFalling && DropTime > 0 && RegenerationClock >= DropTime;

    // 서버 시각으로 숨김/복귀를 계산하므로 클라이언트마다 같은 시점에 발판이 돌아옵니다.
    private IEnumerator RegenerateRoutine()
    {
        double drop = RegenerationClock + fallDelay;
        if (IsNetworkActive) regenerationDropTime.Value = drop;
        else localDropTime = drop;
        while (RegenerationClock < drop + respawnDelay + respawnTweenSeconds) yield return null;
        if (IsNetworkActive) { networkFalling.Value = false; regenerationDropTime.Value = 0; }
        else { localFalling = false; localDropTime = 0; SetSteppedTint(false); }
    }

    private void Update()
    {
        if (respawnTweenSeconds <= 0f) return;
        if (regenerationCollider == null) regenerationCollider = GetComponent<Collider>();
        bool hidden = RegenerationHidden;
        double elapsed = RegenerationClock - DropTime - respawnDelay;
        // SmoothStep 알파 트윈으로 서서히 나타난 뒤 충돌을 활성화합니다.
        float opacity = hidden ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)elapsed / respawnTweenSeconds)) : 1f;
        if (regenerationCollider != null) regenerationCollider.enabled = !hidden;
        foreach (var renderer in tintRenderers) if (renderer != null) renderer.enabled = opacity > 0f;
        if (!Mathf.Approximately(opacity, regenerationOpacity))
        {
            regenerationOpacity = opacity;
            ApplyTintWeight();
        }
    }
}
