using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Survival Arena 템플릿으로 '왕관 잡기(Crown Grab)'와 '폭탄 돌리기(Hot Potato)' 씬을 생성하는 빌더.
// 두 맵 모두 Survival Arena의 정사각형 발판·관중석·범퍼를 쓰고, 발판은 밟아도 무너지지 않게 고정한다.
public static class BuildPartyArenas
{
    private const string TemplatePath = "Assets/Scenes/Survival Arena.unity";
    private const string CrownPath = "Assets/Scenes/Crown Grab.unity";
    private const string HotPotatoPath = "Assets/Scenes/Hot Potato.unity";
    private const string BarrelPrefabPath = "Assets/Prefab/SM_Prop_Barrel_01.prefab";
    private const string BombPrefabPath = "Assets/Prefab/Throwable Bomb.prefab";
    private const int SpawnCount = 8;
    private const float SpawnRadius = 10f;

    private static Material Mat(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    // 왕관 하나를 두고 뺏고 뺏기는 2분 점수전. 들고 있는 1초마다 1점, 점수 순으로 순위.
    // 폭탄과 범퍼는 남겨 왕관을 든 사람을 맞혀 떨어뜨릴 수 있게 한다.
    [MenuItem("Tools/Battle Royal/Build Crown Grab")]
    public static void BuildCrownGrab()
    {
        var scene = OpenFromTemplate(CrownPath, out var previous);
        if (!scene.IsValid()) return;

        RemoveRoots(scene, name => name.StartsWith("Barrel "));
        var bumpers = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Bumpers");
        var centerBumper = bumpers != null ? bumpers.transform.Find("Bumper 0") : null;
        if (centerBumper != null)
        {
            Object.DestroyImmediate(centerBumper.gameObject); // 가운데는 왕관 자리
        }

        var manager = Manager(scene);
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("timedCoinSurvival").boolValue = true;
        serialized.FindProperty("raceMode").boolValue = false;
        serialized.FindProperty("modeName").stringValue = "Crown Grab";
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 코인 점수판은 그대로 쓰고 코인만 뿌리지 않는다(점수는 왕관이 준다).
        var scoreBoard = new SerializedObject(manager.GetComponent<SurvivalCoinManager>());
        scoreBoard.FindProperty("spawnCoins").boolValue = false;
        scoreBoard.ApplyModifiedPropertiesWithoutUndo();

        float tileTop = StabilizeTiles(scene);
        ArrangeSpawns(scene, tileTop);
        CreateCrown(scene, tileTop);

        Save(scene, previous, CrownPath);
    }

    // 폭탄을 든 사람이 몸을 부딪혀 남에게 넘기는 생존전. 시간이 다 되면 든 사람이 터져 탈락.
    [MenuItem("Tools/Battle Royal/Build Hot Potato")]
    public static void BuildHotPotato()
    {
        var scene = OpenFromTemplate(HotPotatoPath, out var previous);
        if (!scene.IsValid()) return;

        RemoveRoots(scene, name => name.StartsWith("Barrel ") || name.StartsWith("Bomb "));

        var manager = Manager(scene);
        if (manager.TryGetComponent<SurvivalCoinManager>(out var coins))
        {
            Object.DestroyImmediate(coins);
        }
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("timedCoinSurvival").boolValue = false;
        serialized.FindProperty("raceMode").boolValue = false;
        serialized.FindProperty("modeName").stringValue = "Hot Potato";
        serialized.FindProperty("suddenDeathStartSeconds").floatValue = 0f; // 폭발로 끝나므로 발판 붕괴는 끈다
        serialized.ApplyModifiedPropertiesWithoutUndo();

        float tileTop = StabilizeTiles(scene);
        ArrangeSpawns(scene, tileTop);
        CreateHotPotato(scene);

        Save(scene, previous, HotPotatoPath);
    }

    private static void CreateCrown(Scene scene, float tileTop)
    {
        // 통 프리팹의 네트워크·잡기 구성(NetworkObject, GrabbableObject, NetworkTransform, NetworkRigidbody)을 빌려
        // 모양과 충돌체만 왕관으로 바꾼다.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrelPrefabPath);
        var crown = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        PrefabUtility.UnpackPrefabInstance(crown, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        crown.name = "Crown";
        crown.transform.SetPositionAndRotation(new Vector3(0f, tileTop + 0.5f, 0f), Quaternion.identity);
        crown.transform.localScale = Vector3.one;

        Object.DestroyImmediate(crown.GetComponent<MeshCollider>());
        Object.DestroyImmediate(crown.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(crown.GetComponent<MeshFilter>());
        var collider = crown.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.2f, 0f);
        collider.size = new Vector3(0.9f, 0.55f, 0.9f);

        Part(PrimitiveType.Cylinder, "Crown Band", crown.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.85f, 0.12f, 0.85f), Quaternion.identity, "Gold");
        for (int i = 0; i < 5; i++)
        {
            float angle = i * 72f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Part(PrimitiveType.Cube, "Crown Spike", crown.transform, dir * 0.36f + Vector3.up * 0.36f, new Vector3(0.2f, 0.2f, 0.2f), Quaternion.Euler(45f, angle, 45f), "Gold");
            Part(PrimitiveType.Sphere, "Crown Gem", crown.transform, dir * 0.43f + Vector3.up * 0.12f, Vector3.one * 0.13f, Quaternion.identity, i % 2 == 0 ? "Pink" : "Sky");
        }

        var beacon = Part(PrimitiveType.Cube, "Crown Beacon", crown.transform, Vector3.up * 1.6f, new Vector3(0.3f, 0.3f, 0.3f), Quaternion.Euler(45f, 0f, 35.26f), "Gold");

        var controller = crown.AddComponent<CrownController>();
        var controllerSerialized = new SerializedObject(controller);
        controllerSerialized.FindProperty("beacon").objectReferenceValue = beacon.transform;
        controllerSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateHotPotato(Scene scene)
    {
        var root = new GameObject("Hot Potato");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<NetworkObject>();
        var controller = root.AddComponent<HotPotatoController>();

        // 던지는 폭탄 프리팹의 모형(Visual)과 폭발 이펙트를 그대로 가져온다.
        var bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
        GameObject explosionVfx = null;
        Transform visual = null;
        if (bombPrefab != null)
        {
            var bomb = bombPrefab.GetComponent<ThrowableBomb>();
            if (bomb != null)
            {
                explosionVfx = (GameObject)new SerializedObject(bomb).FindProperty("explosionVfx").objectReferenceValue;
            }

            var source = bombPrefab.transform.Find("Visual");
            if (source != null)
            {
                visual = Object.Instantiate(source.gameObject, root.transform).transform;
                visual.name = "Potato Bomb";
                visual.localScale = source.localScale * 1.4f;
                foreach (var col in visual.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }
            }
        }

        if (visual == null)
        {
            visual = Part(PrimitiveType.Sphere, "Potato Bomb", root.transform, Vector3.zero, Vector3.one * 0.8f, Quaternion.identity, "Plum").transform;
        }

        var label = new GameObject("Fuse Countdown");
        label.transform.SetParent(visual, false);
        label.transform.localPosition = Vector3.up * 0.9f / Mathf.Max(0.01f, visual.localScale.y);
        var text = label.AddComponent<TextMeshPro>();
        text.text = "10";
        text.fontSize = 6;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(3f, 1.5f);

        var serialized = new SerializedObject(controller);
        serialized.FindProperty("bombVisual").objectReferenceValue = visual;
        serialized.FindProperty("countdownText").objectReferenceValue = text;
        serialized.FindProperty("explosionVfx").objectReferenceValue = explosionVfx;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ───── 공통 ─────

    private static Scene OpenFromTemplate(string path, out Scene previous)
    {
        previous = SceneManager.GetActiveScene();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplatePath) == null)
        {
            Debug.LogError("[Party Arenas] 템플릿 씬이 없어 빌드를 중단합니다: " + TemplatePath);
            return default;
        }

        // 기존 씬이 있으면 .meta(GUID)는 두고 내용만 덮어써 빌드 설정·참조가 끊기지 않게 한다.
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
        {
            System.IO.File.Copy(TemplatePath, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            AssetDatabase.CopyAsset(TemplatePath, path);
        }

        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        return scene;
    }

    private static void Save(Scene scene, Scene previous, string path)
    {
        GameFeedbackAutoWire.Wire();
        EditorSceneManager.SaveScene(scene);
        if (!EditorBuildSettings.scenes.Any(s => s.path == path))
        {
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }

        if (previous.IsValid() && previous != scene)
        {
            SceneManager.SetActiveScene(previous);
        }
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        Debug.Log("[Party Arenas] 씬을 생성했습니다: " + path);
    }

    private static SurvivalGameManager Manager(Scene scene) =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SurvivalGameManager>(true)).Single();

    private static void RemoveRoots(Scene scene, System.Func<string, bool> match)
    {
        foreach (var obj in scene.GetRootGameObjects())
        {
            if (match(obj.name))
            {
                Object.DestroyImmediate(obj);
            }
        }
    }

    // 발판은 밟아도 무너지지 않게 고정하고, 발판 윗면 높이를 돌려준다.
    private static float StabilizeTiles(Scene scene)
    {
        float tileTop = 0f;
        foreach (var platform in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FallingPlatform>(true)))
        {
            platform.triggerByStepping = false;
            EditorUtility.SetDirty(platform);
            if (platform.TryGetComponent<Renderer>(out var renderer))
            {
                tileTop = Mathf.Max(tileTop, renderer.bounds.max.y);
            }
        }
        return tileTop;
    }

