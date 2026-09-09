using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// 원본 스모 씬은 보존하고 별도 레이스 씬을 생성합니다.
public static class BuildObstacleRace
{
    private const string Path = "Assets/Scenes/Obstacle Race.unity";
    private static Transform root;
    private static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    [MenuItem("Tools/Battle Royal/Build Obstacle Race")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Path) != null)
        {
            Debug.LogError("Obstacle Race already exists; generation skipped to preserve edits.");
            return;
        }
        AssetDatabase.CopyAsset("Assets/Scenes/Sumo Arena.unity", Path);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        foreach (var obj in scene.GetRootGameObjects())
            if (IsOldArenaProp(obj))
                Object.DestroyImmediate(obj);
        root = new GameObject("Obstacle Race Course").transform;
        var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SurvivalGameManager>(true)).Single();
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("raceMode").boolValue = true;
        serialized.FindProperty("modeName").stringValue = "Obstacle Race";
        serialized.FindProperty("knockbackMultiplier").floatValue = 1;
        serialized.FindProperty("suddenDeathStartSeconds").floatValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var spawns = scene.GetRootGameObjects().Single(g => g.name == "Spawn Points").transform;
        spawns.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        for (int i = 0; i < spawns.childCount; i++)
            spawns.GetChild(i).SetPositionAndRotation(new Vector3(-15 + i * 2, 1.5f, 0), Quaternion.identity);

        // 두 직선과 상단 연결부로 U자 코스를 구성합니다. 장식에는 충돌체를 두지 않습니다.
        Box("Outbound Track", new(-12, -.5f, 25), new(10, 1, 60), "Gold", true);
        Box("Return Track", new(12, -.5f, 25), new(10, 1, 60), "Gold", true);
        Box("Turn Track", new(0, -.5f, 60), new(34, 1, 10), "Sky", true);
        Box("Slime Pool", new(0, -7, 30), new(65, .3f, 100), "Pink");
        foreach (float x in new[] { -17.2f, -6.8f, 6.8f, 17.2f })
            Box("Track Edge", new(x, -.05f, 27), new(.25f, .15f, 64), "Cream");
        Box("Turn Edge", new(0, -.05f, 65), new(34, .15f, .25f), "Cream");
        foreach (float z in new[] { 18f, 38f, 58f })
            foreach (float x in new[] { -12f, 12f })
            {
                Box("Checkpoint Stripe", new(x, .012f, z), new(9.8f, .025f, .45f), "Mint");
                foreach (float dx in new[] { -4.8f, 4.8f })
                    Box("Checkpoint Marker", new(x + dx, .5f, z), new(.3f, 1, .3f), "Mint");
            }
        Sweeper(new(-12, .65f, 12), 55);
        Sweeper(new(-12, .65f, 29), -70);
        Sweeper(new(12, .65f, 29), 80);
        for (int i = 0; i < 3; i++)
        {
            var wall = Box("Sliding Gate", new(-12, 1.2f, 44 + i * 4), new(3.2f, 2.4f, .75f), i % 2 == 0 ? "Violet" : "Pink", true);
            var motion = wall.AddComponent<RaceObstacle>();
            motion.rotate = false; motion.speed = 1.2f; motion.travel = 3.4f; motion.phase = i * 1.7f;
        }
        Sweeper(new(0, .65f, 60), -55);
        for (int i = 0; i < 3; i++)
        {
            var wall = Box("Final Slider", new(12, .9f, 12 - i * 3), new(2.6f, 1.8f, .6f), "Sky", true);
            var motion = wall.AddComponent<RaceObstacle>();
            motion.rotate = false; motion.speed = 1.4f; motion.phase = i * 2;
        }
        Arch(-12, -3, "START", "Violet");
        Arch(12, 2, "FINISH", "Pink");
        for (int i = 0; i < 10; i++)
            for (int row = 0; row < 2; row++)
                Box("Finish Checker", new(7.5f + i, .022f, 2 + row * .6f), new(1, .04f, .6f), (i + row) % 2 == 0 ? "Cream" : "Plum");
        for (int i = 0; i < 9; i++)
            foreach (int side in new[] { -1, 1 })
            {
                float z = i * 8;
                Box("Stadium Plinth", new(side * 24, -1, z), new(4, 3, 6), "Plum");
                Box("Stadium Seat", new(side * 24, 1, z), new(4, 1, 6), i % 2 == 0 ? "Sky" : "Violet");
                Box("Banner Pole", new(side * 23, 4, z), new(.18f, 6, .18f), "Cream");
                Box("Festival Banner", new(side * 23, 6, z), new(2, 2.3f, .12f), i % 2 == 0 ? "Gold" : "Pink");
            }
        RefineRaceWithProBuilder.Refine();
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(Path, true) }).ToArray();
        SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        Debug.Log("Obstacle Race generated: U course, six sliders, four sweepers, checkpoints, finish and stadium.");
    }

    [MenuItem("Tools/Battle Royal/Preview Obstacle Race")]
    public static void Preview()
    {
        if (EditorApplication.isPlaying) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.path != Path) return;
        foreach (var obj in scene.GetRootGameObjects())
            if (IsOldArenaProp(obj)) Object.DestroyImmediate(obj);
        foreach (var obj in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
        {
            if (obj.name == "Outbound Track" || obj.name == "Return Track")
            {
                obj.position = new Vector3(obj.position.x, -.5f, 25);
                obj.localScale = new Vector3(10, 1, 60);
            }
            if (obj.name == "FINISH" && obj.GetComponent<TextMeshPro>() != null)
                obj.SetPositionAndRotation(new Vector3(12, 5, 2.36f), Quaternion.Euler(0, 180, 0));
        }
        EditorSceneManager.SaveScene(scene);
        SceneView.lastActiveSceneView?.LookAtDirect(new Vector3(0, 0, 30), Quaternion.Euler(55, 0, 0), 50);
    }

    private static bool IsOldArenaProp(GameObject obj) => obj.name == "Arena Props" || obj.name == "Arena Tiles"
        || obj.name == "Elimination Zone" || obj.name.StartsWith("Barrel ") || obj.name.StartsWith("SM_")
        || obj.name.StartsWith("PressurePlate") || obj.name == "HeavyObject";

    [MenuItem("Tools/Battle Royal/Capture Race Preview")]
    public static void CapturePreview()
    {
        var go = new GameObject("Temporary Preview Camera");
        var camera = go.AddComponent<Camera>();
        go.transform.position = new Vector3(48, 62, -32);
        go.transform.LookAt(new Vector3(0, 0, 30));
        camera.farClipPlane = 300;
        var rt = new RenderTexture(1280, 900, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(1280, 900, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); texture.Apply();
        System.IO.Directory.CreateDirectory("Screenshots");
        System.IO.File.WriteAllBytes("Screenshots/obstacle-race.png", texture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }

    [MenuItem("Tools/Battle Royal/Import Race Thumbnail")]
    public static void ImportThumbnail()
    {
        const string thumbnail = "Assets/Materials/FallingFloors/ObstacleRacePreview.png";
        System.IO.File.Copy("Screenshots/obstacle-race.png", thumbnail, true);
        AssetDatabase.ImportAsset(thumbnail);
        var importer = (TextureImporter)AssetImporter.GetAtPath(thumbnail);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static GameObject Box(string name, Vector3 position, Vector3 scale, string material, bool solid = false)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name; obj.transform.SetParent(root);
        obj.transform.position = position; obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
        if (!solid) Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }

    private static void Sweeper(Vector3 position, float speed)
    {
        Box("Sweeper Hub", position, new(.9f, 1.3f, .9f), "Violet");
        var bar = Box("Rotating Sweeper", position, new(8.5f, .35f, .4f), "Pink", true);
        bar.AddComponent<RaceObstacle>().speed = speed;
        // 봉 끝은 색상으로 구분하고 같은 강체에 속하도록 묶습니다.
        foreach (int side in new[] { -1, 1 })
        {
            var tip = Box("Sweeper Tip", position + Vector3.right * side * 3.8f, new(.5f, .38f, .44f), "Cream");
            tip.transform.SetParent(bar.transform, true);
        }
    }

    private static void Arch(float x, float z, string label, string color)
    {
        foreach (int side in new[] { -1, 1 })
            Box(label + " Pillar", new(x + side * 5, 2.5f, z), new(.65f, 5, .65f), color);
        Box(label + " Header", new(x, 5, z), new(10.6f, 1.1f, .65f), color);
        var obj = new GameObject(label); obj.transform.SetParent(root);
        obj.transform.position = new Vector3(x, 5, z - .36f);
        var text = obj.AddComponent<TextMeshPro>();
        text.text = label; text.fontSize = 6; text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(8, 1);
    }
}
