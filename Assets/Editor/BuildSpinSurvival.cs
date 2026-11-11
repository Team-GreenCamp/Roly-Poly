using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// '회전봉 생존(Spin Survival)' 씬을 Survival Arena 템플릿으로 생성하는 빌더.
// 원형 발판 가운데서 봉이 돌며 점점 빨라지고 가끔 방향을 바꾼다. 맞으면 날아가고,
// 시간이 지나면 바깥 발판부터 무너진다. 마지막까지 남은 1명이 승리한다.
public static class BuildSpinSurvival
{
    private const string Path = "Assets/Scenes/Spin Survival.unity";
    private const string TemplatePath = "Assets/Scenes/Survival Arena.unity";
    private const float ArenaRadius = 15.5f;   // 이 반경 밖의 발판은 지워 원형으로 만든다
    private const float SweeperReach = 15.8f;  // 봉 끝이 발판 가장자리까지 닿도록
    private const float SpawnRadius = 8f;
    private const int SpawnCount = 8;
    private static Transform root;

    private static Material Mat(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    [MenuItem("Tools/Battle Royal/Build Spin Survival")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplatePath) == null)
        {
            Debug.LogError("[Spin Survival] 템플릿 씬이 없어 빌드를 중단합니다: " + TemplatePath);
            return;
        }

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

        // 1. 서바이벌 전용 소품 정리 (발판·관중석·탈락 구역은 그대로 쓴다)
        foreach (var obj in scene.GetRootGameObjects())
        {
            if (obj.name == "Bumpers" || obj.name.StartsWith("Bomb ") || obj.name.StartsWith("Barrel "))
            {
                Object.DestroyImmediate(obj);
            }
        }
        var decor = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Arena Decor");
        var edgeTrim = decor != null ? decor.transform.Find("Edge Trim") : null;
        if (edgeTrim != null)
        {
            Object.DestroyImmediate(edgeTrim.gameObject);
        }

