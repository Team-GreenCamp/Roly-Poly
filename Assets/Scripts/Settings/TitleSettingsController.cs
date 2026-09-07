using Michsky.UI.Heat;
using UnityEngine;

/// <summary>
/// Title Scene의 Heat UI 컨트롤을 실제 게임 설정에 연결합니다.
/// </summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class TitleSettingsController : MonoBehaviour
{
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
                    // 현재 프로젝트에 포함된 번역 데이터는 영어 하나뿐이라 샘플 선택지를 제거합니다.
                    selector.items.Clear();
                    selector.CreateNewItem("English");
                    selector.defaultIndex = 0;
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
                case "Anisotropic Filtering":
                    ConfigureSelector(selector, "AnisotropicFiltering", GameSettings.SetAnisotropicFiltering);
                    break;
                case "Texture Quality":
                    ConfigureSelector(selector, "TextureQuality", GameSettings.SetTextureQuality);
                    break;
            }
        }
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
