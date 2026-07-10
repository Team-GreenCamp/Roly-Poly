using UnityEngine;

// 게임 이벤트별 SFX/VFX 원샷 재생기 (씬에 하나 배치, 순수 로컬 연출).
//
// 호출부는 전부 "이미 모든 클라이언트에서 동기화되어 실행되는 지점"이라 추가 네트워킹이 없다:
//  • 애니메이션 상태 전환(Hit/Roll/Attack/Spin) — PlayerController.Animation.ApplyBodyState
//  • 탈락 ClientRpc — SurvivalGameManager
//  • 발판 낙하 NetworkVariable — FallingPlatform
//  • HUD 카운트다운/서든데스 — SurvivalHudController(로컬)
//
// 클립/프리팹 연결은 Tools ▸ Feedback 클립 자동 연결 메뉴로 채운다.
// 씬에 이 컴포넌트가 없으면 모든 호출이 조용히 무시된다(로비 등 다른 씬 안전).
[DisallowMultipleComponent]
public class GameFeedback : MonoBehaviour
{
    [Header("피격 (넉다운/스턴 진입)")]
    [SerializeField] private GameObject[] hitVfx;
    [SerializeField] private AudioClip[] hitSfx;
    [SerializeField] private AudioClip[] hitVoice;

    [Header("대시 구르기")]
    [SerializeField] private GameObject[] dashVfx;
    [SerializeField] private AudioClip[] dashSfx;

    [Header("잡기/던지기 스윙")]
    [SerializeField] private AudioClip[] swingSfx;

    [Header("탈락")]
    [SerializeField] private AudioClip[] eliminationVoice;
    [SerializeField] private AudioClip[] uiPop;

    [Header("카운트다운/시작/서든데스")]
    [SerializeField] private AudioClip[] countdownTick;
    [SerializeField] private AudioClip[] goVoice;
    [SerializeField] private AudioClip[] suddenDeathVoice;

    [Header("승자")]
    [SerializeField] private GameObject[] winnerVfx;
    [SerializeField] private AudioClip[] winnerVoice;

    [Header("발판 붕괴")]
    [SerializeField] private GameObject[] floorFallVfx;

    [Header("볼륨")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float uiVolume = 0.7f;

    [Header("VFX 크기")]
    [Tooltip("모든 VFX에 곱해지는 스케일. HCFX 팩은 사람 크기 기준이라 동물 캐릭터(~0.9m)에겐 줄여서 쓴다.")]
    [SerializeField, Range(0.1f, 2f)] private float vfxScale = 0.45f;

    public static GameFeedback Instance { get; private set; }

    private AudioSource uiSource; // 2D 원샷(UI/아나운서)용

    private void Awake()
    {
        Instance = this;

        uiSource = gameObject.AddComponent<AudioSource>();
        uiSource.playOnAwake = false;
        uiSource.spatialBlend = 0f; // 2D
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ───── 정적 진입점 (인스턴스 없으면 무시) ─────
    public static void HitAt(Vector3 position)
    {
        if (Instance == null) return;
        Instance.SpawnVfx(Instance.hitVfx, position);
        Instance.PlayAt(Instance.hitSfx, position, Instance.sfxVolume);
        Instance.PlayAt(Instance.hitVoice, position, Instance.voiceVolume);
    }

    public static void DashAt(Vector3 position)
    {
        if (Instance == null) return;
        Instance.SpawnVfx(Instance.dashVfx, position);
        Instance.PlayAt(Instance.dashSfx, position, Instance.sfxVolume);
    }

    public static void SwingAt(Vector3 position)
    {
        if (Instance == null) return;
        Instance.PlayAt(Instance.swingSfx, position, Instance.sfxVolume);
    }

    public static void Elimination()
    {
        if (Instance == null) return;
        Instance.PlayUi(Instance.eliminationVoice, Instance.voiceVolume);
        Instance.PlayUi(Instance.uiPop, Instance.uiVolume);
    }

    public static void CountdownTick()
    {
        if (Instance == null) return;
        Instance.PlayUi(Instance.countdownTick, Instance.uiVolume);
    }

    public static void MatchStart()
    {
        if (Instance == null) return;
        Instance.PlayUi(Instance.goVoice, Instance.voiceVolume);
    }

    public static void SuddenDeath()
    {
        if (Instance == null) return;
        Instance.PlayUi(Instance.suddenDeathVoice, Instance.voiceVolume);
    }

    public static void WinnerAt(Vector3 position)
    {
        if (Instance == null) return;
        Instance.SpawnVfx(Instance.winnerVfx, position);
        Instance.PlayUi(Instance.winnerVoice, Instance.voiceVolume);
    }

    public static void PlatformFallAt(Vector3 position)
    {
        if (Instance == null) return;
        Instance.SpawnVfx(Instance.floorFallVfx, position);
    }

    // ───── 내부 재생 ─────
    private void PlayAt(AudioClip[] clips, Vector3 position, float volume)
    {
        AudioClip clip = Pick(clips);
        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
        }
    }

    private void PlayUi(AudioClip[] clips, float volume)
    {
        AudioClip clip = Pick(clips);
        if (clip != null && uiSource != null)
        {
            uiSource.PlayOneShot(clip, volume);
        }
    }

    private void SpawnVfx(GameObject[] prefabs, Vector3 position)
    {
        GameObject prefab = Pick(prefabs);
        if (prefab == null)
        {
            return;
        }

        GameObject vfx = Instantiate(prefab, position, Quaternion.identity);
        vfx.transform.localScale = prefab.transform.localScale * Mathf.Max(0.1f, vfxScale);

        // 파티클이 transform 스케일을 따르도록 강제(프리팹의 scalingMode 설정과 무관하게 적용).
        foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        Destroy(vfx, 5f); // 파티클 원샷 정리
    }

    private static T Pick<T>(T[] array) where T : Object
    {
        if (array == null || array.Length == 0)
        {
            return null;
        }

        return array[Random.Range(0, array.Length)];
    }
}
