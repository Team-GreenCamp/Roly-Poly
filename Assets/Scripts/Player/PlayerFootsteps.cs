using UnityEngine;

/// <summary>
/// 이동 거리 기반 발소리 재생기.
/// 트랜스폼 위치 변화만 관측하므로 네트워크 동기화 없이
/// 로컬/원격 플레이어 모두 각 클라이언트에서 동일하게 재생된다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerFootsteps : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip[] footstepClips;

    [Header("Stride")]
    [Tooltip("한 걸음으로 간주하는 이동 거리(m). 작을수록 발소리가 잦아집니다.")]
    [SerializeField] private float strideLength = 1.9f;
    [Tooltip("이 속도(m/s) 미만이면 정지로 간주하고 발소리를 내지 않습니다.")]
    [SerializeField] private float minSpeed = 0.8f;
    [Tooltip("한 프레임에 이 거리(m) 이상 이동하면 텔레포트/리스폰으로 간주하고 무시합니다.")]
    [SerializeField] private float teleportDistanceThreshold = 3f;

    [Header("Sound")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.55f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.92f, 1.08f);
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 18f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private float groundCheckOffset = 0.1f;
    [SerializeField] private LayerMask groundLayers = ~0;

    private AudioSource source;
    private Vector3 lastPosition;
    private float distanceAccum;
    private int lastClipIndex = -1;
    private readonly Collider[] groundHits = new Collider[8];

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        lastPosition = transform.position;
    }

    private void OnEnable()
    {
        lastPosition = transform.position;
        distanceAccum = 0f;
    }

    private void Update()
    {
        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        delta.y = 0f;

        float distance = delta.magnitude;
        if (distance >= teleportDistanceThreshold)
        {
            distanceAccum = 0f;
            return;
        }

        float speed = Time.deltaTime > 0f ? distance / Time.deltaTime : 0f;
        if (speed < minSpeed || !IsGrounded())
        {
            distanceAccum = 0f;
            return;
        }

        distanceAccum += distance;
        if (distanceAccum >= strideLength)
        {
            distanceAccum -= strideLength;
            PlayStep();
        }
    }

    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * groundCheckOffset;
        int count = Physics.OverlapSphereNonAlloc(origin, groundCheckRadius, groundHits, groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            // 자기 자신(하위 포함)의 콜라이더는 지면으로 치지 않는다.
            if (!groundHits[i].transform.IsChildOf(transform))
                return true;
        }
        return false;
    }

    private void PlayStep()
    {
        if (footstepClips == null || footstepClips.Length == 0)
            return;

        int index = Random.Range(0, footstepClips.Length);
        if (footstepClips.Length > 1 && index == lastClipIndex)
            index = (index + 1) % footstepClips.Length;
        lastClipIndex = index;

        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.PlayOneShot(footstepClips[index], volume);
    }
}
