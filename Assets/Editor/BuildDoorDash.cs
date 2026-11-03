using System.Collections.Generic;
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
    private const string TemplatePath = "Assets/Scenes/Survival Arena.unity";
    private const int SpawnCount = 8;
    private const float StandFrontX = 12f;              // 트랙(폭 18m) 바깥, 관중석 앞면까지의 거리
    private static readonly float[] StandCentersZ = { 8f, 52f, 96f }; // 관중석 한 면(44m) 3개로 트랙 전체를 덮는다
    private static Transform root;

    private static Material Mat(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    [MenuItem("Tools/Battle Royal/Build Door Dash")]
    public static void Build()
    {
        // 템플릿이 없으면 기존 씬을 지우기 전에 중단 (지우고 나서 복사에 실패하면 씬이 사라진다)
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplatePath) == null)
        {
            Debug.LogError("[Door Dash] 템플릿 씬이 없어 빌드를 중단합니다: " + TemplatePath);
            return;
        }

        // Survival Arena 씬(HUD, 시상대, 관중석, 하늘, 네트워크가 세팅된 템플릿)을 복사.
        // 기존 씬이 있으면 .meta(GUID)는 두고 내용만 덮어써 빌드 설정·참조가 끊기지 않게 한다.
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Path) != null)
        {
            System.IO.File.Copy(TemplatePath, Path, true);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            AssetDatabase.CopyAsset(TemplatePath, Path);
        }
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        // 서바이벌 전용 발판·소품·범퍼와 정사각형 경기장용 장식 정리
        foreach (var obj in scene.GetRootGameObjects())
        {
            if (IsOldArenaProp(obj))
            {
                Object.DestroyImmediate(obj);
            }
        }
        var decor = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Arena Decor");
        if (decor != null)
        {
            DestroyChild(decor.transform, "Edge Trim");
            DestroyChild(decor.transform, "Stadium Dressing/Bunting");
        }

        root = new GameObject("Door Dash Course").transform;

        // 1. 코어 매니저 설정 (SurvivalGameManager -> Door Dash 레이스 모드)
        var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SurvivalGameManager>(true)).Single();
        // 코인 매니저는 모드와 무관하게 코인을 뿌리므로 레이스 씬에서는 뺀다
        if (manager.TryGetComponent<SurvivalCoinManager>(out var coins))
        {
            Object.DestroyImmediate(coins);
        }
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("timedCoinSurvival").boolValue = false;
        serialized.FindProperty("raceMode").boolValue = true;
        serialized.FindProperty("modeName").stringValue = "Door Dash";
        serialized.FindProperty("knockbackMultiplier").floatValue = 1.2f;
        serialized.FindProperty("suddenDeathStartSeconds").floatValue = 0f;
        serialized.FindProperty("raceTimeLimit").floatValue = 150f;

        // 체크포인트 게이트 (1~4차 문 벽 바로 뒤, 피니시 라인)
        // 게이트 좌표는 리스폰 위치도 되므로 벽 안이 아니라 벽을 지난 트랙 위에 둔다.
        var gatesProp = serialized.FindProperty("raceGates");
        Vector3[] gates = new Vector3[]
        {
            new Vector3(0, 1.5f, 23f), // 1차 관문
            new Vector3(0, 1.5f, 43f), // 2차 관문
            new Vector3(0, 1.5f, 63f), // 3차 관문
            new Vector3(0, 1.5f, 83f), // 4차 관문
            new Vector3(0, 1.5f, 98f)  // 피니시 라인
        };
        serialized.FindProperty("raceStartRespawn").vector3Value = new Vector3(0, 1.5f, 2f);
        serialized.FindProperty("raceReturnGateCount").intValue = 99; // 직선 코스라 리스폰 방향을 뒤집지 않음
        serialized.FindProperty("raceGateHalfWidth").floatValue = 9f; // 트랙 폭 18m 전체(바깥 문 포함)
        gatesProp.arraySize = gates.Length;
        for (int i = 0; i < gates.Length; i++)
        {
            gatesProp.GetArrayElementAtIndex(i).vector3Value = gates[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 2. 스폰 포인트 배치 (출발선 정렬, 최대 인원만큼)
        var spawns = scene.GetRootGameObjects().Single(g => g.name == "Spawn Points").transform;
        spawns.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        while (spawns.childCount < SpawnCount)
        {
            new GameObject("Spawn Point " + spawns.childCount).transform.SetParent(spawns, false);
        }
        var spawnPoints = new Transform[spawns.childCount];
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            float x = -7f + (14f / Mathf.Max(1, spawnPoints.Length - 1)) * i;
            spawnPoints[i] = spawns.GetChild(i);
            spawnPoints[i].SetPositionAndRotation(new Vector3(x, 1.5f, 2f), Quaternion.identity);
        }
        if (spawns.TryGetComponent<LobbySpawnPointGroup>(out var spawnGroup))
        {
            var spawnSerialized = new SerializedObject(spawnGroup);
            var pointsProp = spawnSerialized.FindProperty("spawnPoints");
            pointsProp.arraySize = spawnPoints.Length;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                pointsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
            }
            spawnSerialized.ApplyModifiedPropertiesWithoutUndo();
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

        // 7. Survival Arena 관중석과 관중을 트랙 양옆으로 재배치
        ArrangeStadium(scene);

        // 8. 게임 피드백(SFX/VFX) 자동 연결
        GameFeedbackAutoWire.Wire();

        // 9. 인게임 일시정지 메뉴 프리팹 (ESC 메뉴) 배치
        var pauseMenuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/InGamePauseMenu.prefab");
        if (pauseMenuPrefab != null && !scene.GetRootGameObjects().Any(g => g.name == "InGamePauseMenu"))
        {
            var menuObj = (GameObject)PrefabUtility.InstantiatePrefab(pauseMenuPrefab, scene);
            menuObj.name = "InGamePauseMenu";
        }

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

    // 정사각형 경기장을 둘러싼 관중석 4면을 트랙 양옆 3면씩(총 6면)으로 옮긴다.
    // 관중(CrowdBounce)은 0번 면 좌석의 관중만 골라 각 면의 위치로 복제한다.
    private static void ArrangeStadium(Scene scene)
    {
        var stadium = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Survival Festival Stadium");
        if (stadium == null)
        {
            Debug.LogWarning("[Door Dash] 템플릿에 관중석(Survival Festival Stadium)이 없어 건너뜁니다.");
            return;
        }

        var sides = stadium.transform.Cast<Transform>().Where(t => t.name.StartsWith("Stadium Side")).ToList();
        var towers = stadium.transform.Cast<Transform>().Where(t => t.name == "Corner Light Tower").ToList();
        if (sides.Count == 0) return;

        // 원본 0번 면의 배치를 기준으로 관중을 옮기므로 이동 전에 행렬을 기록해 둔다.
        var source = sides[0];
        Matrix4x4 sourceToLocal = source.worldToLocalMatrix;
        float sourceYaw = source.eulerAngles.y;

        // 관중석 면은 로컬 +z 쪽(앞면 z=23)에 놓여 있다. y축으로 ±90° 돌리면 앞면이 트랙 옆 x=±StandFrontX에 온다.
        const float sectionFrontZ = 23f;
        var placements = new List<(Vector3 position, float yaw)>();
        foreach (float z in StandCentersZ)
        {
            placements.Add((new Vector3(StandFrontX - sectionFrontZ, 0f, z), 90f));  // 오른쪽(+x)
            placements.Add((new Vector3(sectionFrontZ - StandFrontX, 0f, z), -90f)); // 왼쪽(-x)
        }

        while (sides.Count < placements.Count)
        {
            var clone = Object.Instantiate(source.gameObject, stadium.transform);
            clone.name = "Stadium Side " + sides.Count;
            sides.Add(clone.transform);
        }

        var sectionMatrices = new List<(Matrix4x4 matrix, float yaw)>();
        for (int i = 0; i < sides.Count; i++)
        {
            if (i >= placements.Count)
            {
                Object.DestroyImmediate(sides[i].gameObject);
                continue;
            }
            sides[i].SetPositionAndRotation(placements[i].position, Quaternion.Euler(0f, placements[i].yaw, 0f));
            sectionMatrices.Add((sides[i].localToWorldMatrix, placements[i].yaw));
        }

        // 조명탑은 트랙 네 모서리 바깥에서 코스 중앙을 향하게 둔다.
        var towerSpots = new[] { new Vector3(-18f, 0f, -12f), new Vector3(18f, 0f, -12f), new Vector3(-18f, 0f, 116f), new Vector3(18f, 0f, 116f) };
        for (int i = 0; i < towers.Count; i++)
        {
            if (i >= towerSpots.Length)
            {
                Object.DestroyImmediate(towers[i].gameObject);
                continue;
            }
            towers[i].position = towerSpots[i];
            towers[i].LookAt(new Vector3(0f, 0f, 52f));
        }

        var crowd = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CrowdBounce>(true)).FirstOrDefault();
        if (crowd == null) return;

        var crowdSerialized = new SerializedObject(crowd);
        var spectatorsProp = crowdSerialized.FindProperty("spectators");
        var materialsProp = crowdSerialized.FindProperty("materials");
        var mesh = (Mesh)crowdSerialized.FindProperty("bodyMesh").objectReferenceValue;
        var materials = new Material[materialsProp.arraySize];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = (Material)materialsProp.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        Transform crowdTransform = crowd.transform;
        var arranged = new List<CrowdBounce.Spectator>();
        for (int i = 0; i < spectatorsProp.arraySize; i++)
        {
            var element = spectatorsProp.GetArrayElementAtIndex(i);
            var spectator = new CrowdBounce.Spectator
            {
                position = element.FindPropertyRelative("position").vector3Value,
                yaw = element.FindPropertyRelative("yaw").floatValue,
                scale = element.FindPropertyRelative("scale").floatValue,
                bodyMaterial = (byte)element.FindPropertyRelative("bodyMaterial").intValue,
                headMaterial = (byte)element.FindPropertyRelative("headMaterial").intValue,
                phase = element.FindPropertyRelative("phase").floatValue,
                tempo = element.FindPropertyRelative("tempo").floatValue,
                hopHeight = element.FindPropertyRelative("hopHeight").floatValue,
            };

            // 0번 면 좌석에 앉은 관중만 기준으로 쓴다.
            Vector3 inSource = sourceToLocal.MultiplyPoint3x4(crowdTransform.TransformPoint(spectator.position));
            if (inSource.z < 20f || Mathf.Abs(inSource.x) > 23f) continue;

            for (int s = 0; s < sectionMatrices.Count; s++)
            {
                var copy = spectator;
                copy.position = crowdTransform.InverseTransformPoint(sectionMatrices[s].matrix.MultiplyPoint3x4(inSource));
                copy.yaw = spectator.yaw + sectionMatrices[s].yaw - sourceYaw;
                copy.phase = Mathf.Repeat(spectator.phase + s * 0.37f, 1f); // 면마다 박자를 엇갈리게
                arranged.Add(copy);
            }
        }

        crowd.SetData(mesh, materials, arranged);
        EditorUtility.SetDirty(crowd);
    }

    private static void DestroyChild(Transform parent, string path)
    {
        var child = parent.Find(path);
        if (child != null)
        {
            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static bool IsOldArenaProp(GameObject obj) =>
        obj.name == "Arena Props" || obj.name == "Arena Tiles" || obj.name == "Elimination Zone"
        || obj.name == "Bumpers" || obj.name.StartsWith("Bomb ")
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
