using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 던지는 폭탄 (서버 권한). GrabbableObject와 같은 오브젝트에 붙여 잡기/던지기는 그대로 쓴다.
//
// • 집는 순간 도화선에 불이 붙고 fuseSeconds 뒤 터진다. 던진 폭탄이 플레이어를 맞히면 즉시 터진다.
// • 폭발 반경의 플레이어는 날아가며 넘어지고(들고 있던 사람 포함), 근처 폭탄은 연쇄로 터지고, 통 같은 물체는 밀려난다.
// • breakPlatforms면 폭발 지점 주변의 FallingPlatform도 무너진다(Falling Floors·Survival Arena).
// • 터진 폭탄은 인씬 NetworkObject라 Despawn 대신 숨겼다가 respawnSeconds 뒤 처음 자리에서 다시 나타난다.
// 깜빡임·째깍 소리·재등장은 서버 시각 기반 NetworkVariable로 계산해 모든 클라이언트가 같은 타이밍에 그린다.
[RequireComponent(typeof(GrabbableObject))]
public class ThrowableBomb : NetworkBehaviour
{
    [Header("도화선")]
    [SerializeField] private float fuseSeconds = 3f;
    [Tooltip("다른 폭발에 휘말렸을 때 터지기까지의 시간(초)")]
    [SerializeField] private float chainFuseSeconds = 0.2f;
    [Tooltip("던진 폭탄이 이 속도 이상으로 플레이어에 부딪히면 즉시 터진다")]
    [SerializeField] private float directHitMinSpeed = 3f;
    [Tooltip("손에서 떠난 직후 던진 사람 자신과 부딪혀 터지는 것을 막는 시간(초)")]
    [SerializeField] private float throwerGraceSeconds = 0.4f;

    [Header("폭발")]
    [SerializeField] private float blastRadius = 3.5f;
    [SerializeField] private float blastHorizontalForce = 14f;
    [SerializeField] private float blastUpwardForce = 15f;
    [Tooltip("반경 끝에서의 힘 비율(중심은 1)")]
    [SerializeField, Range(0f, 1f)] private float edgeForceRatio = 0.5f;
    [SerializeField] private float objectBlastForce = 9f;

    [Header("발판 붕괴")]
    [SerializeField] private bool breakPlatforms = true;
    [SerializeField] private float platformBreakRadius = 3f;
    [Tooltip("폭발 지점과 발판의 높이 차가 이 값 이하일 때만 무너뜨린다(공중 폭발·다른 층 제외)")]
    [SerializeField] private float platformBreakHeight = 2f;

    [Header("재등장")]
    [SerializeField] private float respawnSeconds = 8f;
    [SerializeField] private float popInSeconds = 0.35f;

    [Header("연출")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Transform sparkTip;
    [SerializeField, ColorUsage(false, true)] private Color warningEmission = new Color(2.6f, 0.35f, 0.1f);
    [SerializeField] private GameObject explosionVfx;
    [SerializeField] private float explosionVfxScale = 0.8f;
    [SerializeField] private AudioClip explosionSfx;
    [SerializeField] private AudioClip tickSfx;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;

    private readonly NetworkVariable<double> fuseEndTime = new NetworkVariable<double>(0);
    private readonly NetworkVariable<double> hiddenUntil = new NetworkVariable<double>(0);

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly Collider[] OverlapBuffer = new Collider[64];
    private static readonly HashSet<Rigidbody> PushedBodies = new HashSet<Rigidbody>();

    private GrabbableObject grabbable;
    private Rigidbody rb;
    private Collider[] colliders;
    private Renderer[] renderers;
    private NetworkTransform networkTransform;
    private MaterialPropertyBlock block;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 visualBaseScale = Vector3.one;
    private Vector3 sparkBaseScale = Vector3.one;
    private bool serverParked;
    private bool wasHeld;
    private double releaseTime;
    private int lastTickIndex = -1;

    private double Now => NetworkManager.ServerTime.Time;
    public bool IsHidden => hiddenUntil.Value > 0 && Now < hiddenUntil.Value;
    public bool IsArmed => fuseEndTime.Value > 0;

    private void Awake()
    {
        grabbable = GetComponent<GrabbableObject>();
        rb = GetComponent<Rigidbody>();
        networkTransform = GetComponent<NetworkTransform>();
        colliders = GetComponentsInChildren<Collider>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        homePosition = transform.position;
        homeRotation = transform.rotation;
        if (visualRoot != null) visualBaseScale = visualRoot.localScale;
        if (sparkTip != null) sparkBaseScale = sparkTip.localScale;
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            UpdateServer();
        }

        UpdateVisuals();
    }

