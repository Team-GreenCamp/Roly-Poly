using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DecorateSurvivalArena
{
    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");
    static Transform Piece(Transform parent, string name, Vector3 pos, Vector3 size, string color)
        => DetailRaceObstacles.BeveledBox(name, parent, pos, size, .12f, Mat(color), Mat("Cream")).transform;

    [MenuItem("Tools/Battle Royal/Decorate Survival Arena")]
    public static void Build()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "Survival Arena")
            throw new System.InvalidOperationException("Open Survival Arena in Edit Mode.");
        if (GameObject.Find("Survival Festival Stadium") != null) return;
        // 장식은 발판 바깥에 배치하며 충돌체를 추가하지 않습니다.
        var root = new GameObject("Survival Festival Stadium").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Decorate Survival Arena");
        for (int side = 0; side < 4; side++)
        {
            var section = new GameObject("Stadium Side " + side).transform;
            section.SetParent(root, false);
            section.rotation = Quaternion.Euler(0, side * 90, 0);
            Piece(section, "Lower Frame", new Vector3(0,-3,23), new Vector3(44,.6f,.7f), "Orange");
            Piece(section, "Outer Safety Rail", new Vector3(0,1.3f,24), new Vector3(44,.6f,.6f), "Violet");
            for (int bay = -2; bay <= 2; bay++)
            {
                float x = bay * 8.2f;
                Piece(section, "Stand Foundation", new Vector3(x,-1.8f,27), new Vector3(7.6f,2.4f,6.8f), "Plum");
                Piece(section, "Front Fascia", new Vector3(x,-1.2f,23.55f), new Vector3(7.1f,1.1f,.15f), "Sky");
                for (int tier = 0; tier < 3; tier++)
                {
                    Piece(section, "Seating Tier", new Vector3(x,-.4f+tier*.65f,25+tier*1.8f), new Vector3(7.3f,.5f,1.7f), side%2==0 ? "Sky" : "Violet");
                    for (int seat = -2; seat <= 2; seat++)
                        Piece(section, "Seat Cushion", new Vector3(x+seat*1.35f,-.1f+tier*.65f,25+tier*1.8f), new Vector3(.95f,.13f,1), (seat+tier)%2==0 ? "Gold" : "Mint");
                }
                Piece(section, "Support", new Vector3(x,-6,24), new Vector3(.75f,12,.75f), "Orange");
                Piece(section, "Banner Pole", new Vector3(x,3,30), new Vector3(.18f,6,.18f), "Cream");
                Piece(section, "Festival Pennant", new Vector3(x,4.5f,30), new Vector3(2.1f,2.3f,.16f), bay%2==0 ? "Violet" : "Sky");
                var badge=Piece(section, "Diamond Badge", new Vector3(x,4.5f,29.88f), new Vector3(.8f,.8f,.08f), "Gold");
                badge.localRotation=Quaternion.Euler(0,0,45);
                for(int stripe=-2;stripe<=2;stripe++)
                    Piece(section,"Fascia Accent",new Vector3(x+stripe*1.25f,-1.2f,23.44f),new Vector3(.22f,.7f,.08f),"Cream");
            }
        }
        // 코너 조명탑은 카메라와 플레이 영역에서 충분히 떨어뜨립니다.
        foreach(int x in new[]{-1,1}) foreach(int z in new[]{-1,1})
        {
            var tower=new GameObject("Corner Light Tower").transform;tower.SetParent(root,false);
            tower.position=new Vector3(x*25,0,z*25);tower.LookAt(new Vector3(0,0,0));
            Piece(tower,"Tower Foot",new Vector3(0,-1,0),new Vector3(2.5f,2,2.5f),"Plum");
            Piece(tower,"Tower Mast",new Vector3(0,4,0),new Vector3(.9f,9,.9f),"Orange");
            Piece(tower,"Light Housing",new Vector3(0,8.5f,0),new Vector3(4,2.5f,.6f),"Violet");
            for(int a=-1;a<=1;a++)for(int b=0;b<2;b++)
                Piece(tower,"Lamp Panel",new Vector3(a*1.1f,8+b, .36f),new Vector3(.78f,.7f,.12f),"Cream");
        }
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("SURVIVAL DECOR PASS: " + root.GetComponentsInChildren<MeshRenderer>().Length + " decorative meshes; colliders=" + root.GetComponentsInChildren<Collider>().Length);
        Capture();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Battle Royal/Configure Timed Survival")]
    public static void ConfigureRules()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "Survival Arena")
            throw new System.InvalidOperationException("Open Survival Arena in Edit Mode.");
        var manager = Object.FindFirstObjectByType<SurvivalGameManager>();
        var settings = new SerializedObject(manager);
        settings.FindProperty("timedCoinSurvival").boolValue = true;
        settings.ApplyModifiedProperties();
        // 획득 개수가 그대로 승리 점수가 되도록 모든 코인은 1점입니다.
        var coins = new SerializedObject(Object.FindFirstObjectByType<SurvivalCoinManager>());
        coins.FindProperty("innerCoinValue").intValue = 1;
        coins.FindProperty("outerCoinValue").intValue = 1;
        coins.ApplyModifiedProperties();
        int count = 0;
        foreach (var tile in Object.FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None))
        {
            tile.triggerByStepping = true;
            tile.fallDelay = .6f;
            tile.respawnDelay = 3f;
            tile.respawnTweenSeconds = .6f;
            EditorUtility.SetDirty(tile);
            count++;
        }
        if (count != 289) throw new System.InvalidOperationException("Missing arena tiles: " + count);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("SURVIVAL RULES PASS: 289 regenerating tiles, 120-second coin survival.");
    }

    [MenuItem("Tools/Battle Royal/Match Survival Tiles To Falling Floors")]
    public static void MatchTiles()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "Survival Arena")
            throw new System.InvalidOperationException("Open Survival Arena in Edit Mode.");
        var root = GameObject.Find("Arena Tiles").transform;
        int count = 0;
        // 발판의 위치와 컴포넌트는 보존하고 Falling Floors의 유리 메시만 적용합니다.
        foreach (Transform tile in root)
        {
            if (!tile.name.StartsWith("Tile ")) continue;
            var renderer = tile.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            renderer.sharedMaterial = Mat("VioletGlass");
            DetailFallingFloors.ReplaceBox(tile.gameObject, .06f, true);
            if (tile.GetComponent<BoxCollider>() == null || tile.GetComponent<Unity.Netcode.NetworkObject>() == null)
                throw new System.InvalidOperationException("Tile physics/network component missing: " + tile.name);
            count++;
        }
        if (count != 289) throw new System.InvalidOperationException("Unexpected tile count: " + count);
        Capture();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("SURVIVAL TILE PASS: 289 Falling Floors glass meshes; original physics and network components preserved.");
    }

    [MenuItem("Tools/Battle Royal/Capture Survival Preview")]
    public static void Capture()
    {
        // UI 없이 경기장 전체를 썸네일로 촬영합니다.
        var obj=new GameObject("Temporary Survival Preview");var camera=obj.AddComponent<Camera>();
        obj.transform.position=new Vector3(53,48,-59);obj.transform.LookAt(new Vector3(0,-1,0));camera.farClipPlane=250;
        var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;camera.targetTexture=rt;
        camera.Render();RenderTexture.active=rt;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();System.IO.Directory.CreateDirectory("Screenshots");
        System.IO.File.WriteAllBytes("Screenshots/survival-preview.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);Object.DestroyImmediate(obj);
    }
}
