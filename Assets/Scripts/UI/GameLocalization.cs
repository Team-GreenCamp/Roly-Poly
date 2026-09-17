using System.Collections.Generic;
using System.Globalization;
using Michsky.UI.Heat;
using TMPro;
using UnityEngine;

// 코드에서 만드는 문구도 HEAT의 언어 테이블과 저장된 언어를 사용합니다.
public static class GameLocalization
{
    private static UIManager uiManager;
    private static LocalizationLanguage cachedLanguage;
    private static readonly Dictionary<string, string> translations = new();

    public static LocalizationLanguage Language
    {
        get
        {
            if (LocalizationManager.instance != null && LocalizationManager.instance.currentLanguageAsset != null)
                return LocalizationManager.instance.currentLanguageAsset;
            if (uiManager == null) uiManager = Resources.Load<UIManager>("Heat UI Manager");
            if (uiManager == null || uiManager.localizationSettings == null) return null;
            var settings = uiManager.localizationSettings;
            string saved = PlayerPrefs.GetString(UIManager.localizationSaveKey, settings.defaultLanguageID);
            foreach (var language in settings.languages)
                if (saved == language.languageID || saved == language.languageName + " (" + language.languageID + ")")
                    return language.localizationLanguage;
            return settings.languages.Count > 0
                ? settings.languages[Mathf.Clamp(settings.defaultLanguageIndex, 0, settings.languages.Count - 1)].localizationLanguage
                : null;
        }
    }

    public static string Get(string key, params object[] arguments)
    {
        var language = Language;
        if (language != cachedLanguage)
        {
            cachedLanguage = language;
            translations.Clear();
            if (language != null)
                foreach (var table in language.tableList)
                    foreach (var entry in table.tableContent)
                        if (!string.IsNullOrEmpty(entry.key) && !string.IsNullOrEmpty(entry.value))
                            translations[entry.key] = entry.value;
        }
        string value = translations.TryGetValue(key, out var translated) ? translated : key;
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    // 표시 중인 이벤트 문구도 언어를 바꾸면 인수를 유지한 채 다시 번역합니다.
    public static void Set(TMP_Text text, string key, params object[] arguments)
    {
        if (text == null) return;
        if (!text.TryGetComponent<GameLocalizedText>(out var binding))
            binding = text.gameObject.AddComponent<GameLocalizedText>();
        binding.Set(key, arguments);
    }

    // 서버의 맵 식별자는 유지하고 화면에 표시하는 이름만 번역합니다.
    public static string MapName(string mapId)
    {
        return mapId switch
        {
            "chapter-1" => Get("MapSurvival"),
            "chapter-2" => Get("MapSumo"),
            "chapter-3" => Get("MapFalling"),
            _ => string.IsNullOrWhiteSpace(mapId) ? Get("MapUnspecified") : mapId
        };
    }
}
