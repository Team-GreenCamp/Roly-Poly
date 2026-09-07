using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬별 BGM을 재생하며 씬이 바뀌어도 하나의 재생기를 유지합니다.
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class BackgroundMusicController : MonoBehaviour
{
    [Serializable]
    public sealed class SceneMusic
    {
        [Tooltip("씬 이름 또는 Assets/로 시작하는 전체 씬 경로")]
        public string sceneName;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.5f;
    }

    [SerializeField] private SceneMusic[] sceneMusic = Array.Empty<SceneMusic>();
    [Tooltip("곡 교체 시 페이드 아웃/인의 각각의 시간(초). 0이면 즉시 전환")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.5f;

    private static BackgroundMusicController instance;
    private AudioSource source;
    private AudioClip requestedClip;
    private float requestedVolume;
    private float trackVolume;
    private float fadeLevel;

    // 어떤 씬에서 시작하든 Resources의 공통 프리팹을 자동 생성합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Application.isBatchMode || instance != null) return;
        var existing = FindFirstObjectByType<BackgroundMusicController>();
        if (existing != null) return;

        var prefab = Resources.Load<BackgroundMusicController>("Background Music");
        if (prefab != null) Instantiate(prefab);
    }

    private void Awake()
    {
        // 타이틀 재진입 등으로 생성된 중복 컨트롤러는 재생하지 않습니다.
        if (Application.isBatchMode || (instance != null && instance != this))
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        source = GetComponent<AudioSource>();
        source.Stop();
        source.clip = null;
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.ignoreListenerVolume = false;
    }

    private void OnEnable()
    {
        if (instance != this) return;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        PlayForScene(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        if (source != null) source.Stop();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void HandleActiveSceneChanged(Scene previous, Scene next)
    {
        // Additive로 보조 씬만 로드한 경우에는 현재 BGM을 유지합니다.
        PlayForScene(next);
    }

    private void PlayForScene(Scene scene)
    {
        // 등록되지 않은 씬이나 빈 Clip은 페이드 아웃 후 무음 처리합니다.
        requestedClip = null;
        requestedVolume = 0f;
        foreach (var entry in sceneMusic)
        {
            if (entry == null || (entry.sceneName != scene.name && entry.sceneName != scene.path)) continue;
            requestedClip = entry.clip;
            requestedVolume = Mathf.Clamp01(entry.volume);
            break;
        }
    }

    private void Update()
    {
        if (instance != this || source == null) return;

        // 빠른 연속 씬 전환에도 마지막 요청 곡으로 전환하며 timeScale=0에서도 페이드합니다.
        float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
        if (source.clip != requestedClip)
        {
            fadeLevel = Mathf.MoveTowards(fadeLevel, 0f, step);
            if (fadeLevel <= 0f)
            {
                source.Stop();
                source.clip = requestedClip;
                trackVolume = requestedVolume;
                if (requestedClip != null) source.Play();
            }
        }
        else if (requestedClip != null)
        {
            // 같은 곡은 재시작하지 않고 씬별 음량만 갱신합니다.
            trackVolume = requestedVolume;
            if (!source.isPlaying && !AudioListener.pause) source.Play();
            fadeLevel = Mathf.MoveTowards(fadeLevel, 1f, step);
        }

        // Master는 AudioListener가 처리하고 Music 슬라이더는 BGM에만 즉시 적용합니다.
        source.volume = trackVolume * fadeLevel * Mathf.Clamp01(GameSettings.MusicVolume);
    }
}
