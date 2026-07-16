using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 아레나 발판(FallingPlatform)을 중심에서의 거리별 동심원 링 색으로 칠하는 도구.
// 서든데스 붕괴가 바깥→안쪽 순서이므로, 색 밴드가 붕괴 순서(과녁)를 미리 알려주는 역할도 한다.
// 안전구역(FallingPlatform 없는 중앙 타일)은 건드리지 않는다.
// Tools ▸ 아레나 발판 링 색칠 — 현재 열린 씬에 적용한다.
public static class ArenaTilePainter
{
    // 안쪽(safe 인접) → 바깥(먼저 붕괴) 그라데이션. 4개 링.
    private static readonly string[] RingMaterialPaths =
    {
        "Assets/Materials/Arena/ArenaTileLayer0.mat", // 청록
        "Assets/Materials/Arena/ArenaTileLayer1.mat", // 골드
        "Assets/Materials/Arena/ArenaTileLayer2.mat", // 주황
        "Assets/Materials/Arena/ArenaTileLayer3.mat", // 레드(최외곽)
    };

    [MenuItem("Tools/아레나 발판 링 색칠")]
    public static void Paint()
    {
        Material[] ringMaterials = new Material[RingMaterialPaths.Length];
        for (int i = 0; i < RingMaterialPaths.Length; i++)
        {
            ringMaterials[i] = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPaths[i]);
            if (ringMaterials[i] == null)
            {
                Debug.LogError($"[ArenaTilePainter] 머티리얼 없음: {RingMaterialPaths[i]}");
                return;
            }
        }

        FallingPlatform[] platforms = Object.FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None);
        if (platforms.Length == 0)
        {
            Debug.LogWarning("[ArenaTilePainter] 씬에 FallingPlatform이 없습니다.");
            return;
        }

        // 중심: 씬의 SurvivalGameManager 위치(붕괴 스윕 기준과 동일). 없으면 발판들의 평균.
        Vector3 center = ResolveCenter(platforms);

        // 최대 수평 거리 산출(높이는 무시 — 링은 평면 기준).
        float maxDistance = 0.01f;
        for (int i = 0; i < platforms.Length; i++)
        {
            maxDistance = Mathf.Max(maxDistance, HorizontalDistance(platforms[i].transform.position, center));
        }

        int painted = 0;
        int rings = ringMaterials.Length;
        for (int i = 0; i < platforms.Length; i++)
        {
            MeshRenderer renderer = platforms[i].GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                continue;
            }

            float normalized = HorizontalDistance(platforms[i].transform.position, center) / maxDistance;
            int ring = Mathf.Clamp(Mathf.FloorToInt(normalized * rings), 0, rings - 1);

            Undo.RecordObject(renderer, "Paint Arena Tile");
            renderer.sharedMaterial = ringMaterials[ring];
            EditorUtility.SetDirty(renderer);
            painted++;
        }

        EditorSceneManager.MarkSceneDirty(platforms[0].gameObject.scene);
        Debug.Log($"[ArenaTilePainter] 발판 {painted}개를 {rings}개 링으로 색칠했습니다. (중심 {center}, 최대거리 {maxDistance:F1}) 씬을 저장하세요.");
    }

    private static Vector3 ResolveCenter(FallingPlatform[] platforms)
    {
        SurvivalGameManager manager = Object.FindFirstObjectByType<SurvivalGameManager>();
        if (manager != null)
        {
            return manager.transform.position;
        }

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < platforms.Length; i++)
        {
            sum += platforms[i].transform.position;
        }

        return sum / platforms.Length;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
