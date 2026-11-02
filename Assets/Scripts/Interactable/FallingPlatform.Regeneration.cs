using UnityEngine;

public partial class FallingPlatform
{
    [Tooltip("재생성 페이드 시간. 0이면 제자리로 돌아올 때 바로 나타납니다.")]
    public float respawnTweenSeconds;
    private float regenerationOpacity = 1f;

    // 떨어졌다가 제자리로 돌아와 다시 나타나는 중인지(이때만 숨김·충돌 해제).
    private bool RegenerationReturning => respawnTweenSeconds > 0f && respawnDelay > 0f && IsFalling && DropTime > 0
        && FallClock >= DropTime + respawnDelay;

    // 낙하 중인 발판만 위치를 계산하고, 재생성 발판은 복귀 페이드(서버 시각 기준이라 클라이언트마다 같은 시점)를 맞춘다.
    // 가만히 있는 수백 장은 첫 줄에서 바로 빠진다.
    private void Update()
    {
        if (!fallActive && regenerationOpacity >= 1f)
        {
            return;
        }

        if (fallActive)
        {
            UpdateFallPose();
        }

        if (respawnTweenSeconds <= 0f)
        {
            return;
        }

        float opacity = 1f;
        if (RegenerationReturning)
        {
            double elapsed = FallClock - DropTime - respawnDelay;
            opacity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)elapsed / respawnTweenSeconds));
        }

        if (Mathf.Approximately(opacity, regenerationOpacity))
        {
            return;
        }

        bool visible = opacity > 0f;
        if (visible != regenerationOpacity > 0f)
        {
            foreach (var renderer in tintRenderers) if (renderer != null) renderer.enabled = visible;
        }

        regenerationOpacity = opacity;
        ApplyTintWeight();
    }
}
