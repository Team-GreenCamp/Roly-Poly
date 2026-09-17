using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// 서바이벌 아레나 맵 빌더 (Tools ▸ Survival Arena 빌더).
//
// 아레나의 바닥 타일 그리드/스폰 포인트/배럴을 파라미터로 재생성하는 개발 도구.
//  • 타일: 그리드 크기·간격·모양(사각/원형)·무너지는 외곽 링 수를 조절해 [타일 재생성].
//    바깥 링(밟으면 무너짐)은 붉은 톤, 안쪽(서든데스 전용)은 회색 체커보드로 색이 입혀져 위험이 읽힌다.
//  • 선택 유틸: 씬에서 타일을 골라 무너지는/안전 타일로 일괄 전환.
//  • 생성 후 반드시 씬 저장(인씬 NetworkObject GlobalObjectIdHash 확정) — [씬 저장] 버튼 제공.
public class SurvivalArenaBuilder : EditorWindow
{
    private enum ArenaShape { Square, Circle }

    // ── 타일 설정 ──
    private int gridSize = 7;
    private float tilePitch = 2.8f;
    private float tileSize = 2.7f;
    private float tileThickness = 0.5f;
    private ArenaShape shape = ArenaShape.Square;
    private int crumbleRings = 1;
    private float outerFallDelay = 0.8f;
    private float innerFallDelay = 0.5f;
    private float despawnAfterFall = 4f;
    private bool applyColors = true;

    // 층층 바닥(폴가이즈 Hex-A-Gone): 2 이상이면 모든 타일이 '밟으면 무너지는' 타일이 되고
    // 여러 층이 수직으로 쌓인다(위층에서 아래층으로 떨어지며 버티기). 1이면 단층.
    private int layerCount = 1;
    private float layerGap = 3.5f;

    // ── 스폰 설정 ──
    // 상하좌우 네 변으로 흩어 각 변의 중심에 배치한다. spawnRadius = 중심에서 각 변까지의 거리.
    private int spawnCount = 8;
    private bool autoFitSpawns = true;  // 맵(타일 설정)에서 스폰 거리를 자동 계산. 타일 재생성 시 스폰도 함께 재배치.
    private float spawnRadius = 6.3f;   // 안전(비붕괴) 구역의 바깥 링 중심선. 붕괴 링은 이보다 바깥.
    private float spawnHeight = 1.2f;
    private float spawnSideSpacing = 4.2f; // 같은 변에 둘 이상일 때 서로의 간격(겹침 방지). 타일 피치의 배수 권장.

    // 맵 바깥 가장자리(가장 바깥에서 한 칸 안쪽)에 스폰해 최대한 흩어지게 한다.
    // 가장자리 타일이 '밟으면 무너지는' 타일이어도 스폰 지점 아래는 자동으로 안전 타일로 바꾼다(CarveSafeSpawnPad).
    private float AutoSpawnRadius
    {
        get
        {
            int edgeRing = Mathf.Max(1, gridSize / 2 - 1);
            return edgeRing * tilePitch;
        }
    }

    // ── 배럴 설정 ──
    private int barrelCount = 6;
    private float barrelRadius = 5f;
    private float barrelScale = 0.65f;

    private const string BarrelPrefabPath = "Assets/Prefab/SM_Prop_Barrel_01.prefab";
    private const string TileMaterialFolder = "Assets/Materials/Arena";

    private Vector2 scroll;

    [MenuItem("Tools/Survival Arena 빌더")]
    public static void Open()
    {
        GetWindow<SurvivalArenaBuilder>("Arena Builder");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        // 아레나 씬(SGM이 있는 씬)이 아니면 경고 — Survival Arena/Sumo Arena/Falling Floors 모두 허용.
        if (FindFirstObjectByType<SurvivalGameManager>() == null)
        {
            EditorGUILayout.HelpBox("현재 씬에 SurvivalGameManager가 없습니다. 아레나 씬(서바이벌/스모/떨어지는 바닥)에서 사용하세요.", MessageType.Warning);
        }

        // ───────── 바닥 타일 ─────────
        GUILayout.Label("바닥 타일", EditorStyles.boldLabel);
        if (GUILayout.Button(new GUIContent("↺ 현재 맵에서 설정 읽어오기", "현재 씬의 타일에서 그리드·간격·층 수·모양을 읽어 아래 값에 채운다(재생성 시 기존 구성 유지)")))
        {
            ReadSettingsFromCurrentMap();
        }
        gridSize = EditorGUILayout.IntSlider(new GUIContent("그리드 크기 (N×N)"), gridSize, 5, 21);
        if (gridSize % 2 == 0) gridSize += 1; // 중심 타일이 있도록 홀수 유지
        tilePitch = EditorGUILayout.Slider("타일 간격(중심 거리)", tilePitch, 1f, 6f);
        tileSize = EditorGUILayout.Slider("타일 한 변 크기", tileSize, 0.5f, tilePitch);
        tileThickness = EditorGUILayout.Slider("타일 두께", tileThickness, 0.2f, 2f);
        shape = (ArenaShape)EditorGUILayout.EnumPopup("아레나 모양", shape);
        crumbleRings = EditorGUILayout.IntSlider(new GUIContent("무너지는 외곽 링 수", "바깥에서부터 몇 겹을 '밟으면 무너지는' 타일로 만들지"), crumbleRings, 0, gridSize / 2);
        outerFallDelay = EditorGUILayout.Slider("외곽 낙하 지연(초)", outerFallDelay, 0.1f, 3f);
        innerFallDelay = EditorGUILayout.Slider("서든데스 낙하 지연(초)", innerFallDelay, 0.1f, 3f);
        applyColors = EditorGUILayout.Toggle(new GUIContent("색상 적용", "외곽=붉은 톤, 안쪽=회색 체커보드"), applyColors);
        layerCount = EditorGUILayout.IntSlider(new GUIContent("층 수 (폴가이즈)", "2 이상이면 모든 타일이 밟으면 무너지고 여러 층이 수직으로 쌓임"), layerCount, 1, 5);
        if (layerCount > 1)
        {
            layerGap = EditorGUILayout.Slider("층 간격(수직 거리)", layerGap, 2f, 6f);
        }

        float width = (gridSize - 1) * tilePitch + tileSize;
        EditorGUILayout.HelpBox($"맵 크기 약 {width:0.#} × {width:0.#} m", MessageType.None);

        if (GUILayout.Button("타일 재생성 (기존 타일 삭제 후 생성)", GUILayout.Height(28)))
        {
            RebuildTiles();
        }

        EditorGUILayout.Space(6);
        GUILayout.Label("선택한 타일 일괄 변경 (씬에서 타일 선택 후)", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("→ 무너지는 타일로")) SetSelectedTiles(true);
            if (GUILayout.Button("→ 안전(서든데스) 타일로")) SetSelectedTiles(false);
        }

