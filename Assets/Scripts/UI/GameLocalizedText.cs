using Michsky.UI.Heat;
using TMPro;
using UnityEngine;

// 런타임 HUD의 숫자·닉네임은 그대로 두고 문구만 갱신합니다.
[DisallowMultipleComponent]
public sealed class GameLocalizedText : MonoBehaviour
{
    private TMP_Text target;
    [SerializeField] private string key;
    private object[] arguments = System.Array.Empty<object>();
    private LocalizationLanguage language;

    private void OnEnable()
    {
        // 프리팹에 지정한 메뉴 문구도 현재 언어로 표시합니다.
        if (!string.IsNullOrEmpty(key)) Refresh();
    }

    public void Set(string newKey, params object[] newArguments)
    {
        key = newKey;
        arguments = newArguments;
        Refresh();
    }

    private void LateUpdate()
    {
        if (key != null && language != GameLocalization.Language) Refresh();
    }

    private void Refresh()
    {
        if (target == null) target = GetComponent<TMP_Text>();
        language = GameLocalization.Language;
        target.text = GameLocalization.Get(key, arguments);
    }
}
