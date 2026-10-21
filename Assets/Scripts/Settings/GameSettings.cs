using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 저장된 사용자 설정을 모든 게임 씬에 적용하는 전역 진입점입니다.
/// </summary>
public static class GameSettings
{
    private const string MasterVolumeKey = "Slider_MasterVolume";
    private const string MusicVolumeKey = "Slider_MusicVolume";
    private const string SfxVolumeKey = "Slider_SFXVolume";
    private const string UiVolumeKey = "Slider_UIVolume";
    private const string CameraSensitivityKey = "Slider_CameraSensitivity";
    private const string UiScaleKey = "HorizontalSelector_UI Scale";
    private const string SprintModeKey = "HorizontalSelector_SprintMode";
    private const string FrameRateKey = "HorizontalSelector_FrameRate";
    private const string AnisotropicFilteringKey = "HorizontalSelector_AnisotropicFiltering";
    private const string TextureQualityKey = "HorizontalSelector_TextureQuality";
    private const string DisplayWidthKey = "Display_Width";
    private const string DisplayHeightKey = "Display_Height";
    private const string DisplayModeKey = "Display_Mode";
    private const string HintsKey = "Switch_EnableHints";
    private const string ReverseLookKey = "Switch_ReverseLook";
    private const string VSyncKey = "Switch_VSync";

    private static readonly float[] ScaleValues = { 0.25f, 0.5f, 1f, 1.5f, 2f };
    private static readonly int[] FrameRates = { -1, 30, 60, 144, 240 };
    private static readonly Dictionary<CanvasScaler, Vector2> CanvasReferenceResolutions = new();