        EditorGUILayout.Space(10);

        // ───────── 스폰 포인트 ─────────
        GUILayout.Label("스폰 포인트", EditorStyles.boldLabel);
        spawnCount = EditorGUILayout.IntSlider("스폰 수", spawnCount, 2, 12);
        autoFitSpawns = EditorGUILayout.Toggle(new GUIContent("맵 크기에 맞춤", "타일 설정(그리드/간격/붕괴 링)에서 스폰 거리를 자동 계산하고, 타일 재생성 시 스폰도 함께 재배치"), autoFitSpawns);

        if (autoFitSpawns)
        {
            spawnRadius = AutoSpawnRadius;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Slider(new GUIContent("변까지 거리 (자동)"), spawnRadius, 1f, width * 0.5f);
            }
        }
        else
        {
            spawnRadius = EditorGUILayout.Slider(new GUIContent("변까지 거리", "중심에서 상하좌우 각 변까지의 거리"), spawnRadius, 1f, width * 0.5f);
        }

        spawnHeight = EditorGUILayout.Slider("스폰 높이(y)", spawnHeight, 0.5f, 5f);
        spawnSideSpacing = EditorGUILayout.Slider(new GUIContent("같은 변 간격", "한 변에 2명 이상일 때 서로 벌어지는 간격"), spawnSideSpacing, 1f, 8f);
        if (GUILayout.Button("스폰 포인트 재배치", GUILayout.Height(24)))
        {
            RebuildSpawns();
        }

        EditorGUILayout.Space(10);

        // ───────── 배럴 ─────────
        GUILayout.Label("배럴 (던질 물체)", EditorStyles.boldLabel);
        barrelCount = EditorGUILayout.IntSlider("배럴 수", barrelCount, 0, 20);
        barrelRadius = EditorGUILayout.Slider("배치 반경", barrelRadius, 1f, width * 0.5f);
        barrelScale = EditorGUILayout.Slider("배럴 스케일", barrelScale, 0.3f, 1.5f);
        if (GUILayout.Button("배럴 재배치", GUILayout.Height(24)))
        {
            RebuildBarrels();
        }

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox("생성/변경 후 반드시 씬을 저장하세요. 인씬 NetworkObject는 저장 시점에 ID가 확정되며, 저장하지 않으면 클라이언트 씬 전환이 깨질 수 있습니다.", MessageType.Warning);
        if (GUILayout.Button("씬 저장", GUILayout.Height(28)))
        {
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[ArenaBuilder] 씬 저장 완료.");
        }

        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────────────────────────────────────────
    // 타일
    // ─────────────────────────────────────────────────────────────
    private void RebuildTiles()
    {
        BuildArenaTiles(gridSize, tilePitch, tileSize, tileThickness, shape, crumbleRings,
            layerCount, layerGap, outerFallDelay, innerFallDelay, despawnAfterFall, applyColors);

        // 맵 크기가 바뀌었으니 스폰도 새 위치(가장자리 + 안전 패드)에 맞춰 재배치.
        if (autoFitSpawns)
        {
            spawnRadius = AutoSpawnRadius;
            RebuildSpawns();
        }

        // 탈락존/killY를 새 최하단에 맞춘다(층 수·간격을 바꿔도 자동으로 따라감).
        FitHazardsToCurrentMap();
    }

    // 현재 씬의 타일에서 그리드/간격/층 수/모양을 측정해 창의 값에 채운다.
    // (Falling Floors 등 기존 맵을 그리드만 바꿔 재생성할 때 층 구성이 뭉개지지 않도록.)
    private void ReadSettingsFromCurrentMap()
    {
        FallingPlatform[] tiles = FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None);
        if (tiles.Length == 0)
        {
            Debug.LogWarning("[ArenaBuilder] 타일이 없어 읽어올 수 없습니다.");
            return;
        }

        // 층: 서로 다른 y값(반올림) 그룹.
        var layerYs = new SortedSet<float>();
        foreach (FallingPlatform t in tiles)
        {
            layerYs.Add(Mathf.Round(t.transform.position.y * 100f) / 100f);
        }
        var ysDesc = new List<float>(layerYs);
        ysDesc.Sort((a, b) => b.CompareTo(a));
        layerCount = ysDesc.Count;
        if (layerCount > 1)
        {
            float sum = 0f;
            for (int i = 1; i < ysDesc.Count; i++) sum += ysDesc[i - 1] - ysDesc[i];
            layerGap = sum / (ysDesc.Count - 1);
        }

        // 최상층 타일만으로 그리드/간격/모양 측정.
        float topY = ysDesc[0];
        var topTiles = new List<Transform>();
        var xset = new SortedSet<float>();
        float maxAbs = 0f;
        foreach (FallingPlatform t in tiles)
        {
            if (Mathf.Abs(t.transform.position.y - topY) > 0.05f) continue;
            topTiles.Add(t.transform);
            float x = Mathf.Round(t.transform.position.x * 100f) / 100f;
            xset.Add(x);
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(t.transform.position.x), Mathf.Abs(t.transform.position.z));
        }

        // 간격 = 인접 x좌표 최소 차.
        float prev = float.NaN, pitch = 0f;
        foreach (float x in xset)
        {
            if (!float.IsNaN(prev))
            {
                float d = x - prev;
                if (d > 0.05f && (pitch <= 0f || d < pitch)) pitch = d;
            }
            prev = x;
        }
        if (pitch > 0f) tilePitch = pitch;

        // 타일 크기/두께.
        if (topTiles.Count > 0)
        {
            tileSize = topTiles[0].localScale.x;
            tileThickness = topTiles[0].localScale.y;
        }

        // 그리드 크기 = 전체 폭/간격 + 1.
        if (pitch > 0f)
        {
            gridSize = Mathf.RoundToInt((maxAbs * 2f) / pitch) + 1;
            if (gridSize % 2 == 0) gridSize += 1;
        }

        // 모양: 코너(±maxAbs, ±maxAbs)에 타일이 있으면 사각, 없으면 원형.
        bool cornerFound = false;
        foreach (Transform t in topTiles)
        {
            if (Mathf.Abs(Mathf.Abs(t.position.x) - maxAbs) < pitch * 0.5f &&
                Mathf.Abs(Mathf.Abs(t.position.z) - maxAbs) < pitch * 0.5f)
            {
                cornerFound = true;
                break;
            }
        }
        shape = cornerFound ? ArenaShape.Square : ArenaShape.Circle;

        // 무너지는 링 수(단층만 의미): 최상층에서 밟으면 무너지는 가장 안쪽 링.
        if (layerCount == 1)
        {
            int center = gridSize / 2;
            int innerMostCrumble = int.MaxValue;
            foreach (Transform t in topTiles)
            {
                FallingPlatform fp = t.GetComponent<FallingPlatform>();
                if (fp == null || !fp.triggerByStepping) continue;
                int ring = Mathf.RoundToInt(Mathf.Max(Mathf.Abs(t.position.x), Mathf.Abs(t.position.z)) / pitch);
                innerMostCrumble = Mathf.Min(innerMostCrumble, ring);
            }
            crumbleRings = innerMostCrumble == int.MaxValue ? 0 : Mathf.Clamp(center - innerMostCrumble + 1, 0, center);
        }
        else
        {
            crumbleRings = 0;
        }

        Repaint();
        Debug.Log($"[ArenaBuilder] 현재 맵 읽음 → 그리드 {gridSize}, 간격 {tilePitch:0.##}, 층 {layerCount}, 간격 {layerGap:0.#}, {shape}.");
    }

    // 타일 생성 코어(정적 — 프리셋 메뉴에서도 호출). layerCount>1이면 모든 타일이 무너지고 여러 층이 쌓인다.
    private static void BuildArenaTiles(int gridSize, float tilePitch, float tileSize, float tileThickness,
        ArenaShape shape, int crumbleRings, int layerCount, float layerGap,
        float outerFallDelay, float innerFallDelay, float despawnAfterFall, bool applyColors)
    {
        Transform group = GetOrCreateGroup("Arena Tiles");
        DeleteExistingTiles(group);

        Material outerMat = applyColors ? GetTileMaterial("ArenaTileCrumble", new Color(0.85f, 0.45f, 0.38f)) : null;
        Material innerMatA = applyColors ? GetTileMaterial("ArenaTileA", new Color(0.78f, 0.78f, 0.8f)) : null;
        Material innerMatB = applyColors ? GetTileMaterial("ArenaTileB", new Color(0.62f, 0.64f, 0.68f)) : null;
        // 층별 색(위→아래 구분).
        Color[] layerColors =
        {
            new Color(0.55f, 0.75f, 0.9f), new Color(0.9f, 0.8f, 0.5f),
            new Color(0.8f, 0.55f, 0.85f), new Color(0.6f, 0.85f, 0.6f), new Color(0.85f, 0.6f, 0.55f),
        };

        int center = gridSize / 2;
        float circleRadius = center * tilePitch + tileSize * 0.5f;
        bool layered = layerCount > 1;
        int created = 0;

        for (int layer = 0; layer < Mathf.Max(1, layerCount); layer++)
        {
            float layerY = -layer * layerGap;
            Transform layerParent = group;
            if (layered)
            {
                GameObject layerGo = new GameObject($"Layer {layer}");
                layerGo.transform.SetParent(group, false);
                Undo.RegisterCreatedObjectUndo(layerGo, "Layer 생성");
                layerParent = layerGo.transform;
            }

            for (int iz = 0; iz < gridSize; iz++)
            {
                for (int ix = 0; ix < gridSize; ix++)
                {
                    float x = (ix - center) * tilePitch;
                    float z = (iz - center) * tilePitch;

                    if (shape == ArenaShape.Circle && Mathf.Sqrt(x * x + z * z) > circleRadius - tileSize * 0.5f)
                    {
                        continue;
                    }

                    // 층층 모드: 모든 타일이 무너짐. 단층: 바깥 링만 무너짐(체비쇼프 거리).
                    int ring = Mathf.Max(Mathf.Abs(ix - center), Mathf.Abs(iz - center));
                    bool crumble = layered || (crumbleRings > 0 && ring > center - crumbleRings);

                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile {created}";
                    tile.transform.SetParent(layerParent, false);
                    tile.transform.position = new Vector3(x, layerY + tileThickness * 0.5f, z);
                    tile.transform.localScale = new Vector3(tileSize, tileThickness, tileSize);

                    tile.AddComponent<NetworkObject>();
                    tile.AddComponent<NetworkTransform>();

                    FallingPlatform platform = tile.AddComponent<FallingPlatform>();
                    platform.triggerByStepping = crumble;
                    platform.fallDelay = crumble ? outerFallDelay : innerFallDelay;
                    platform.respawnDelay = 0f;
                    platform.despawnAfterFallSeconds = despawnAfterFall;

                    if (applyColors)
                    {
                        Material mat;
                        if (layered)
                        {
                            mat = GetTileMaterial($"ArenaTileLayer{layer}", layerColors[layer % layerColors.Length]);
                        }
                        else
                        {
                            mat = crumble ? outerMat : ((ix + iz) % 2 == 0 ? innerMatA : innerMatB);
                        }
                        tile.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    }

                    Undo.RegisterCreatedObjectUndo(tile, "Arena Tiles 생성");
                    created++;
                }
            }
        }

        MarkDirty();
        Debug.Log($"[ArenaBuilder] 타일 {created}개 생성 (층 {Mathf.Max(1, layerCount)}, 무너지는 링 {crumbleRings}). 씬을 저장하세요!");
    }

    // ─────────────────────────────────────────────────────────────
    // 모드 프리셋 (execute_menu_item로도 호출 가능 — 활성 씬에 즉시 빌드)
    // ─────────────────────────────────────────────────────────────
    [MenuItem("Tools/아레나 프리셋/스모 링아웃 (원형 단단)")]
    public static void PresetSumoRingout()
    {
        // 원형 단단 발판(밟아도 안 무너짐). 서든데스가 바깥 링부터 좁혀 링아웃을 강제.
        BuildArenaTiles(13, 2.8f, 2.7f, 0.5f, ArenaShape.Circle, 0,
            1, 3.5f, 0.8f, 0.5f, 4f, true);
        FitSpawnsToCurrentMap();
        FitHazardsToCurrentMap();
        Debug.Log("[ArenaBuilder] 스모 링아웃 프리셋 생성. SGM: 넉백 배율↑, 서든데스 ON 권장. 씬 저장 필요.");
    }

    [MenuItem("Tools/아레나 프리셋/떨어지는 바닥 (층층)")]
    public static void PresetFallingFloors()
    {
        // 모든 타일이 밟으면 무너지는 4층 구조. 위층에서 떨어지며 버티기.
        BuildArenaTiles(9, 2.5f, 2.4f, 0.5f, ArenaShape.Square, 0,
            4, 5f, 0.7f, 0.7f, 3f, true);
        FitSpawnsToCurrentMap();
        FitHazardsToCurrentMap();
        Debug.Log("[ArenaBuilder] 떨어지는 바닥 프리셋 생성. SGM: 서든데스 OFF(0) 권장. 탈락존/killY는 자동 배치됨. 씬 저장 필요.");
    }

    // ─────────────────────────────────────────────────────────────
    // 시상대 (승자 발표) — 공중에 배치, 종료 시 상위 3인이 올라간다.
    // ─────────────────────────────────────────────────────────────
    [MenuItem("Tools/시상대 생성 (현재 씬)")]
    public static void BuildPodium()
    {
        // 기존 시상대 제거.
        GameObject existing = GameObject.Find("Podium");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        // 아레나 위 공중(카메라가 승자를 따라오므로 위치는 자유). 낙사 판정보다 훨씬 위.
        GameObject root = new GameObject("Podium");
        root.transform.position = new Vector3(0f, 25f, 0f);
        Undo.RegisterCreatedObjectUndo(root, "시상대 생성");

        // 바닥 슬랩.
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "Podium Base";
        slab.transform.SetParent(root.transform, false);
        slab.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        slab.transform.localScale = new Vector3(7.5f, 0.4f, 4f);
        Object.DestroyImmediate(slab.GetComponent<BoxCollider>()); // 시상대는 물리 불필요
        slab.GetComponent<MeshRenderer>().sharedMaterial = GetTileMaterial("PodiumBase", new Color(0.2f, 0.22f, 0.28f));

        // 블록 3개(1등 가운데 가장 높음) + 스탠드 지점.
        // (로컬x, 블록높이, 색)
        var blocks = new (float x, float h, Color c, string label)[]
        {
            (0f, 1.5f, new Color(1f, 0.84f, 0.2f), "1"),     // 금
            (-2.3f, 1.0f, new Color(0.78f, 0.8f, 0.85f), "2"), // 은
            (2.3f, 0.7f, new Color(0.8f, 0.5f, 0.2f), "3"),  // 동
        };

        Transform[] stands = new Transform[3];
        for (int i = 0; i < blocks.Length; i++)
        {
            var b = blocks[i];

            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = $"Podium Block {b.label}";
            block.transform.SetParent(root.transform, false);
            block.transform.localPosition = new Vector3(b.x, b.h * 0.5f, 0f);
            block.transform.localScale = new Vector3(1.8f, b.h, 1.8f);
            Object.DestroyImmediate(block.GetComponent<BoxCollider>());
            block.GetComponent<MeshRenderer>().sharedMaterial = GetTileMaterial($"Podium{b.label}", b.c);

            GameObject stand = new GameObject($"Stand {b.label}");
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(b.x, b.h + 0.1f, 0f);
            stand.transform.localRotation = Quaternion.LookRotation(Vector3.back); // -Z를 바라봄
            stands[i] = stand.transform;
        }

        // SGM에 배선.
        SurvivalGameManager sgm = FindFirstObjectByType<SurvivalGameManager>();
        if (sgm != null)
        {
            SerializedObject so = new SerializedObject(sgm);
            SerializedProperty rootProp = so.FindProperty("podiumRoot");
            if (rootProp != null) rootProp.objectReferenceValue = root;

            SerializedProperty standsProp = so.FindProperty("podiumStands");
            if (standsProp != null)
            {
                standsProp.arraySize = 3;
                for (int i = 0; i < 3; i++)
                {
                    standsProp.GetArrayElementAtIndex(i).objectReferenceValue = stands[i];
                }
            }
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[ArenaBuilder] SGM이 없어 시상대를 배선하지 못했습니다. 아레나 씬에서 실행하세요.");
        }

        // 평소엔 숨김(종료 시 SGM이 켠다). 스탠드 위치는 비활성 상태에서도 유효.
        root.SetActive(false);

        MarkDirty();
        Debug.Log("[ArenaBuilder] 시상대 생성 + SGM 배선 완료(비활성). 씬 저장 필요.");
    }

    // 시상대 카메라(없으면 생성) + 화면 페이드 오버레이(없으면 생성)를 SGM에 배선한다.
    [MenuItem("Tools/시상대 카메라 + 페이드 배선 (현재 씬)")]
    public static void WirePodiumCameraAndFade()
    {
        SurvivalGameManager sgm = FindFirstObjectByType<SurvivalGameManager>();
        if (sgm == null)
        {
            Debug.LogWarning("[ArenaBuilder] SGM이 없습니다. 아레나 씬에서 실행하세요.");
            return;
        }

        // 시상대 위치 파악(카메라 프레이밍용).
        GameObject podium = GameObject.Find("Podium");
        Vector3 podiumPos = podium != null ? podium.transform.position : new Vector3(0f, 25f, 0f);

        // ── 시상대 카메라(있으면 재사용=위치 유지, 없으면 생성+프레이밍). 평소 비활성. ──
        GameObject camGo = FindInactiveOrActiveByName("Podium Camera");
        bool createdCamera = false;
        if (camGo == null)
        {
            camGo = new GameObject("Podium Camera");
            camGo.AddComponent<CinemachineCamera>();
            Undo.RegisterCreatedObjectUndo(camGo, "Podium Camera 생성");
            createdCamera = true;
        }
        if (createdCamera)
        {
            // 새로 만들 때만 정면(-Z)에서 살짝 내려다보게 배치(기존 카메라는 사용자 배치 존중).
            Vector3 camPos = podiumPos + new Vector3(0f, 2.2f, -7.5f);
            camGo.transform.position = camPos;
            camGo.transform.rotation = Quaternion.LookRotation((podiumPos + new Vector3(0f, 0.9f, 0f)) - camPos);
        }
        CinemachineCamera cc = camGo.GetComponent<CinemachineCamera>();
        if (cc != null)
        {
            var pr = cc.Priority;
            pr.Enabled = true;
            pr.Value = 100; // FreeLook보다 높게(전환 시 활성화되면 이 카메라 선택)
            cc.Priority = pr;
            EditorUtility.SetDirty(cc);
        }
        camGo.SetActive(false); // 종료 시 SGM이 켠다

        // ── 페이드 오버레이(HUD Canvas 위 전체 검은 이미지 + CanvasGroup). ──
        CanvasGroup fade = null;
        Canvas hud = FindHudCanvas();
        if (hud != null)
        {
            Transform existing = hud.transform.Find("Fade Overlay");
            if (existing != null)
            {
                fade = existing.GetComponent<CanvasGroup>();
            }

            if (fade == null)
            {
                GameObject fadeGo = new GameObject("Fade Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
                fadeGo.transform.SetParent(hud.transform, false);
                RectTransform rt = fadeGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                Image img = fadeGo.GetComponent<Image>();
                img.color = Color.black;
                img.raycastTarget = false;
                fade = fadeGo.GetComponent<CanvasGroup>();
                fade.alpha = 0f;
                fade.blocksRaycasts = false;
                fade.interactable = false;
                fadeGo.transform.SetAsLastSibling(); // 항상 맨 위에 그려지도록
                Undo.RegisterCreatedObjectUndo(fadeGo, "Fade Overlay 생성");
            }
        }
        else
        {
            Debug.LogWarning("[ArenaBuilder] HUD Canvas를 찾지 못해 페이드 오버레이를 만들지 못했습니다.");
        }

        // ── SGM 배선 ──
        GameObject freeLook = FindInactiveOrActiveByName("FreeLook Camera");
        SerializedObject so = new SerializedObject(sgm);
        SetRef(so, "podiumCamera", camGo);
        SetRef(so, "gameplayCamera", freeLook);
        SetRef(so, "fadeOverlay", fade);
        so.ApplyModifiedProperties();

        MarkDirty();
        Debug.Log($"[ArenaBuilder] 시상대 카메라 + 페이드 배선 완료(카메라:{(camGo != null)}, 페이드:{(fade != null)}, FreeLook:{(freeLook != null)}). 씬 저장 필요.");
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null) p.objectReferenceValue = value;
    }

    private static GameObject FindInactiveOrActiveByName(string name)
    {
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.name == name && go.scene.IsValid() && !EditorUtility.IsPersistent(go))
            {
                return go;
            }
        }
        return null;
    }

    private static Canvas FindHudCanvas()
    {
        SurvivalHudController hud = FindFirstObjectByType<SurvivalHudController>();
        if (hud != null)
        {
            Canvas c = hud.GetComponentInParent<Canvas>();
            if (c != null) return c;
        }
        return FindFirstObjectByType<Canvas>();
    }

    private static void DeleteExistingTiles(Transform group)
    {
        // 그룹 아래 자식(타일 + Layer 하위 그룹) 전부 + 루트에 남은 옛 타일("Tile N")을 제거.
        var doomed = new List<GameObject>();

        for (int i = group.childCount - 1; i >= 0; i--)
        {
            doomed.Add(group.GetChild(i).gameObject);
        }

        Regex tileName = new Regex(@"^Tile \d+$");
        foreach (FallingPlatform platform in FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None))
        {
            if (platform.transform.parent == null && tileName.IsMatch(platform.gameObject.name))
            {
                doomed.Add(platform.gameObject);
            }
        }

        foreach (GameObject go in doomed)
        {
            Undo.DestroyObjectImmediate(go);
        }
    }

    private void SetSelectedTiles(bool crumble)
    {
        int changed = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            FallingPlatform platform = go.GetComponent<FallingPlatform>();
            if (platform == null)
            {
                continue;
            }

            Undo.RecordObject(platform, "타일 유형 변경");
            platform.triggerByStepping = crumble;
            platform.fallDelay = crumble ? outerFallDelay : innerFallDelay;
            EditorUtility.SetDirty(platform);

            if (applyColors)
            {
                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, "타일 색 변경");
                    renderer.sharedMaterial = crumble
                        ? GetTileMaterial("ArenaTileCrumble", new Color(0.85f, 0.45f, 0.38f))
                        : GetTileMaterial("ArenaTileA", new Color(0.78f, 0.78f, 0.8f));
                }
            }

            changed++;
        }

        MarkDirty();
        Debug.Log($"[ArenaBuilder] 선택 타일 {changed}개 → {(crumble ? "무너지는" : "안전")} 타일로 변경.");
    }

    // ─────────────────────────────────────────────────────────────
    // 스폰 포인트
    // ─────────────────────────────────────────────────────────────
    private void RebuildSpawns()
    {
        BuildSpawnPoints(spawnCount, spawnRadius, spawnSideSpacing, spawnHeight, applyColors);
    }

    // 창을 열지 않고도 현재 씬의 맵 크기를 측정해 스폰을 가장자리에 다시 뿌린다(execute_menu_item로도 호출 가능).
    [MenuItem("Tools/스폰을 현재 맵 크기에 맞춰 재배치")]
    public static void FitSpawnsToCurrentMap()
    {
        FallingPlatform[] tiles = FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None);
        if (tiles.Length == 0)
        {
            Debug.LogWarning("[ArenaBuilder] 타일(FallingPlatform)이 없어 맵 크기를 잴 수 없습니다.");
            return;
        }

        // 맵 반경(가장 바깥 타일)과 타일 간격을 실제 씬에서 측정.
        float maxAbs = 0f;
        var coords = new SortedSet<float>();
        foreach (FallingPlatform t in tiles)
        {
            Vector3 p = t.transform.position;
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(p.x), Mathf.Abs(p.z));
            coords.Add(Mathf.Round(p.x * 100f) / 100f);
        }

        float pitch = 0f, prev = float.NaN;
        foreach (float c in coords)
        {
            if (!float.IsNaN(prev))
            {
                float d = c - prev;
                if (d > 0.05f && (pitch <= 0f || d < pitch)) pitch = d;
            }
            prev = c;
        }
        if (pitch <= 0f) pitch = 2.8f;

        float radius = Mathf.Max(pitch, maxAbs - pitch); // 가장 바깥에서 한 칸 안쪽

        // 스폰 수는 기존 "Spawn Point N" 개수를 유지(없으면 4).
        int existing = 0;
        Regex spawnName = new Regex(@"^Spawn Point \d+$");
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (spawnName.IsMatch(go.name)) existing++;
        }
        int count = existing >= 2 ? existing : 4;

        BuildSpawnPoints(count, radius, Mathf.Max(pitch, 4.2f), 1.2f, true);
        Debug.Log($"[ArenaBuilder] 현재 맵(반경 {maxAbs:0.#}, 간격 {pitch:0.##})에 맞춰 스폰 {count}개를 가장자리(거리 {radius:0.#})에 재배치.");
    }

    // 탈락존(Elimination Zone)과 SGM.killY를 현재 맵의 최하단 타일 아래로 자동 배치한다.
    // 층 수/간격을 어떻게 바꿔도 위험지대가 항상 최하층 밑에 오도록 맞춰준다.
    [MenuItem("Tools/탈락존/killY를 현재 맵에 맞춤")]
    public static void FitHazardsToCurrentMap()
    {
        FallingPlatform[] tiles = FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None);
        if (tiles.Length == 0)
        {
            Debug.LogWarning("[ArenaBuilder] 타일이 없어 탈락존을 맞출 수 없습니다.");
            return;
        }

        // 가장 낮은 타일 바닥 y.
        float minTileY = float.MaxValue;
        foreach (FallingPlatform t in tiles)
        {
            float bottom = t.transform.position.y - t.transform.localScale.y * 0.5f;
            if (bottom < minTileY) minTileY = bottom;
        }

        // 최하단에서 4m 아래에 탈락존, 그보다 5m 더 아래에 killY(서버 안전망).
        float zoneY = minTileY - 4f;
        float killY = zoneY - 5f;

        // Elimination Zone 배치(없으면 만든다) + 아레나를 덮는 넓은 트리거.
        EliminationZone zone = FindFirstObjectByType<EliminationZone>();
        if (zone == null)
        {
            GameObject go = new GameObject("Elimination Zone");
            zone = go.AddComponent<EliminationZone>();
            Undo.RegisterCreatedObjectUndo(go, "Elimination Zone 생성");
        }

        Undo.RecordObject(zone.transform, "탈락존 위치");
        zone.transform.position = new Vector3(0f, zoneY, 0f);

        BoxCollider box = zone.GetComponent<BoxCollider>();
        if (box != null)
        {
            Undo.RecordObject(box, "탈락존 콜라이더");
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(80f, 3f, 80f);
            EditorUtility.SetDirty(box);
        }
        EditorUtility.SetDirty(zone);

        // SGM.killY 설정(직렬화 private 필드).
        SurvivalGameManager sgm = FindFirstObjectByType<SurvivalGameManager>();
        if (sgm != null)
        {
            SerializedObject so = new SerializedObject(sgm);
            SerializedProperty prop = so.FindProperty("killY");
            if (prop != null)
            {
                prop.floatValue = killY;
                so.ApplyModifiedProperties();
            }
        }

        MarkDirty();
        Debug.Log($"[ArenaBuilder] 탈락존 y={zoneY:0.#}, killY={killY:0.#} (최하단 {minTileY:0.#} 아래)로 맞춤.");
    }

    // 4변 균등 배치 + 스폰 지점 아래 타일을 안전 타일로 만드는 공통 로직.
    private static void BuildSpawnPoints(int count, float radius, float sideSpacing, float height, bool applyColors)
    {
        GameObject spawnRoot = GameObject.Find("Spawn Points");
        if (spawnRoot == null)
        {
            spawnRoot = new GameObject("Spawn Points");
            spawnRoot.AddComponent<LobbySpawnPointGroup>();
            Undo.RegisterCreatedObjectUndo(spawnRoot, "Spawn Points 생성");
        }
        else if (spawnRoot.GetComponent<LobbySpawnPointGroup>() == null)
        {
            Undo.AddComponent<LobbySpawnPointGroup>(spawnRoot);
        }

        for (int i = spawnRoot.transform.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(spawnRoot.transform.GetChild(i).gameObject);
        }

        // 상하좌우 4변에 흩어서 배치한다. 인덱스를 변 순환(상→하→좌→우→상2…)으로 배정하므로
        // clientId % count 스폰 규칙에서 2~4인은 전원 서로 다른 변에서 시작한다.
        Vector3[] sideDirs =
        {
            new Vector3(0f, 0f, 1f),   // 상(+Z)
            new Vector3(0f, 0f, -1f),  // 하(-Z)
            new Vector3(-1f, 0f, 0f),  // 좌(-X)
            new Vector3(1f, 0f, 0f),   // 우(+X)
        };

        int perSide = Mathf.CeilToInt(count / 4f);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = sideDirs[i % 4];
            Vector3 tangent = new Vector3(dir.z, 0f, dir.x); // 변 방향(가장자리 따라 좌우)

            int slot = i / 4;
            float offset = (slot - (perSide - 1) * 0.5f) * sideSpacing;
            Vector3 spawnPos = dir * radius + tangent * offset + Vector3.up * height;

            // 이름 기반 폴백("Spawn Point N")을 쓰므로 이름 규칙을 지킨다.
            GameObject point = new GameObject($"Spawn Point {i}");
            point.transform.SetParent(spawnRoot.transform, false);
            point.transform.position = spawnPos;
            point.transform.rotation = Quaternion.LookRotation(-dir); // 중앙을 바라봄
            Undo.RegisterCreatedObjectUndo(point, "Spawn Point 생성");

            // 가장자리 타일이 '밟으면 무너지는' 타일이면 즉사하므로 스폰 아래 타일을 안전 타일로.
            CarveSafeSpawnPad(spawnPos, applyColors);
        }

        MarkDirty();
    }

    // 주어진 위치에서 가장 가까운 타일을 밟아도 안 무너지는 안전 타일로 만든다.
    private static void CarveSafeSpawnPad(Vector3 worldPos, bool applyColors)
    {
        FallingPlatform nearest = null;
        float best = float.MaxValue;
        foreach (FallingPlatform p in FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None))
        {
            // 3D 거리 — 층층 맵에서 스폰(최상층 위)과 가장 가까운 '최상층' 타일을 고르기 위함.
            float sq = (p.transform.position - worldPos).sqrMagnitude;
            if (sq < best)
            {
                best = sq;
                nearest = p;
            }
        }

        if (nearest == null || !nearest.triggerByStepping)
        {
            return;
        }

        Undo.RecordObject(nearest, "스폰 안전 타일");
        nearest.triggerByStepping = false;
        EditorUtility.SetDirty(nearest);

        if (applyColors)
        {
            MeshRenderer renderer = nearest.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Undo.RecordObject(renderer, "스폰 타일 색");
                renderer.sharedMaterial = GetTileMaterial("ArenaTileSpawn", new Color(0.35f, 0.7f, 0.45f));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 배럴
    // ─────────────────────────────────────────────────────────────
    private void RebuildBarrels()
    {
        // 기존 배럴("Barrel N") 제거.
        Regex barrelName = new Regex(@"^Barrel \d+$");
        foreach (GrabbableObject grabbable in FindObjectsByType<GrabbableObject>(FindObjectsSortMode.None))
        {
            if (barrelName.IsMatch(grabbable.gameObject.name))
            {
                Undo.DestroyObjectImmediate(grabbable.gameObject);
            }
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrelPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ArenaBuilder] 배럴 프리팹을 찾지 못했습니다: {BarrelPrefabPath}");
            return;
        }

        for (int i = 0; i < barrelCount; i++)
        {
            // 링 위에 균등 + 약간의 랜덤 오프셋.
            float angle = i * (360f / Mathf.Max(1, barrelCount)) * Mathf.Deg2Rad;
            float radius = barrelRadius * Random.Range(0.55f, 1f);
            Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 1f, Mathf.Sin(angle) * radius);

            GameObject barrel = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            barrel.name = $"Barrel {i}";
            barrel.transform.position = pos;
            barrel.transform.localScale = Vector3.one * barrelScale;
            Undo.RegisterCreatedObjectUndo(barrel, "Barrel 생성");
        }

        MarkDirty();
        Debug.Log($"[ArenaBuilder] 배럴 {barrelCount}개 재배치.");
    }

    // ─────────────────────────────────────────────────────────────
    // 유틸
    // ─────────────────────────────────────────────────────────────
    private static Transform GetOrCreateGroup(string name)
    {
        GameObject group = GameObject.Find(name);
        if (group == null)
        {
            group = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(group, name + " 생성");
        }

        return group.transform;
    }

    // 파이프라인(URP/기본)에 맞는 단색 머티리얼을 프로젝트에 만들어 재사용한다.
    private static Material GetTileMaterial(string name, Color color)
    {
        string path = $"{TileMaterialFolder}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
        {
            return mat;
        }

        if (!AssetDatabase.IsValidFolder(TileMaterialFolder))
        {
            string parent = System.IO.Path.GetDirectoryName(TileMaterialFolder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(TileMaterialFolder));
        }

        Shader shader = GraphicsSettings.currentRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void MarkDirty()
    {
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    private static string SceneManager_ActiveSceneName()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
}
