using Michsky.UI.Heat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Heat UI의 기존 사운드 호출을 받아 모든 씬에서 버튼음을 재생합니다.
[DefaultExecutionOrder(-200)]
public sealed class UISoundController : UIManagerAudio
{
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;

    // UI의 Awake/Start보다 먼저 준비해 Heat UI가 사운드를 비활성화하지 않게 합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Application.isBatchMode || instance != null) return;
        var prefab = Resources.Load<UISoundController>("UI Sounds");
        if (prefab != null) Instantiate(prefab);
    }

    private void Awake()
    {
        // 씬 전환 중에도 클릭음이 끊기지 않도록 하나의 재생기를 유지합니다.
        if (Application.isBatchMode || (instance != null && instance != this))
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerVolume = false;
        UpdateVolume();
    }

    private void Start()
    {
        // 기본 클래스의 AudioMixer 초기화 대신 GameSettings 음량을 사용합니다.
        UpdateVolume();
    }

    private void OnEnable()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded += BindSceneButtons;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= BindSceneButtons;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        // 재생 중인 원샷에도 UI Volume 변경을 반영합니다. Master는 AudioListener가 처리합니다.
        UpdateVolume();
    }

    private void UpdateVolume()
    {
        if (audioSource != null) audioSource.volume = volume * Mathf.Clamp01(GameSettings.UiVolume);
    }

    private void BindSceneButtons(Scene scene, LoadSceneMode mode)
    {
        // Heat UI 버튼은 자체 이벤트를 사용하고 일반 Unity 버튼만 어댑터를 붙입니다.
        foreach (var root in scene.GetRootGameObjects())
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            if (UIButtonSound.HasHeatSound(button.gameObject) || button.GetComponent<UIButtonSound>() != null) continue;
            button.gameObject.AddComponent<UIButtonSound>();
        }
    }

    public void PlayClick()
    {
        // 일반 버튼과 Inspector 이벤트에서도 같은 클릭음을 호출할 수 있습니다.
        UpdateVolume();
        if (UIManagerAsset != null && UIManagerAsset.clickSound != null)
            audioSource.PlayOneShot(UIManagerAsset.clickSound);
    }

    public void PlayHover()
    {
        UpdateVolume();
        if (UIManagerAsset != null && UIManagerAsset.hoverSound != null)
            audioSource.PlayOneShot(UIManagerAsset.hoverSound);
    }
}