    public static float MasterVolume => PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
    public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
    public static float SfxVolume => PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
    public static float UiVolume => PlayerPrefs.GetFloat(UiVolumeKey, 1f);
    public static float CameraSensitivity => PlayerPrefs.GetFloat(CameraSensitivityKey, 2f);
    public static bool UseToggleSprint => PlayerPrefs.GetInt(SprintModeKey, 0) == 0;
    public static bool HintsEnabled => ReadBool(HintsKey, true);
    public static Vector2Int DisplayResolution => new(PlayerPrefs.GetInt(DisplayWidthKey, Screen.width), PlayerPrefs.GetInt(DisplayHeightKey, Screen.height));
    public static int WindowModeIndex => Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey, Screen.fullScreenMode == FullScreenMode.Windowed ? 0 : 1), 0, 1);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // 씬 전환 뒤 새로 생성된 카메라와 UI에도 설정을 다시 적용합니다.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ApplyDisplaySettings();
        ApplyRuntimeSettings();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyRuntimeSettings();
    }

    public static void ApplyRuntimeSettings()
    {
        // 저장값을 Unity 런타임 옵션과 현재 씬 오브젝트에 반영합니다.
        AudioListener.volume = Mathf.Clamp01(MasterVolume);
        ApplyFrameRate(PlayerPrefs.GetInt(FrameRateKey, 0));
        ApplyVSync(ReadBool(VSyncKey, false));
        ApplyAnisotropicFiltering(PlayerPrefs.GetInt(AnisotropicFilteringKey, 2));
        ApplyTextureQuality(PlayerPrefs.GetInt(TextureQualityKey, 3));
        ApplyUiScale(PlayerPrefs.GetInt(UiScaleKey, 2));
        ApplyHintSettings();
        ApplyCameraSettings();
    }

    public static void SetMasterVolume(float value)
    {
        // 전체 음량은 AudioListener에 즉시 적용합니다.
        value = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MasterVolumeKey, value);
        AudioListener.volume = value;
    }

    public static void SetMusicVolume(float value)
    {
        // 음악 시스템이 공통 배율을 읽을 수 있도록 저장합니다.
        PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
    }

    public static void SetSfxVolume(float value)
    {
        // 게임 효과음에서 사용하는 공통 배율을 저장합니다.
        PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
    }

    public static void SetUiVolume(float value)
    {
        // UI 및 아나운서 효과음에서 사용하는 공통 배율을 저장합니다.
        PlayerPrefs.SetFloat(UiVolumeKey, Mathf.Clamp01(value));
    }

    public static void SetCameraSensitivity(float value)
    {
        // Cinemachine Look 축의 감도를 즉시 갱신합니다.
        PlayerPrefs.SetFloat(CameraSensitivityKey, Mathf.Clamp(value, 0.1f, 5f));
        ApplyCameraSettings();
    }

    public static void SetUiScale(int index)
    {
        // 모든 CanvasScaler의 기준 해상도를 조절해 UI 크기를 변경합니다.
        index = ClampIndex(index, ScaleValues.Length);
        PlayerPrefs.SetInt(UiScaleKey, index);
        ApplyUiScale(index);
    }

    public static void SetSprintMode(int index)
    {
        // 0은 토글, 1은 누르고 있는 동안 달리기입니다.
        PlayerPrefs.SetInt(SprintModeKey, Mathf.Clamp(index, 0, 1));
    }

    public static void SetFrameRate(int index)
    {
        // 선택한 목표 프레임을 즉시 적용합니다.
        index = ClampIndex(index, FrameRates.Length);
        PlayerPrefs.SetInt(FrameRateKey, index);
        ApplyFrameRate(index);
    }

    public static void SetAnisotropicFiltering(int index)
    {
        // 비등방성 필터링의 전역 품질을 적용합니다.
        index = Mathf.Clamp(index, 0, 2);
        PlayerPrefs.SetInt(AnisotropicFilteringKey, index);
        ApplyAnisotropicFiltering(index);
    }

    public static void SetTextureQuality(int index)
    {
        // Low부터 Ultra까지 텍스처 밉맵 제한을 적용합니다.
        index = Mathf.Clamp(index, 0, 3);
        PlayerPrefs.SetInt(TextureQualityKey, index);
        ApplyTextureQuality(index);
    }

    public static void SetDisplayResolution(Vector2Int resolution)
    {
        // 선택한 해상도를 저장하고 현재 화면에 바로 반영합니다.
        PlayerPrefs.SetInt(DisplayWidthKey, resolution.x);
        PlayerPrefs.SetInt(DisplayHeightKey, resolution.y);
        PlayerPrefs.Save();
        ApplyDisplaySettings();
    }

    public static void SetWindowMode(int index)
    {
        // 0은 창모드, 1은 전체화면 창모드입니다.
        if (!PlayerPrefs.HasKey(DisplayWidthKey))
        {
            PlayerPrefs.SetInt(DisplayWidthKey, Screen.width);
            PlayerPrefs.SetInt(DisplayHeightKey, Screen.height);
        }

        PlayerPrefs.SetInt(DisplayModeKey, Mathf.Clamp(index, 0, 1));
        PlayerPrefs.Save();
        ApplyDisplaySettings();
    }

    public static void SetHintsEnabled(bool enabled)
    {
        // 힌트 텍스트 표시 여부를 저장하고 현재 씬에 적용합니다.
        WriteBool(HintsKey, enabled);
        ApplyHintSettings();
    }

    public static void SetReverseLook(bool enabled)
    {
        // 세로 Look 축의 방향만 반전합니다.
        WriteBool(ReverseLookKey, enabled);
        ApplyCameraSettings();
    }

    public static void SetVSync(bool enabled)
    {
        // VSync를 켜면 모니터 주사율에 맞춰 렌더링합니다.
        WriteBool(VSyncKey, enabled);
        ApplyVSync(enabled);
    }

    private static void ApplyFrameRate(int index)
    {
        Application.targetFrameRate = FrameRates[ClampIndex(index, FrameRates.Length)];
    }

    private static void ApplyDisplaySettings()
    {
        if (!PlayerPrefs.HasKey(DisplayWidthKey) && !PlayerPrefs.HasKey(DisplayModeKey))
        {
            return;
        }

        Vector2Int resolution = DisplayResolution;
        FullScreenMode mode = PlayerPrefs.HasKey(DisplayModeKey)
            ? (WindowModeIndex == 0 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow)
            : Screen.fullScreenMode;
        Screen.SetResolution(resolution.x, resolution.y, mode);
    }

    private static void ApplyVSync(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
    }

    private static void ApplyAnisotropicFiltering(int index)
    {
        QualitySettings.anisotropicFiltering = index switch
        {
            0 => AnisotropicFiltering.Disable,
            1 => AnisotropicFiltering.Enable,
            _ => AnisotropicFiltering.ForceEnable
        };
    }

    private static void ApplyTextureQuality(int index)
    {
        // Unity의 밉맵 제한은 숫자가 작을수록 고품질입니다.
        QualitySettings.globalTextureMipmapLimit = 3 - Mathf.Clamp(index, 0, 3);
    }

    private static void ApplyUiScale(int index)
    {
        float scale = ScaleValues[ClampIndex(index, ScaleValues.Length)];
        CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (CanvasScaler scaler in scalers)
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
            {
                scaler.scaleFactor = scale;
                continue;
            }

            if (!CanvasReferenceResolutions.TryGetValue(scaler, out Vector2 baseResolution))
            {
                baseResolution = scaler.referenceResolution;
                CanvasReferenceResolutions[scaler] = baseResolution;
            }

            scaler.referenceResolution = baseResolution / scale;
        }
    }

    private static void ApplyCameraSettings()
    {
        float sensitivity = CameraSensitivity;
        bool reverseLook = ReadBool(ReverseLookKey, false);
        CinemachineInputAxisController[] controllers = Object.FindObjectsByType<CinemachineInputAxisController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (CinemachineInputAxisController axisController in controllers)
        {
            foreach (var controller in axisController.Controllers)
            {
                if (controller?.Input == null)
                {
                    continue;
                }

                if (controller.Name == "Look Orbit X")
                {
                    controller.Input.Gain = sensitivity;
                }
                else if (controller.Name == "Look Orbit Y")
                {
                    controller.Input.Gain = sensitivity * (reverseLook ? 1f : -1f);
                }
            }
        }
    }

    private static void ApplyHintSettings()
    {
        // 설정 패널 자체를 제외하고 이름에 Hint가 있는 텍스트만 제어합니다.
        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.name.Contains("Hint", System.StringComparison.OrdinalIgnoreCase) && !IsInsideSettingsPanel(text.transform))
            {
                text.gameObject.SetActive(HintsEnabled);
            }
        }
    }

    private static bool IsInsideSettingsPanel(Transform target)
    {
        // Settings 조상 아래의 미리보기/라벨은 전역 표시 설정에서 제외합니다.
        while (target != null)
        {
            if (target.name == "Settings")
            {
                return true;
            }

            target = target.parent;
        }

        return false;
    }

    private static bool ReadBool(string key, bool defaultValue)
    {
        return PlayerPrefs.GetString(key, defaultValue ? "true" : "false") == "true";
    }

    private static void WriteBool(string key, bool value)
    {
        PlayerPrefs.SetString(key, value ? "true" : "false");
    }

    private static int ClampIndex(int index, int count)
    {
        return Mathf.Clamp(index, 0, count - 1);
    }
}
