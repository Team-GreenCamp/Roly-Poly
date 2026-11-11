using TMPro;
using Unity.Netcode;
using UnityEngine;

// 폭탄 돌리기(Hot Potato). 서버가 생존자 중 한 명에게 폭탄을 주고, 몸이 닿으면 상대에게 넘어간다.
// 시간이 다 되면 폭탄을 든 사람이 터져 탈락하고(주변은 날아감) 다음 라운드를 시작한다.
// 동기화는 폭탄 주인과 폭발 시각뿐이고, 폭탄 모형과 남은 시간 표시는 각 클라가 그린다.
[DisallowMultipleComponent]
public class HotPotatoController : NetworkBehaviour
{
    private const ulong NoHolder = ulong.MaxValue;

    [Header("라운드")]
    [SerializeField] private float firstFuseSeconds = 20f;
    [Tooltip("라운드마다 줄어드는 시간.")]
    [SerializeField] private float fuseStepSeconds = 2f;
    [SerializeField] private float minFuseSeconds = 8f;
    [Tooltip("시간에 더하는 무작위 폭(0~이 값). 정확한 타이밍 계산을 어렵게 한다.")]
    [SerializeField] private float fuseJitterSeconds = 3f;
    [Tooltip("폭발 후 다음 폭탄이 나오기까지의 시간.")]
    [SerializeField] private float roundBreakSeconds = 3f;

    [Header("넘기기")]
    [Tooltip("폭탄 주인과 이 거리 안에 들어온 플레이어에게 폭탄이 넘어간다.")]
    [SerializeField] private float passRadius = 1.4f;
    [Tooltip("넘긴 직후 다시 넘어가지 않는 시간(바로 되돌려 주기 방지).")]
    [SerializeField] private float passCooldownSeconds = 1f;

    [Header("폭발")]
    [SerializeField] private float blastRadius = 4f;
    [SerializeField] private float blastHorizontalForce = 9f;
    [SerializeField] private float blastUpwardForce = 5f;
    [SerializeField] private GameObject explosionVfx;
    [SerializeField] private float explosionVfxScale = 1f;

    [Header("연출")]
    [Tooltip("폭탄 주인 머리 위를 따라다니는 모형(네트워크 오브젝트가 아닌 순수 연출).")]
    [SerializeField] private Transform bombVisual;
    [SerializeField] private TextMeshPro countdownText;
    [SerializeField] private float headHeight = 2.4f;

    private readonly NetworkVariable<ulong> holder = new NetworkVariable<ulong>(NoHolder);
    private readonly NetworkVariable<double> fuseEndTime = new NetworkVariable<double>(0);

    private int round;
    private double nextRoundTime;
    private double passLockedUntil;
    private Vector3 visualBaseScale = Vector3.one;

    public ulong Holder => holder.Value;

    private void Awake()
    {
        if (bombVisual != null)
        {
            visualBaseScale = bombVisual.localScale;
            bombVisual.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (IsSpawned && IsServer)
        {
            UpdateServer();
        }

        UpdateVisual();
    }

    private void UpdateServer()
    {
        var manager = SurvivalGameManager.Instance;
        if (manager == null || manager.State != SurvivalGameManager.MatchState.Playing)
        {
            if (holder.Value != NoHolder)
            {
                holder.Value = NoHolder;
            }
            nextRoundTime = Now + roundBreakSeconds;
            return;
        }

        if (holder.Value == NoHolder)
        {
            if (Now >= nextRoundTime)
            {
                StartRound(manager);
            }
            return;
        }

        // 폭탄 주인이 다른 이유(낙사·접속 종료)로 빠지면 터뜨리지 않고 다음 라운드로 넘긴다.
        if (!manager.IsAliveOnServer(holder.Value) || !TryGetPlayer(holder.Value, out PlayerController holderPlayer))
        {
            holder.Value = NoHolder;
            nextRoundTime = Now + 1f;
            return;
        }

        if (Now >= fuseEndTime.Value)
        {
            Explode(manager, holderPlayer);
            return;
        }

        TryPass(manager, holderPlayer);
    }

    private void StartRound(SurvivalGameManager manager)
    {
        var candidates = new System.Collections.Generic.List<ulong>();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (manager.IsAliveOnServer(clientId) && TryGetPlayer(clientId, out _))
            {
                candidates.Add(clientId);
            }
        }

        if (candidates.Count == 0)
        {
            nextRoundTime = Now + 1f;
            return;
        }

        float fuse = Mathf.Max(minFuseSeconds, firstFuseSeconds - round * fuseStepSeconds) + Random.Range(0f, fuseJitterSeconds);
        round++;
        passLockedUntil = Now + passCooldownSeconds;
        fuseEndTime.Value = Now + fuse;
        holder.Value = candidates[Random.Range(0, candidates.Count)];
    }

