using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 개발용: 로비를 거치지 않고 Survival Arena에서 바로 플레이를 시작하는 메뉴.
// 씬 안의 DirectPlayBootstrap이 플레이 진입 시 로컬 호스트를 자동 시작한다.
public static class PlaySurvivalArenaTool
{
    private const string ArenaScenePath = "Assets/Scenes/Survival Arena.unity";

    [MenuItem("Tools/Survival Arena 바로 플레이 %#g")] // Ctrl/Cmd+Shift+G
    public static void PlayArena()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[DirectPlay] 이미 플레이 중입니다. 정지 후 다시 실행하세요.");
            return;
        }

        // 수정 중인 씬 저장 여부를 물어본 뒤 아레나를 열고 즉시 플레이.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return; // 사용자가 취소
        }

        EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Survival Arena 씬 열기")]
    public static void OpenArena()
    {
        if (EditorApplication.isPlaying)
        {
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);
    }
}
