using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// GameFeedback의 클립/VFX 배열을 프로젝트 에셋에서 자동으로 채우는 도구.
// Tools ▸ Feedback 클립 자동 연결 — 씬의 GameFeedback(없으면 생성)에 아래 매핑을 적용한다.
public static class GameFeedbackAutoWire
{
    private const string SfxRoot = "Assets/External Assets/BaykalArts/HyperCasual Voice and SFX pack";
    private const string VfxRoot = "Assets/External Assets/VFX_Klaus/Prefabs/Hyper Casual FX";

    [MenuItem("Tools/Feedback 클립 자동 연결")]
    public static void Wire()
    {
        GameFeedback feedback = Object.FindFirstObjectByType<GameFeedback>();
        if (feedback == null)
        {
            GameObject go = new GameObject("Game Feedback");
            feedback = go.AddComponent<GameFeedback>();
            Undo.RegisterCreatedObjectUndo(go, "Game Feedback 생성");
        }

        SerializedObject so = new SerializedObject(feedback);

        // ── SFX/보이스 ──
        SetClips(so, "hitSfx", LoadClips($"{SfxRoot}/SFX/Punches", 8));
        SetClips(so, "hitVoice", LoadClips($"{SfxRoot}/Voices/Emotions/Agony", 8));
        SetClips(so, "dashSfx", LoadClips($"{SfxRoot}/SFX/Swishes", 6));
        SetClips(so, "swingSfx", LoadClips($"{SfxRoot}/UI/Swipes", 6));
        SetClips(so, "eliminationVoice", LoadClips($"{SfxRoot}/Voices/Emotions/DeathPain", 8));
        SetClips(so, "uiPop", LoadClips($"{SfxRoot}/UI/Bubless", 4));
        SetClips(so, "countdownTick", LoadClips($"{SfxRoot}/UI/Clicks", 3));
        SetClips(so, "goVoice", LoadClips($"{SfxRoot}/Voices/Phrases/Fight", 5));
        SetClips(so, "suddenDeathVoice", LoadClips($"{SfxRoot}/Voices/Phrases/FinishHim", 5));
        SetClips(so, "winnerVoice", LoadClips($"{SfxRoot}/Voices/Phrases/Victory", 5));

        // ── VFX ──
        SetPrefabs(so, "hitVfx", new[]
        {
            $"{VfxRoot}/HCFX_Hit_01.prefab",
            $"{VfxRoot}/HCFX_Hit_02.prefab",
            $"{VfxRoot}/HCFX_Hit_03.prefab",
        });
        SetPrefabs(so, "dashVfx", new[] { $"{VfxRoot}/HCFX_Dust_Dash_01.prefab" });
        SetPrefabs(so, "winnerVfx", new[] { $"{VfxRoot}/HCFX_Conffeti.prefab" });
        SetPrefabs(so, "floorFallVfx", new[]
        {
            $"{VfxRoot}/HCFX_Dust_FloorHit_01.prefab",
            $"{VfxRoot}/HCFX_Dust_FloorHit_02.prefab",
        });

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feedback);
        EditorSceneManager.MarkSceneDirty(feedback.gameObject.scene);

        Debug.Log("[FeedbackAutoWire] 클립/VFX 연결 완료. 씬을 저장하세요.");
    }

    // 폴더에서 오디오 클립을 이름순으로 최대 max개 로드.
    private static List<AudioClip> LoadClips(string folder, int max)
    {
        var clips = new List<AudioClip>();

        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogWarning($"[FeedbackAutoWire] 폴더 없음: {folder}");
            return clips;
        }

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        System.Array.Sort(guids);

        foreach (string guid in guids)
        {
            if (clips.Count >= max) break;
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null) clips.Add(clip);
        }

        if (clips.Count == 0)
        {
            Debug.LogWarning($"[FeedbackAutoWire] 클립 없음: {folder}");
        }

        return clips;
    }

    private static void SetClips(SerializedObject so, string property, List<AudioClip> clips)
    {
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
        {
            Debug.LogWarning($"[FeedbackAutoWire] 필드 없음: {property}");
            return;
        }

        prop.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }

    private static void SetPrefabs(SerializedObject so, string property, string[] paths)
    {
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
        {
            Debug.LogWarning($"[FeedbackAutoWire] 필드 없음: {property}");
            return;
        }

        var prefabs = new List<GameObject>();
        foreach (string path in paths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) prefabs.Add(prefab);
            else Debug.LogWarning($"[FeedbackAutoWire] 프리팹 없음: {path}");
        }

        prop.arraySize = prefabs.Count;
        for (int i = 0; i < prefabs.Count; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
        }
    }
}