    // 최대 인원만큼 원형으로 배치하고 가운데를 바라보게 한다.
    private static void ArrangeSpawns(Scene scene, float tileTop)
    {
        var spawns = scene.GetRootGameObjects().Single(g => g.name == "Spawn Points").transform;
        spawns.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        while (spawns.childCount < SpawnCount)
        {
            new GameObject("Spawn Point " + spawns.childCount).transform.SetParent(spawns, false);
        }

        var points = new Transform[spawns.childCount];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = (i + 0.5f) * Mathf.PI * 2f / points.Length; // 범퍼(대각선)와 겹치지 않게 반 칸 돌린다
            var position = new Vector3(Mathf.Sin(angle) * SpawnRadius, tileTop + 1f, Mathf.Cos(angle) * SpawnRadius);
            points[i] = spawns.GetChild(i);
            points[i].SetPositionAndRotation(position, Quaternion.LookRotation(-new Vector3(position.x, 0f, position.z)));
        }

        if (spawns.TryGetComponent<LobbySpawnPointGroup>(out var group))
        {
            var serialized = new SerializedObject(group);
            var prop = serialized.FindProperty("spawnPoints");
            prop.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, string material)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;
        obj.transform.localRotation = localRotation;
        obj.transform.localScale = localScale;
        obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }
}
