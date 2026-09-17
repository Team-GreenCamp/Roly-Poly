using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Michsky.UI.Heat;

// 로비 Canvas의 Profile/Text에 로컬 플레이어 닉네임을 표시한다.
public class NicknameUIController : MonoBehaviour
{
    [Header("Lobby UI References")]
    [SerializeField] private TMP_Text profileNameText;
    [SerializeField] private ModalWindowManager profilePopup;
    [SerializeField] private TMP_InputField nicknameInputField;

    private float nextLabelRefreshTime;

    private void Awake()
    {
        RegisterProfilePopupEvents();
        RefreshNameLabel();
    }

    private void OnDestroy()
    {
        if (profilePopup == null)
        {
            return;
        }

        profilePopup.onOpen.RemoveListener(PrepareNicknameInput);
        profilePopup.onConfirm.RemoveListener(ApplyNickname);
    }

    private void Update()
    {
        // 플레이어 스폰 또는 닉네임 변경 뒤에도 Profile 표시를 갱신한다.
        if (Time.unscaledTime < nextLabelRefreshTime)
        {
            return;
        }

        nextLabelRefreshTime = Time.unscaledTime + 0.5f;
        RefreshNameLabel();
    }

    private void RefreshNameLabel()
    {
        if (profileNameText == null)
        {
            return;
        }

        string nickname = GetCurrentNickname();
        if (profileNameText.text == nickname)
        {
            return;
        }

        profileNameText.text = nickname;

        // 현재 닉네임을 기준으로 텍스트와 Profile 레이아웃을 즉시 다시 계산한다.
        LayoutRebuilder.ForceRebuildLayoutImmediate(profileNameText.rectTransform);
        if (profileNameText.transform.parent is RectTransform profileRectTransform)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(profileRectTransform);
        }
    }

    private void RegisterProfilePopupEvents()
    {
        if (profilePopup == null)
        {
            return;
        }

        profilePopup.onOpen.AddListener(PrepareNicknameInput);
        profilePopup.onConfirm.AddListener(ApplyNickname);
    }

    private void PrepareNicknameInput()
    {
        if (nicknameInputField == null)
        {
            return;
        }

        nicknameInputField.text = string.Empty;
        nicknameInputField.characterLimit = NetworkOwnedObjectActivator.NicknameMaxLength;

        if (nicknameInputField.placeholder is TMP_Text placeholderText)
        {
            placeholderText.text = GetCurrentNickname();
        }

        nicknameInputField.ActivateInputField();
    }

    private void ApplyNickname()
    {
        if (nicknameInputField == null)
        {
            return;
        }

        string nickname = nicknameInputField.text != null ? nicknameInputField.text.Trim() : string.Empty;
        // 빈 입력은 기존 닉네임을 유지한다.
        if (string.IsNullOrEmpty(nickname))
        {
            return;
        }

        PlayerPrefs.SetString(NetworkOwnedObjectActivator.NicknamePrefKey, nickname);
        PlayerPrefs.Save();

        // 접속 중에는 서버 검증을 거쳐 다른 클라이언트의 이름표도 함께 갱신한다.
        NetworkOwnedObjectActivator activator = ResolveLocalActivator();
        if (activator != null)
        {
            activator.SubmitNickname(nickname);
        }

        RefreshNameLabel();
    }

    private static string GetCurrentNickname()
    {
        string nickname = PlayerPrefs.GetString(NetworkOwnedObjectActivator.NicknamePrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            return nickname;
        }

        NetworkOwnedObjectActivator activator = ResolveLocalActivator();
        return activator != null ? activator.DisplayName : "닉네임 설정";
    }

    private static NetworkOwnedObjectActivator ResolveLocalActivator()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsClient || networkManager.SpawnManager == null)
        {
            return null;
        }

        NetworkObject localPlayer = networkManager.SpawnManager.GetLocalPlayerObject();
        return localPlayer != null ? localPlayer.GetComponent<NetworkOwnedObjectActivator>() : null;
    }
}
