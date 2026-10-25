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

    // SetInts로 마지막에 적용한 정수 인수(같으면 다시 만들지 않음).
    private bool hasIntArguments;
    private int intCount, intA, intB, intC;

    public void Set(string newKey, params object[] newArguments)
    {
        hasIntArguments = false;
        key = newKey;
        arguments = newArguments;
        Refresh();
    }

    public void SetInts(string newKey, int count, int a, int b, int c)
    {
        if (hasIntArguments && key == newKey && intCount == count && intA == a && intB == b && intC == c)
        {
            return;
        }

        hasIntArguments = true;
        intCount = count;
        intA = a;
        intB = b;
        intC = c;
        key = newKey;
        arguments = count switch
        {
            0 => System.Array.Empty<object>(),
            1 => new object[] { a },
            2 => new object[] { a, b },
            _ => new object[] { a, b, c },
        };
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
