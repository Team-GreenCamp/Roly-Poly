using System.Collections.Generic;
using Michsky.UI.Heat;
using UnityEngine;

/// <summary>
/// 타이틀과 인게임 메뉴의 Heat UI 컨트롤을 실제 게임 설정에 연결합니다.
/// </summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class TitleSettingsController : MonoBehaviour
{
    private readonly List<Vector2Int> displayResolutions = new();

    private void Awake()
    {
        // 자식 컨트롤을 이름으로 연결해 기존 씬 디자인을 그대로 유지합니다.
        BindSliders();
        BindSelectors();
        BindSwitches();
        GameSettings.ApplyRuntimeSettings();
    }

    private void BindSliders()
    {
        foreach (SliderManager slider in GetComponentsInChildren<SliderManager>(true))
        {
            switch (slider.transform.parent.name)
            {
                case "Master Volume":
                    ConfigureSlider(slider, "MasterVolume", GameSettings.SetMasterVolume);
                    break;
                case "Music Volume":
                    ConfigureSlider(slider, "MusicVolume", GameSettings.SetMusicVolume);
                    break;
                case "SFX Volume":
                    ConfigureSlider(slider, "SFXVolume", GameSettings.SetSfxVolume);
                    break;
                case "UI Volume":
                    ConfigureSlider(slider, "UIVolume", GameSettings.SetUiVolume);
                    break;
                case "Camera Sensitivity":
                    ConfigureSlider(slider, "CameraSensitivity", GameSettings.SetCameraSensitivity);
                    break;
            }
        }
    }

    private void BindSelectors()
    {
        foreach (HorizontalSelector selector in GetComponentsInChildren<HorizontalSelector>(true))
        {
            switch (selector.transform.parent.name)
            {
                case "Language":
                    // 언어 목록과 선택 복원은 씬의 LocalizationManager가 처리합니다.
                    selector.saveSelected = false;
                    break;
                case "UI Scale":
                    ConfigureSelector(selector, "UI Scale", GameSettings.SetUiScale);
                    break;
                case "Subtitle Scale":
                    ConfigureSelector(selector, "SubtitleScale", GameSettings.SetSubtitleScale);
                    break;
                case "Sprint Mode":
                    ConfigureSelector(selector, "SprintMode", GameSettings.SetSprintMode);
                    break;
                case "Frame Rate":
                    ConfigureSelector(selector, "FrameRate", GameSettings.SetFrameRate);
                    break;
                case "Resolution":
                    ConfigureResolutionSelector(selector);
                    break;
                case "Window Mode":
                    // 화면 모드는 해상도와 별도로 저장하므로 Heat UI의 인덱스 저장을 끕니다.
                    selector.saveSelected = false;
                    selector.invokeOnAwake = false;
                    selector.defaultIndex = GameSettings.WindowModeIndex;
                    selector.onValueChanged.AddListener(GameSettings.SetWindowMode);
                    break;
                case "Anisotropic Filtering":
                    ConfigureSelector(selector, "AnisotropicFiltering", GameSettings.SetAnisotropicFiltering);
                    break;
                case "Texture Quality":
                    ConfigureSelector(selector, "TextureQuality", GameSettings.SetTextureQuality);
                    break;
            }
        }
    }

    private void ConfigureResolutionSelector(HorizontalSelector selector)
    {
        // 주사율 중복을 빼고 현재 창 크기도 선택 목록에 표시합니다.
        displayResolutions.Clear();
        displayResolutions.Add(new Vector2Int(Screen.width, Screen.height));
        Vector2Int selected = GameSettings.DisplayResolution;
        if (!displayResolutions.Contains(selected))
        {
            displayResolutions.Add(selected);
        }

        foreach (Resolution resolution in Screen.resolutions)
        {
            Vector2Int size = new(resolution.width, resolution.height);
            if (!displayResolutions.Contains(size))
            {
                displayResolutions.Add(size);
            }
        }

        selector.items.Clear();
        for (int i = 0; i < displayResolutions.Count; i++)
        {
            Vector2Int size = displayResolutions[i];
            selector.items.Add(new HorizontalSelector.Item { itemTitle = $"{size.x} x {size.y}" });
            if (size == selected)
            {
                selector.defaultIndex = i;
            }
        }

        selector.saveSelected = false;
        selector.invokeOnAwake = false;
        selector.enableIndicator = false;
        selector.useLocalization = false;
        selector.onValueChanged.AddListener(index => GameSettings.SetDisplayResolution(displayResolutions[index]));
    }

    private void BindSwitches()
    {
        foreach (SwitchManager toggle in GetComponentsInChildren<SwitchManager>(true))
        {
            switch (toggle.transform.parent.name)
            {
                case "Enable Hints":
                    ConfigureSwitch(toggle, "EnableHints", GameSettings.SetHintsEnabled);
                    break;
                case "Enable Subtitles":
                    ConfigureSwitch(toggle, "EnableSubtitles", GameSettings.SetSubtitlesEnabled);
                    break;
                case "Reverse Look":
                    ConfigureSwitch(toggle, "ReverseLook", GameSettings.SetReverseLook);
                    break;
                case "VSync":
                    ConfigureSwitch(toggle, "VSync", GameSettings.SetVSync);
                    break;
            }
        }
    }

    private static void ConfigureSlider(SliderManager slider, string saveKey, UnityEngine.Events.UnityAction<float> callback)
    {
        // Heat UI의 저장 기능과 실제 적용 콜백을 함께 연결합니다.
        slider.saveValue = true;
        slider.saveKey = saveKey;
        slider.onValueChanged.AddListener(callback);
    }

    private static void ConfigureSelector(HorizontalSelector selector, string saveKey, UnityEngine.Events.UnityAction<int> callback)
    {
        // 선택 인덱스를 PlayerPrefs에 저장하고 즉시 적용합니다.
        selector.saveSelected = true;
        selector.saveKey = saveKey;
        selector.onValueChanged.AddListener(callback);
    }

    private static void ConfigureSwitch(SwitchManager toggle, string saveKey, UnityEngine.Events.UnityAction<bool> callback)
    {
        // 토글 상태를 PlayerPrefs에 저장하고 즉시 적용합니다.
        toggle.saveValue = true;
        toggle.saveKey = saveKey;
        toggle.onValueChanged.AddListener(callback);
    }
}
