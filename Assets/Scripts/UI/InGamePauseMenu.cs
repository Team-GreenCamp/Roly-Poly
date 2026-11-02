using System.Collections;
using System.Collections.Generic;
using Michsky.UI.Heat;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 네트워크 게임은 계속 진행하고, 메뉴를 연 클라이언트의 입력만 차단합니다.
[DefaultExecutionOrder(-600)]
[DisallowMultipleComponent]
public sealed class InGamePauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private ModalWindowManager settingsWindow;
    // Heat 버튼을 기본 선택 대상으로 지정해 키보드 탐색과 강조 효과를 유지합니다.
    [SerializeField] private ButtonManager resumeButton;
    [SerializeField] private TMP_Text noticeText;
    [SerializeField] private string lobbySceneName = "Lobby Scene";

    public static bool IsOpen { get; private set; }
    private readonly List<CinemachineInputAxisController> blockedCameraInputs = new();
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool leaving;

    private void Awake()
    {
        IsOpen = false;
        menuPanel.SetActive(false);
        settingsWindow.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (leaving || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

        if (!IsOpen) OpenMenu();
        else if (settingsWindow.gameObject.activeSelf) BackToMenu();
        else Resume();
    }

    public void OpenMenu()
    {
        if (IsOpen || leaving) return;

        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        IsOpen = true;
        MouseController.SetCursorLock(false);
        foreach (var input in FindObjectsByType<CinemachineInputAxisController>(FindObjectsSortMode.None))
        {
            if (!input.enabled) continue;
            blockedCameraInputs.Add(input);
            input.enabled = false;
        }

        GameLocalization.Set(noticeText, NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost
            ? "GameMenuHostNotice" : "GameMenuOnlineNotice");
        BackToMenu();
    }

    private void LateUpdate()
    {
        // 리스폰이나 관전 전환 중에도 열린 메뉴의 커서를 유지합니다.
        if (IsOpen) MouseController.SetCursorLock(false);
    }

    public void OpenSettings()
    {
        menuPanel.SetActive(false);
        settingsWindow.OpenWindow();
    }

    public void BackToMenu()
    {
        settingsWindow.isOn = false;
        settingsWindow.gameObject.SetActive(false);
        menuPanel.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
    }

    public void Resume()
    {
        if (leaving) return;
        settingsWindow.isOn = false;
        settingsWindow.gameObject.SetActive(false);
        menuPanel.SetActive(false);
        RestoreInput();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDisable()
    {
        if (IsOpen) RestoreInput();
    }

    private void RestoreInput()
    {
        IsOpen = false;
        foreach (var input in blockedCameraInputs)
            if (input != null) input.enabled = true;
        blockedCameraInputs.Clear();
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    public void LeaveGame()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(LeaveGameRoutine());
    }

    private IEnumerator LeaveGameRoutine()
    {
        // NGO의 종료가 끝난 뒤 로컬 씬을 바꿔 네트워크 오브젝트 정리와 충돌하지 않게 합니다.
        var manager = NetworkManager.Singleton;
        var session = FindFirstObjectByType<NetworkSessionManager>();
        if (session != null) session.Shutdown();
        else if (manager != null && manager.IsListening) manager.Shutdown();
        while (manager != null && manager.IsListening) yield return null;

        // 로비 씬의 NetworkManager와 중복되지 않도록 종료된 세션 오브젝트를 제거합니다.
        if (manager != null) Destroy(manager.gameObject);
        yield return null;

        RestoreInput();
        MouseController.SetCursorLock(false);
        SceneFader.LoadSceneWithFade(lobbySceneName);
    }
}
