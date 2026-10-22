using System.Collections.Generic;
using UnityEngine;

// 부딪히면 범퍼 중심 반대쪽으로 튕겨 내는 고정 범퍼 (순수 로컬, 네트워킹 없음).
//
// 튕김은 부딪힌 플레이어의 소유자 머신이 직접 처리한다(PlayerController.HandleBumperHit).
// 원격 사본은 kinematic이라 충돌 이벤트가 없으므로, 다른 플레이어가 닿는 것은 거리로 감지해 눌림 연출만 재생한다.
// 발판이 떨어지는 맵에서도 어색하지 않도록 바닥 위에 살짝 떠서 위아래로 일렁인다.
[DisallowMultipleComponent]
public class Bumper : MonoBehaviour
{
    [SerializeField] private float pushSpeed = 13f;
    [SerializeField] private float liftSpeed = 5f;
    [Tooltip("튕겨 나간 뒤 착지 전까지 공중 조작으로 속도가 줄지 않는 최대 시간(초)")]
    [SerializeField] private float controlLockSeconds = 0.6f;

    [Header("연출")]
    [SerializeField] private Transform bumpVisual;
    [SerializeField] private float bumpScale = 0.28f;
    [SerializeField] private float bumpRecoverSpeed = 3.5f;
    [SerializeField] private float hoverAmplitude = 0.06f;
    [SerializeField] private float hoverFrequency = 1.4f;
    [Tooltip("다른 플레이어가 이 거리(범퍼 중심 기준, 수평) 안으로 들어오면 닿은 것으로 본다")]
    [SerializeField] private float touchRadius = 1.45f;
    [SerializeField] private AudioClip bumpSfx;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.85f;

    public float PushSpeed => pushSpeed;
    public float LiftSpeed => liftSpeed;
    public float ControlLockSeconds => controlLockSeconds;

    private static readonly List<PlayerController> Players = new List<PlayerController>();
    private static float nextPlayerRefreshTime;
    private readonly HashSet<PlayerController> touching = new HashSet<PlayerController>();
    private float bump;
    private float phase;
    private Vector3 visualBasePosition;
    private Vector3 visualBaseScale = Vector3.one;

    private void Awake()
    {
        phase = Random.value * 10f;
        if (bumpVisual != null)
        {
            visualBasePosition = bumpVisual.localPosition;
            visualBaseScale = bumpVisual.localScale;
        }
    }

    public void PlayBump()
    {
        bump = 1f;
        if (bumpSfx != null)
        {
            AudioSource.PlayClipAtPoint(bumpSfx, transform.position, sfxVolume * GameSettings.SfxVolume);
        }
    }

    private void Update()
    {
        DetectRemoteTouches();

        if (bumpVisual == null)
        {
            return;
        }

        bump = Mathf.MoveTowards(bump, 0f, bumpRecoverSpeed * Time.deltaTime);
        // 맞는 순간 옆으로 부풀었다가 출렁이며 돌아온다.
        float wobble = Mathf.Sin((1f - bump) * Mathf.PI * 2.5f) * bump;
        float widen = 1f + bumpScale * wobble;
        bumpVisual.localScale = new Vector3(visualBaseScale.x * widen, visualBaseScale.y / Mathf.Max(0.5f, widen), visualBaseScale.z * widen);
        bumpVisual.localPosition = visualBasePosition + Vector3.up * (Mathf.Sin((Time.time + phase) * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude);
    }

    private void DetectRemoteTouches()
    {
        if (Time.time >= nextPlayerRefreshTime)
        {
            nextPlayerRefreshTime = Time.time + 1f;
            Players.Clear();
            Players.AddRange(FindObjectsByType<PlayerController>(FindObjectsSortMode.None));
        }

        foreach (PlayerController player in Players)
        {
            if (player == null || player.HasInputAuthority)
            {
                continue; // 로컬 플레이어는 실제 충돌(HandleBumperHit)에서 연출을 켠다
            }

            Vector3 offset = player.BodyCenter - transform.position;
            bool near = Mathf.Abs(offset.y) < 1.6f && Vector3.ProjectOnPlane(offset, Vector3.up).magnitude < touchRadius;
            if (near && touching.Add(player)) PlayBump();
            else if (!near) touching.Remove(player);
        }
    }
}