    // 서버가 동기화된 플레이어 위치로 접촉을 판정한다(충돌 이벤트를 소유자에게서 받지 않아도 된다).
    private void TryPass(SurvivalGameManager manager, PlayerController holderPlayer)
    {
        if (Now < passLockedUntil)
        {
            return;
        }

        Vector3 from = holderPlayer.BodyCenter;
        ulong nearest = NoHolder;
        float nearestDistance = passRadius;
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId == holder.Value || !manager.IsAliveOnServer(clientId) || !TryGetPlayer(clientId, out PlayerController other))
            {
                continue;
            }

            float distance = Vector3.Distance(from, other.BodyCenter);
            if (distance <= nearestDistance)
            {
                nearest = clientId;
                nearestDistance = distance;
            }
        }

        if (nearest == NoHolder)
        {
            return;
        }

        holder.Value = nearest;
        passLockedUntil = Now + passCooldownSeconds;
        PassedClientRpc(from);
    }

    private void Explode(SurvivalGameManager manager, PlayerController holderPlayer)
    {
        ulong victim = holder.Value;
        Vector3 center = holderPlayer.BodyCenter;
        holder.Value = NoHolder;
        nextRoundTime = Now + roundBreakSeconds;
        ExplodeClientRpc(center);

        // 주변 플레이어는 날려 보내고(터진 주인이 공격자로 기록된다), 주인은 탈락시킨다.
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId == victim || !TryGetPlayer(clientId, out PlayerController player))
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
            Vector3 away = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            float falloff = Mathf.Lerp(1f, 0.4f, distance / blastRadius);
            player.ServerApplyBlast((away * blastHorizontalForce + Vector3.up * blastUpwardForce) * falloff, player.BodyCenter - away * 0.2f, victim);
        }

        manager.ServerEliminate(victim);
    }

    // 모든 클라: 폭탄 주인 머리 위에 모형을 띄우고, 남은 시간이 줄수록 빠르게 부풀었다 줄어든다.
    private void UpdateVisual()
    {
        if (bombVisual == null)
        {
            return;
        }

        Transform target = null;
        if (IsSpawned && holder.Value != NoHolder)
        {
            NetworkObject playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(holder.Value);
            target = playerObject != null ? playerObject.transform : null;
        }

        bool show = target != null;
        if (bombVisual.gameObject.activeSelf != show)
        {
            bombVisual.gameObject.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        double remaining = System.Math.Max(0, fuseEndTime.Value - Now);
        float urgency = Mathf.Clamp01(1f - (float)remaining / 10f);
        float pulse = 1f + 0.15f * (0.3f + urgency) * Mathf.Sin(Time.time * Mathf.Lerp(4f, 22f, urgency));
        bombVisual.position = target.position + Vector3.up * headHeight;
        bombVisual.localScale = visualBaseScale * pulse;

        if (countdownText != null)
        {
            countdownText.text = Mathf.CeilToInt((float)remaining).ToString();
            countdownText.color = Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f), urgency);
            Camera camera = Camera.main;
            if (camera != null)
            {
                countdownText.transform.rotation = Quaternion.LookRotation(countdownText.transform.position - camera.transform.position);
            }
        }
    }

    [ClientRpc]
    private void PassedClientRpc(Vector3 position)
    {
        GameFeedback.HitAt(position);
    }

    [ClientRpc]
    private void ExplodeClientRpc(Vector3 position)
    {
        if (!NetworkManager.IsClient)
        {
            return;
        }

        GameFeedback.HitAt(position);
        if (explosionVfx != null)
        {
            GameObject vfx = Instantiate(explosionVfx, position, Quaternion.identity);
            vfx.transform.localScale = explosionVfx.transform.localScale * explosionVfxScale;
            Destroy(vfx, 4f);
        }
    }

    private bool TryGetPlayer(ulong clientId, out PlayerController player)
    {
        player = null;
        return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            && client.PlayerObject != null
            && client.PlayerObject.TryGetComponent(out player);
    }

    private double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
}