    private void UpdateServer()
    {
        bool hidden = IsHidden;
        if (serverParked && !hidden)
        {
            // 재등장: 숨겨 둔 동안 고정해 둔 물리를 되돌린다.
            serverParked = false;
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        bool held = grabbable.IsBeingHeld;
        if (wasHeld && !held) releaseTime = Now;
        wasHeld = held;

        if (!hidden && !IsArmed && held)
        {
            fuseEndTime.Value = Now + fuseSeconds;
        }

        if (IsArmed && Now >= fuseEndTime.Value)
        {
            ExplodeOnServer();
        }
    }

    // 다른 폭발에 휘말렸을 때(서버). 이미 더 빨리 터질 예정이면 그대로 둔다.
    public void ServerIgnite(float seconds)
    {
        if (!IsSpawned || !IsServer || IsHidden)
        {
            return;
        }

        double end = Now + seconds;
        if (!IsArmed || fuseEndTime.Value > end)
        {
            fuseEndTime.Value = end;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (WillExplodeOnHit(collision))
        {
            ExplodeOnServer();
        }
    }

    // 서버: 이 충돌이 직격 폭발로 이어지는지. GrabbableObject도 같은 판정으로 자기 넉다운을 건너뛴다.
    public bool WillExplodeOnHit(Collision collision)
    {
        if (!IsSpawned || !IsServer || !IsArmed || IsHidden || grabbable.IsBeingHeld)
        {
            return false;
        }

        PlayerController victim = collision.collider.GetComponentInParent<PlayerController>();
        if (victim == null || collision.relativeVelocity.magnitude < directHitMinSpeed)
        {
            return false;
        }

        // 던진 직후 던진 사람 몸에 스치는 경우는 무시한다.
        return !(victim.OwnerClientId == grabbable.LastHolderClientId && Now - releaseTime < throwerGraceSeconds);
    }

    private void ExplodeOnServer()
    {
        Vector3 center = rb.worldCenterOfMass;
        ulong attacker = grabbable.LastHolderClientId;

        fuseEndTime.Value = 0;
        hiddenUntil.Value = Now + respawnSeconds;
        grabbable.ServerReleaseAll();
        ExplodeClientRpc(center);

        BlastPlayers(center, attacker);
        BlastObjects(center);
        if (breakPlatforms)
        {
            BreakPlatforms(center);
        }

        ParkAtHome();
    }

    private void BlastPlayers(Vector3 center, ulong attacker)
    {
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            PlayerController player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
            if (player == null)
            {
                continue;
            }

            Vector3 toPlayer = player.BodyCenter - center;
            float distance = toPlayer.magnitude;
            if (distance > blastRadius)
            {
                continue;
            }

            Vector3 flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
            Vector3 away = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            float falloff = Mathf.Lerp(1f, edgeForceRatio, distance / blastRadius);
            Vector3 force = (away * blastHorizontalForce + Vector3.up * blastUpwardForce) * falloff;
            player.ServerApplyBlast(force, player.BodyCenter - away * 0.2f, attacker);
        }
    }

    private void BlastObjects(Vector3 center)
    {
        PushedBodies.Clear();
        int count = Physics.OverlapSphereNonAlloc(center, blastRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = OverlapBuffer[i];
            ThrowableBomb otherBomb = hit.GetComponentInParent<ThrowableBomb>();
            if (otherBomb != null && otherBomb != this)
            {
                otherBomb.ServerIgnite(chainFuseSeconds);
                continue;
            }

            GrabbableObject other = hit.GetComponentInParent<GrabbableObject>();
            Rigidbody body = hit.attachedRigidbody;
            if (other == null || other == grabbable || other.IsBeingHeld || body == null || body.isKinematic || !PushedBodies.Add(body))
            {
                continue;
            }

            body.AddExplosionForce(objectBlastForce, center, blastRadius, 0.6f, ForceMode.VelocityChange);
        }
    }

    private void BreakPlatforms(Vector3 center)
    {
        // ServerForceFall이 목록을 바꾸지 않지만, 안전하게 뒤에서부터 순회한다.
        for (int i = FallingPlatform.ActivePlatforms.Count - 1; i >= 0; i--)
        {
            FallingPlatform platform = FallingPlatform.ActivePlatforms[i];
            Vector3 delta = platform.transform.position - center;
            if (Mathf.Abs(delta.y) > platformBreakHeight || platform.IsFalling)
            {
                continue;
            }

            delta.y = 0f;
            if (delta.magnitude <= platformBreakRadius)
            {
                platform.ServerForceFall();
            }
        }
    }

    // 숨기는 동안 처음 자리에 고정해 두었다가 재등장 때 그 자리에서 나타난다.
    private void ParkAtHome()
    {
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.position = homePosition;
        rb.rotation = homeRotation;
        transform.SetPositionAndRotation(homePosition, homeRotation);
        if (networkTransform != null)
        {
            networkTransform.Teleport(homePosition, homeRotation, transform.localScale);
        }

        serverParked = true;
    }

    [ClientRpc]
    private void ExplodeClientRpc(Vector3 position)
    {
        // 화면이 없는 데디케이티드 서버는 연출을 건너뛴다.
        if (!NetworkManager.IsClient)
        {
            return;
        }

        if (explosionVfx != null)
        {
            GameObject vfx = Instantiate(explosionVfx, position, Quaternion.identity);
            vfx.transform.localScale = explosionVfx.transform.localScale * explosionVfxScale;
            foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            Destroy(vfx, 5f);
        }

        if (explosionSfx != null)
        {
            AudioSource.PlayClipAtPoint(explosionSfx, position, sfxVolume * GameSettings.SfxVolume);
        }
    }

    private void UpdateVisuals()
    {
        bool hidden = IsHidden;
        foreach (Collider col in colliders)
        {
            if (col != null && col.enabled == hidden) col.enabled = !hidden;
        }

        foreach (Renderer r in renderers)
        {
            if (r != null && r.enabled == hidden) r.enabled = !hidden;
        }

        if (hidden)
        {
            lastTickIndex = -1;
            return;
        }

        // 재등장 직후 톡 튀어나오는 크기 연출.
        float pop = hiddenUntil.Value > 0 ? Mathf.Clamp01((float)(Now - hiddenUntil.Value) / Mathf.Max(0.01f, popInSeconds)) : 1f;
        float scale = EaseOutBack(pop);

        bool armed = IsArmed;
        bool lit = false;
        if (armed)
        {
            // 남은 시간이 줄수록 빨라지는 깜빡임(초당 2회 → 10회). 깜빡임마다 째깍 소리.
            float remaining = Mathf.Max(0f, (float)(fuseEndTime.Value - Now));
            float elapsed = Mathf.Max(0f, fuseSeconds - remaining);
            float beats = 2f * elapsed + 4f * elapsed * elapsed / Mathf.Max(0.1f, fuseSeconds);
            int index = Mathf.FloorToInt(beats);
            lit = beats - index < 0.5f;
            if (index != lastTickIndex)
            {
                lastTickIndex = index;
                if (tickSfx != null && NetworkManager.IsClient)
                {
                    AudioSource.PlayClipAtPoint(tickSfx, transform.position, sfxVolume * GameSettings.SfxVolume);
                }
            }

            scale *= lit ? 1.12f : 1f;
        }
        else
        {
            lastTickIndex = -1;
        }

        if (visualRoot != null)
        {
            visualRoot.localScale = visualBaseScale * scale;
        }

        if (bodyRenderer != null)
        {
            block ??= new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(block);
            block.SetColor(EmissionId, lit ? warningEmission : Color.black);
            bodyRenderer.SetPropertyBlock(block);
        }

        if (sparkTip != null)
        {
            sparkTip.gameObject.SetActive(armed);
            if (armed)
            {
                sparkTip.localScale = sparkBaseScale * Random.Range(0.75f, 1.35f);
            }
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
