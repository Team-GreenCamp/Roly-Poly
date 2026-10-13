using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 폴가이즈 스타일의 '도어 대시(Door Dash)' 신규 미니게임 씬을 생성하는 빌더 스크립트.
public static class BuildDoorDash
{
    private const string Path = "Assets/Scenes/Door Dash.unity";
    private static Transform root;

    private static Material Mat(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    [MenuItem("Tools/Battle Royal/Build Door Dash")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Path) != null)
        {
            // 기존 씬이 있으면 덮어쓰기 위해 삭제 후 재생성
            AssetDatabase.DeleteAsset(Path);
        }

        // Sumo Arena 씬(공통 UI, BGM, HUD, 시상대, 네트워크가 완벽 세팅된 템플릿)을 복사
        AssetDatabase.CopyAsset("Assets/Scenes/Sumo Arena.unity", Path);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        // 기존 스모 프랍 및 타일 정리
        foreach (var obj in scene.GetRootGameObjects())
        {
            if (IsOldArenaProp(obj))
            {
                Object.DestroyImmediate(obj);
            }
        }

        root = new GameObject("Door Dash Course").transform;

        // 1. 코어 매니저 설정 (SurvivalGameManager -> Door Dash 레이스 모드)
        var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SurvivalGameManager>(true)).Single();
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("raceMode").boolValue = true;
        serialized.FindProperty("modeName").stringValue = "Door Dash";
        serialized.FindProperty("knockbackMultiplier").floatValue = 1.2f;
        serialized.FindProperty("suddenDeathStartSeconds").floatValue = 0f;
        serialized.FindProperty("raceTimeLimit").floatValue = 150f;

        // 커스텀 체크포인트 게이트 (1차문, 2차문, 3차문, 4차문, 피니시 라인)
        var gatesProp = serialized.FindProperty("customRaceGates");
        Vector3[] gates = new Vector3[]
        {
            new Vector3(0, 1.5f, 20f), // 1차 관문
            new Vector3(0, 1.5f, 40f), // 2차 관문
            new Vector3(0, 1.5f, 60f), // 3차 관문
            new Vector3(0, 1.5f, 80f), // 4차 관문
            new Vector3(0, 1.5f, 98f)  // 피니시 라인
        };
        gatesProp.arraySize = gates.Length;
        for (int i = 0; i < gates.Length; i++)
        {
            gatesProp.GetArrayElementAtIndex(i).vector3Value = gates[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 2. 스폰 포인트 배치 (출발선 정렬)
        var spawns = scene.GetRootGameObjects().Single(g => g.name == "Spawn Points").transform;
        spawns.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        int spawnCount = spawns.childCount;
        for (int i = 0; i < spawnCount; i++)
        {
            float x = -7f + (14f / Mathf.Max(1, spawnCount - 1)) * i;
            spawns.GetChild(i).SetPositionAndRotation(new Vector3(x, 1.5f, 2f), Quaternion.identity);
        }

        // 3. 직선 메인 트랙 바닥 구축 (폭 18m, 길이 105m)
        Box("Main Track 1", new Vector3(0, -0.5f, 25f), new Vector3(18f, 1f, 52f), "Gold", true);
        Box("Main Track 2", new Vector3(0, -0.5f, 75f), new Vector3(18f, 1f, 50f), "Gold", true);
        Box("Finish Track", new Vector3(0, -0.5f, 102f), new Vector3(18f, 1f, 12f), "Sky", true);

        // 트랙 양쪽 안전 난간 (Track Edges)
        Box("Left Guard Rail", new Vector3(-9.1f, 0.4f, 52f), new Vector3(0.4f, 1.2f, 110f), "Cream", true);
        Box("Right Guard Rail", new Vector3(9.1f, 0.4f, 52f), new Vector3(0.4f, 1.2f, 110f), "Cream", true);

        // 하단 슬라임 풀 (장외 낙사 배경)
        Box("Slime Pool", new Vector3(0, -8f, 52f), new Vector3(80f, 0.5f, 130f), "Pink");

        // 4. 관문 문(Doors) 및 벽체 구성
        // Wall 1 (z = 20m) : 4개 문 (1, 3번 진짜 / 0, 2번 가짜)
        BuildDoorWall(20f, 4, new bool[] { false, true, false, true }, "Violet", "Pink");

        // Wall 2 (z = 40m) : 4개 문 (0, 2번 진짜 / 1, 3번 가짜)
        BuildDoorWall(40f, 4, new bool[] { true, false, true, false }, "Plum", "Mint");

        // Wall 3 (z = 60m) : 3개 문 (1번 진짜 / 0, 2번 가짜)
        BuildDoorWall(60f, 3, new bool[] { false, true, false }, "Violet", "Gold");

        // Wall 4 (z = 80m) : 3개 문 (2번 진짜 / 0, 1번 가짜)
        BuildDoorWall(80f, 3, new bool[] { false, false, true }, "Plum", "Sky");

        // 5. 마지막 슬라임 다이빙 경사로 (z = 86m ~ 96m)
        var ramp = Box("Final Slide Ramp", new Vector3(0, 0.4f, 90f), new Vector3(16f, 0.8f, 12f), "Mint", true);
        ramp.transform.rotation = Quaternion.Euler(6f, 0, 0);

        // 6. 시작 아치 (START) & 결승 아치 (FINISH)
        Arch(0, 0f, "START", "Violet");
        Arch(0, 98f, "FINISH", "Pink");

        // 결승선 체커보드 바닥
        for (int i = 0; i < 16; i++)
        {
            for (int row = 0; row < 2; row++)
            {
                float x = -7.5f + i * 1f;
                float z = 97.5f + row * 0.8f;
                Box("Finish Checker", new Vector3(x, 0.02f, z), new Vector3(1f, 0.04f, 0.8f), (i + row) % 2 == 0 ? "Cream" : "Plum");
            }
        }

        // 7. 경기장 관중석 및 축제 깃발 장식
        for (int i = 0; i < 12; i++)
        {
            float z = i * 8.5f + 5f;
            foreach (int side in new[] { -1, 1 })
            {
                Box("Stadium Seat Block", new Vector3(side * 14f, 1f, z), new Vector3(4f, 3f, 7f), i % 2 == 0 ? "Plum" : "Sky");
                Box("Stadium Banner Pole", new Vector3(side * 13f, 5f, z), new Vector3(0.2f, 6f, 0.2f), "Cream");
                Box("Festival Banner", new Vector3(side * 13f, 6.5f, z), new Vector3(1.8f, 2.2f, 0.1f), i % 2 == 0 ? "Gold" : "Pink");
            }
        }

        // 8. 게임 피드백(SFX/VFX) 자동 연결
        GameFeedbackAutoWire.Wire();

        // 씬 저장 및 Build Settings 등록
        EditorSceneManager.SaveScene(scene);
        if (!EditorBuildSettings.scenes.Any(s => s.path == Path))
        {
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(Path, true) }).ToArray();
        }

        SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();

        Debug.Log("🎉 [Door Dash] 씬이 성공적으로 빌드되었습니다! (경로: " + Path + ")");
    }

    private static void BuildDoorWall(float z, int doorCount, bool[] isReal, string wallColor, string doorColor)
    {
        float totalWidth = 17.6f;
        float doorWidth = totalWidth / doorCount;
        float doorHeight = 3.6f;
        float wallHeight = 5.2f;

        // 상단 가로 벽체
        Box($"Wall_{z}_Header", new Vector3(0, doorHeight + (wallHeight - doorHeight) * 0.5f, z), new Vector3(totalWidth + 1f, wallHeight - doorHeight, 0.8f), wallColor, true);

        // 기둥들
        for (int i = 0; i <= doorCount; i++)
        {
            float pillarX = -totalWidth * 0.5f + i * doorWidth;
            Box($"Wall_{z}_Pillar_{i}", new Vector3(pillarX, doorHeight * 0.5f, z), new Vector3(0.5f, doorHeight, 0.8f), wallColor, true);
        }

        // 문짝 배치
        for (int i = 0; i < doorCount; i++)
        {
            float doorX = -totalWidth * 0.5f + (i + 0.5f) * doorWidth;
            bool real = isReal != null && i < isReal.Length && isReal[i];

            // 문 패널 (Cube)
            var doorObj = Box($"Door_{z}_{i}_{(real ? "REAL" : "FAKE")}", new Vector3(doorX, doorHeight * 0.5f, z), new Vector3(doorWidth - 0.6f, doorHeight - 0.2f, 0.35f), doorColor, true);

            // 멀티플레이어 동기화를 위한 NetworkObject 부착
            doorObj.AddComponent<NetworkObject>();

            // DoorDashGate 및 Rigidbody 컴포넌트 추가
            var rb = doorObj.AddComponent<Rigidbody>();
            rb.mass = real ? 15f : 1000f;
            rb.isKinematic = true; // 대기 상태에서는 닫혀있음

            var gate = doorObj.AddComponent<DoorDashGate>();
            gate.Setup(real, Mat("Mint")); // 열리면 민트색으로 표시

            // 문짝 중앙 장식 (폴가이즈 스타일 흰색/크림색 패턴)
            var emblem = Box($"DoorEmblem_{z}_{i}", new Vector3(doorX, doorHeight * 0.5f, z - 0.2f), new Vector3(1.2f, 1.2f, 0.08f), "Cream");
            emblem.transform.SetParent(doorObj.transform, true);
        }
    }

    private static GameObject Box(string name, Vector3 position, Vector3 scale, string material, bool solid = false)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(root);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
        if (!solid)
        {
            Object.DestroyImmediate(obj.GetComponent<Collider>());
        }
        return obj;
    }

    private static void Arch(float x, float z, string label, string color)
    {
        foreach (int side in new[] { -1, 1 })
        {
            Box(label + " Pillar", new Vector3(x + side * 8f, 3f, z), new Vector3(0.8f, 6f, 0.8f), color);
        }
        Box(label + " Header", new Vector3(x, 6f, z), new Vector3(17f, 1.4f, 0.8f), color);

        var obj = new GameObject(label);
        obj.transform.SetParent(root);
        obj.transform.position = new Vector3(x, 6f, z - 0.45f);
        var text = obj.AddComponent<TextMeshPro>();
        text.text = label;
        text.fontSize = 7;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(12, 2);
    }

    private static bool IsOldArenaProp(GameObject obj) =>
        obj.name == "Arena Props" || obj.name == "Arena Tiles" || obj.name == "Elimination Zone"
        || obj.name.StartsWith("Barrel ") || obj.name.StartsWith("SM_")
        || obj.name.StartsWith("PressurePlate") || obj.name == "HeavyObject"
        || obj.name.StartsWith("ArenaTile");

    [MenuItem("Tools/Battle Royal/Register Door Dash to Lobby")]
    public static void RegisterToLobby()
    {
        string lobbyScenePath = "Assets/Scenes/Lobby Scene.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(lobbyScenePath) == null)
        {
            Debug.LogError($"[Lobby] 로비 씬 파일을 찾을 수 없습니다: {lobbyScenePath}");
            return;
        }

        var previousScene = SceneManager.GetActiveScene();
        var lobbyScene = EditorSceneManager.OpenScene(lobbyScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(lobbyScene);

        bool modified = false;

        // 1. LobbyUIController의 mapSelections에 Chapter 4 (Door Dash) 추가
        var lobbyControllers = lobbyScene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<LobbyUIController>(true))
            .ToList();

        foreach (var controller in lobbyControllers)
        {
            var serialized = new SerializedObject(controller);
            var mapSelectionsProp = serialized.FindProperty("mapSelections");
            if (mapSelectionsProp != null)
            {
                bool exists = false;
                for (int i = 0; i < mapSelectionsProp.arraySize; i++)
                {
                    var elem = mapSelectionsProp.GetArrayElementAtIndex(i);
                    var sceneNameProp = elem.FindPropertyRelative("sceneName");
                    if (sceneNameProp != null && sceneNameProp.stringValue == "Door Dash")
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    int newIndex = mapSelectionsProp.arraySize;
                    mapSelectionsProp.InsertArrayElementAtIndex(newIndex);
                    var newElem = mapSelectionsProp.GetArrayElementAtIndex(newIndex);
                    newElem.FindPropertyRelative("chapterId").stringValue = "Chapter 4";
                    newElem.FindPropertyRelative("mapId").stringValue = "chapter-4";
                    newElem.FindPropertyRelative("sceneName").stringValue = "Door Dash";

                    // 이전 챕터의 배경 스프라이트를 기본값으로 복사
                    if (newIndex > 0)
                    {
                        var prevElem = mapSelectionsProp.GetArrayElementAtIndex(newIndex - 1);
                        newElem.FindPropertyRelative("buttonBackground").objectReferenceValue =
                            prevElem.FindPropertyRelative("buttonBackground").objectReferenceValue;
                    }

                    serialized.ApplyModifiedProperties();
                    modified = true;
                    Debug.Log("✅ [LobbyUIController] Door Dash 맵 선택 항목(Chapter 4)이 등록되었습니다.");
                }
            }
        }

        // 2. Heat ChapterManager의 chapters 목록에 Chapter 4 추가
        var chapterManagers = lobbyScene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Michsky.UI.Heat.ChapterManager>(true))
            .ToList();

        foreach (var cm in chapterManagers)
        {
            var serializedCm = new SerializedObject(cm);
            var chaptersProp = serializedCm.FindProperty("chapters");
            if (chaptersProp != null)
            {
                bool exists = false;
                for (int i = 0; i < chaptersProp.arraySize; i++)
                {
                    var item = chaptersProp.GetArrayElementAtIndex(i);
                    var idProp = item.FindPropertyRelative("chapterID");
                    if (idProp != null && (idProp.stringValue == "Chapter 4" || idProp.stringValue == "Door Dash"))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    int newIndex = chaptersProp.arraySize;
                    chaptersProp.InsertArrayElementAtIndex(newIndex);
                    var newItem = chaptersProp.GetArrayElementAtIndex(newIndex);
                    newItem.FindPropertyRelative("chapterID").stringValue = "Chapter 4";
                    newItem.FindPropertyRelative("title").stringValue = "도어 대시";
                    newItem.FindPropertyRelative("titleKey").stringValue = "MapDoorDash";
                    newItem.FindPropertyRelative("description").stringValue = "진짜 문을 찾아 부시고 결승선까지 달리세요!";
                    newItem.FindPropertyRelative("descriptionKey").stringValue = "MapDoorDashDesc";
                    newItem.FindPropertyRelative("defaultState").enumValueIndex = 1; // Unlocked

                    if (newIndex > 0)
                    {
                        var prevItem = chaptersProp.GetArrayElementAtIndex(newIndex - 1);
                        newItem.FindPropertyRelative("background").objectReferenceValue =
                            prevItem.FindPropertyRelative("background").objectReferenceValue;
                    }

                    serializedCm.ApplyModifiedProperties();
                    modified = true;
                    Debug.Log("✅ [ChapterManager] Chapter 4 (도어 대시) 챕터 항목이 등록되었습니다.");
                }
            }
        }

        if (modified)
        {
            EditorSceneManager.SaveScene(lobbyScene);
            AssetDatabase.SaveAssets();
            Debug.Log("🎉 [Lobby Scene] Door Dash 로비 등록이 완료 및 저장되었습니다!");
        }
        else
        {
            Debug.Log("ℹ️ [Lobby Scene] Door Dash 항목이 이미 등록되어 있습니다.");
        }

        SceneManager.SetActiveScene(previousScene);
        EditorSceneManager.CloseScene(lobbyScene, true);
    }
}
