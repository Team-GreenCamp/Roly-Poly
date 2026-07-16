using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 로비 우측 하단 닉네임 표시/변경 UI. 로비 씬에 빈 오브젝트로 배치하면 UI는 런타임에 스스로 만든다
// (플레이어 이름표(PlayerNameLabel)와 같은 방식 — 씬 UI 배선 불필요).
//
// 동작: 평소엔 현재 닉네임 버튼을 표시하고, 클릭하면 입력 필드로 전환된다.
// Enter/확인 → PlayerPrefs 저장 + 로컬 플레이어 NOA.SubmitNickname(서버 검증 → 전 클라 이름표 반영).
// Esc → 취소. 접속 전(로컬 플레이어 미스폰)에도 PlayerPrefs에는 저장되며, 스폰 시 NOA가 반영한다.
public class NicknameUIController : MonoBehaviour
{
    [Tooltip("한글 표시용 TMP 폰트(NanumGothic SDF 권장). 비우면 TMP 기본 폰트를 사용합니다.")]
    [SerializeField] private TMP_FontAsset uiFont;
    [Tooltip("화면 우측 하단 모서리 기준 패널 위치 오프셋(x는 왼쪽으로, y는 위로).")]
    [SerializeField] private Vector2 panelOffset = new Vector2(-16f, 16f);
    [SerializeField] private int canvasSortingOrder = 40;

    private GameObject viewRoot;          // 표시 모드(닉네임 버튼)
    private GameObject editRoot;          // 편집 모드(입력 필드 + 확인)
    private TextMeshProUGUI nameText;
    private TMP_InputField inputField;
    private float nextLabelRefreshTime;

    private void Awake()
    {
        BuildUI();
        SetEditing(false);
    }

    private void Update()
    {
        if (editRoot.activeSelf)
        {
            if (Keyboard.EscapePressedThisFrame())
            {
                SetEditing(false);
            }
            return;
        }

        // 로컬 플레이어 스폰 전에는 "Player ?"라서, 스폰/변경을 가볍게 폴링해 라벨을 갱신한다.
        if (Time.unscaledTime >= nextLabelRefreshTime)
        {
            nextLabelRefreshTime = Time.unscaledTime + 0.5f;
            RefreshNameLabel();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 상태 전환/적용
    // ─────────────────────────────────────────────────────────────
    private void SetEditing(bool editing)
    {
        viewRoot.SetActive(!editing);
        editRoot.SetActive(editing);

        if (editing)
        {
            inputField.text = PlayerPrefs.GetString(NetworkOwnedObjectActivator.NicknamePrefKey, string.Empty);
            inputField.ActivateInputField();
        }
        else
        {
            RefreshNameLabel();
        }
    }

    private void ApplyNickname()
    {
        string nickname = inputField.text != null ? inputField.text.Trim() : string.Empty;

        PlayerPrefs.SetString(NetworkOwnedObjectActivator.NicknamePrefKey, nickname);
        PlayerPrefs.Save();

        // 세션에 접속해 있으면 즉시 반영(서버 검증 → 전 클라이언트 이름표 갱신).
        NetworkOwnedObjectActivator activator = ResolveLocalActivator();
        if (activator != null)
        {
            activator.SubmitNickname(nickname);
        }

        SetEditing(false);
    }

    private void RefreshNameLabel()
    {
        // 닉네임만 표시(접두어 없음). 특수 글리프(✎ 등)는 NanumGothic SDF에 없어 □로 깨지므로 쓰지 않는다.
        string nickname = PlayerPrefs.GetString(NetworkOwnedObjectActivator.NicknamePrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            nameText.text = nickname;
            return;
        }

        // 저장된 닉네임이 없으면: 접속 중엔 기본 표시명(Player N), 아니면 설정 안내.
        NetworkOwnedObjectActivator activator = ResolveLocalActivator();
        nameText.text = activator != null ? activator.DisplayName : "닉네임 설정";
    }

    private static NetworkOwnedObjectActivator ResolveLocalActivator()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient || nm.SpawnManager == null)
        {
            return null;
        }

        NetworkObject localPlayer = nm.SpawnManager.GetLocalPlayerObject();
        return localPlayer != null ? localPlayer.GetComponent<NetworkOwnedObjectActivator>() : null;
    }

    // ─────────────────────────────────────────────────────────────
    // 런타임 UI 생성
    // ─────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // 표시 모드: 닉네임 버튼
        viewRoot = CreatePanel("NicknameView", new Vector2(260f, 44f));
        Button viewButton = viewRoot.GetComponent<Button>();
        viewButton.onClick.AddListener(() => SetEditing(true));
        nameText = CreateLabel(viewRoot.transform, "NameText", "닉네임 설정", TextAlignmentOptions.Center);

        // 편집 모드: 입력 필드 + 확인 버튼
        editRoot = CreatePanel("NicknameEdit", new Vector2(260f, 44f), interactable: false);
        BuildInputField(editRoot.transform);
        BuildConfirmButton(editRoot.transform);
    }

    // 우측 하단 고정 패널(배경 + 선택적으로 클릭 가능한 Button).
    private GameObject CreatePanel(string name, Vector2 size, bool interactable = true)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);

        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = panelOffset;
        rect.sizeDelta = size;

        Image background = panel.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);

        if (interactable)
        {
            Button button = panel.AddComponent<Button>();
            button.targetGraphic = background;
        }

        return panel;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string name, string text, TextAlignmentOptions alignment)
    {
        GameObject labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)labelObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 4f);
        rect.offsetMax = new Vector2(-12f, -4f);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 22f;
        label.color = Color.white;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        ApplyFont(label);
        return label;
    }

    private void BuildInputField(Transform parent)
    {
        GameObject fieldObject = new GameObject("NicknameInput", typeof(RectTransform), typeof(Image));
        fieldObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)fieldObject.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(4f, 0f);
        rect.sizeDelta = new Vector2(182f, -8f);

        Image background = fieldObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.12f);

        // 텍스트 영역(뷰포트) + 실제 텍스트
        GameObject textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(fieldObject.transform, false);
        RectTransform textAreaRect = (RectTransform)textArea.transform;
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10f, 3f);
        textAreaRect.offsetMax = new Vector2(-10f, -3f);

        TextMeshProUGUI placeholder = CreateFieldText(textArea.transform, "Placeholder", "닉네임 입력...");
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.fontStyle = FontStyles.Italic;

        TextMeshProUGUI text = CreateFieldText(textArea.transform, "Text", string.Empty);

        inputField = fieldObject.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.characterLimit = NetworkOwnedObjectActivator.NicknameMaxLength;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.onSubmit.AddListener(_ => ApplyNickname());
    }

    private TextMeshProUGUI CreateFieldText(Transform parent, string name, string content)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = content;
        label.fontSize = 20f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        ApplyFont(label);
        return label;
    }

    private void BuildConfirmButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("ConfirmButton", typeof(RectTransform), typeof(Image));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-4f, 0f);
        rect.sizeDelta = new Vector2(62f, -8f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.2f, 0.7f, 0.35f, 0.9f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(ApplyNickname);

        TextMeshProUGUI label = CreateLabel(buttonObject.transform, "Label", "확인", TextAlignmentOptions.Center);
        label.fontSize = 19f;
    }

    private void ApplyFont(TextMeshProUGUI label)
    {
        if (uiFont != null)
        {
            label.font = uiFont;
        }
    }

    // 구/신 Input System 양쪽에서 Esc 감지 (프로젝트는 New Input System 사용).
    private static class Keyboard
    {
        public static bool EscapePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