        // 2. 매니저: 코인 없이 마지막 1명이 남을 때까지. 서든데스는 늦게 걸어 경기가 끝나도록 보장만 한다.
        var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SurvivalGameManager>(true)).Single();
        if (manager.TryGetComponent<SurvivalCoinManager>(out var coins))
        {
            Object.DestroyImmediate(coins);
        }
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("timedCoinSurvival").boolValue = false;
        serialized.FindProperty("raceMode").boolValue = false;
        serialized.FindProperty("modeName").stringValue = "Spin Survival";
        serialized.FindProperty("knockbackMultiplier").floatValue = 1f;
        serialized.FindProperty("suddenDeathStartSeconds").floatValue = 150f;
        serialized.FindProperty("suddenDeathCollapseSeconds").floatValue = 10f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 3. 정사각형 발판을 원형으로 깎고, 밟아도 무너지지 않게(축소는 컨트롤러가 담당) 바꾼다.
        var tiles = scene.GetRootGameObjects().Single(g => g.name == "Arena Tiles").transform;
        float tileTop = 0f;
        foreach (var platform in tiles.GetComponentsInChildren<FallingPlatform>(true))
        {
            Vector3 p = platform.transform.position;
            if (new Vector2(p.x, p.z).magnitude > ArenaRadius)
            {
                Object.DestroyImmediate(platform.gameObject);
                continue;
            }

            platform.triggerByStepping = false;
            platform.respawnDelay = 0f;
            platform.respawnTweenSeconds = 0f;
            platform.fallDelay = 1.2f;
            platform.despawnAfterFallSeconds = 4f;
            platform.playFallVfx = true;
            EditorUtility.SetDirty(platform);
            if (platform.TryGetComponent<Renderer>(out var renderer))
            {
                tileTop = Mathf.Max(tileTop, renderer.bounds.max.y);
            }
        }

        // 4. 스폰: 봉 끝(가장 빠른 곳)과 가운데 기둥 사이 고리에 중앙을 바라보게 배치
        var spawns = scene.GetRootGameObjects().Single(g => g.name == "Spawn Points").transform;
        spawns.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        while (spawns.childCount < SpawnCount)
        {
            new GameObject("Spawn Point " + spawns.childCount).transform.SetParent(spawns, false);
        }
        var spawnPoints = new Transform[spawns.childCount];
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / spawnPoints.Length;
            var position = new Vector3(Mathf.Sin(angle) * SpawnRadius, tileTop + 1f, Mathf.Cos(angle) * SpawnRadius);
            spawnPoints[i] = spawns.GetChild(i);
            spawnPoints[i].SetPositionAndRotation(position, Quaternion.LookRotation(-new Vector3(position.x, 0f, position.z)));
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

        // 5. 가운데 기둥(경기 진행 컨트롤러)과 회전봉
        root = new GameObject("Spin Arena").transform;

        var hub = new GameObject("Spin Hub");
        hub.transform.SetParent(root);
        hub.transform.position = new Vector3(0f, tileTop, 0f);
        hub.AddComponent<NetworkObject>();
        var controller = hub.AddComponent<SpinSweeperController>();

        Primitive(PrimitiveType.Cylinder, "Hub Pillar", hub.transform, new Vector3(0f, tileTop + 1.1f, 0f), new Vector3(2.2f, 1.1f, 2.2f), "Plum", true);
        var cap = Primitive(PrimitiveType.Cylinder, "Hub Cap", hub.transform, new Vector3(0f, tileTop + 2.35f, 0f), new Vector3(2.6f, 0.15f, 2.6f), "Violet", false);
        Primitive(PrimitiveType.Sphere, "Hub Lamp", cap.transform, new Vector3(0f, tileTop + 2.7f, 0f), new Vector3(0.7f, 0.7f, 0.7f), "Gold", false);

        var controllerSerialized = new SerializedObject(controller);
        controllerSerialized.FindProperty("warningRenderer").objectReferenceValue = cap.GetComponent<Renderer>();
        controllerSerialized.FindProperty("warningMaterial").objectReferenceValue = Mat("Pink");
        controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

        // 봉은 발판 위 무릎 높이. 점프(1.2m)로 넘을 수 있고, 걸으면 맞는다.
        var bar = Primitive(PrimitiveType.Cube, "Spin Sweeper", root, new Vector3(0f, tileTop + 0.55f, 0f), new Vector3(SweeperReach * 2f, 0.35f, 0.45f), "Pink", true);
        var obstacle = bar.AddComponent<RaceObstacle>();
        obstacle.motion = RaceObstacle.Motion.Driven;
        obstacle.driver = controller;
        obstacle.knockdown = true;
        obstacle.launchForce = 6f;
        obstacle.launchLift = 2.5f;
        obstacle.velocityBoost = 0.25f;
        foreach (int side in new[] { -1, 1 })
        {
            Primitive(PrimitiveType.Cube, "Sweeper Tip", bar.transform, new Vector3(side * (SweeperReach - 0.4f), tileTop + 0.55f, 0f), new Vector3(0.8f, 0.45f, 0.55f), "Cream", false);
        }

        // 6. 게임 피드백(SFX/VFX) 자동 연결, 저장, Build Settings 등록
        GameFeedbackAutoWire.Wire();
        EditorSceneManager.SaveScene(scene);
        if (!EditorBuildSettings.scenes.Any(s => s.path == Path))
        {
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(Path, true) }).ToArray();
        }

        SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();

        Debug.Log("[Spin Survival] 씬을 생성했습니다: " + Path);
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, string material, bool solid)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.position = position;
        obj.transform.localScale = scale;
        obj.transform.SetParent(parent, true);
        obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
        if (!solid)
        {
            Object.DestroyImmediate(obj.GetComponent<Collider>());
        }
        return obj;
    }
}
