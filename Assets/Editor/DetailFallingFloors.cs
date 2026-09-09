using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;

public static class DetailFallingFloors
{
    private static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/"+name+".mat");

    [MenuItem("Tools/Battle Royal/Detail Falling Floors")]
    public static void Build()
    {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().name!="Falling Floors")
            throw new System.InvalidOperationException("Open Falling Floors in Edit Mode.");
        int tiles=0, scenery=0;
        // 기존 발판 오브젝트 자체를 편집하므로 네트워크 ID, 충돌체, 낙하 효과의 참조를 보존합니다.
        foreach(var tile in Object.FindObjectsByType<FallingPlatform>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(tile.GetComponent<ProBuilderMesh>()!=null)continue;
            ReplaceBox(tile.gameObject,.06f,true);tiles++;
        }
        foreach(var name in new[]{"Falling Floors - Festival Arena","Podium"})
        {
            var root=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==name);
            if(root==null)continue;
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true).ToArray())
            {
                if(renderer.GetComponent<ProBuilderMesh>()!=null)continue;
                string n=renderer.name;
                if(n=="Slime Surface"||n=="Slime Bubble"||n=="Marquee Bulb")continue;
                var size=renderer.transform.localScale;
                ReplaceBox(renderer.gameObject,Mathf.Min(.18f,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.2f),false);
                scenery++;
                if(n=="Festival Banner") Banner(renderer.transform);
                if(n=="Grandstand Colour") Seats(renderer.transform);
            }
        }
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("FALLING DETAIL PASS: "+tiles+" ProBuilder tiles, "+scenery+" scenery bodies; original physics preserved.");
    }

    private static void ReplaceBox(GameObject obj,float bevel,bool glass)
    {
        Vector3 scale=obj.transform.localScale;
        var material=obj.GetComponent<MeshRenderer>().sharedMaterial;
        var template=DetailRaceObstacles.BeveledBox("Temporary Shape",null,Vector3.zero,scale,bevel,material,glass?material:Mat("Cream"));
        var points=template.positions.Select(p=>new Vector3(p.x/scale.x,p.y/scale.y,p.z/scale.z)).ToArray();
        var faces=template.faces.Select(f=>new Face(f)).ToArray();
        Object.DestroyImmediate(template.gameObject);
        // 기존 큐브 메시를 비워 ProBuilder의 Reset이 미초기화 면을 재구축하지 않도록 합니다.
        obj.GetComponent<MeshFilter>().sharedMesh=null;
        var mesh=obj.AddComponent<ProBuilderMesh>();
        mesh.RebuildWithPositionsAndFaces(points,faces);
        obj.GetComponent<MeshRenderer>().sharedMaterials=new[]{material,glass?material:Mat("Cream")};
        mesh.ToMesh();mesh.Refresh();
    }

    private static Transform DetailRoot(Transform item)
    {
        var detail=new GameObject("Festival Detail").transform;detail.SetParent(item,false);
        Vector3 s=item.localScale;detail.localScale=new Vector3(1/s.x,1/s.y,1/s.z);return detail;
    }
    private static void Banner(Transform item)
    {
        var parent=DetailRoot(item);Vector3 s=item.localScale;
        foreach(int side in new[]{-1,1})
        {
            DetailRaceObstacles.BeveledBox("Banner Panel",parent,new Vector3(0,0,side*(s.z/2+.02f)),
                new Vector3(s.x*.8f,s.y*.82f,.05f),.012f,Mat("Plum"),Mat("Cream"));
            var badge=DetailRaceObstacles.BeveledBox("Festival Emblem",parent,new Vector3(0,0,side*(s.z/2+.06f)),
                new Vector3(.9f,.9f,.04f),.009f,Mat("Gold"),Mat("Cream"));
            badge.transform.localRotation=Quaternion.Euler(0,0,45);
        }
    }
    private static void Seats(Transform item)
    {
        var parent=DetailRoot(item);var material=item.GetComponent<MeshRenderer>().sharedMaterial;
        float direction=Mathf.Sign(item.position.z);
        for(int tier=0;tier<3;tier++)
        {
            float z=direction*(-1.5f+tier*1.5f);
            DetailRaceObstacles.BeveledBox("Seating Terrace",parent,new Vector3(0,1.15f+tier*.32f,z),
                new Vector3(5.7f,.28f,1.4f),.065f,material,Mat("Cream"));
            for(int seat=0;seat<5;seat++)
                DetailRaceObstacles.BeveledBox("Seat Pad",parent,new Vector3(-2.2f+seat*1.1f,1.32f+tier*.32f,z),
                    new Vector3(.8f,.07f,.8f),.015f,Mat("Plum"),Mat("Plum"));
        }
    }

    [MenuItem("Tools/Battle Royal/Validate Falling Details")]
    public static void Validate()
    {
        var tiles=Object.FindObjectsByType<FallingPlatform>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        if(tiles.Length!=900)throw new System.InvalidOperationException("Tile count changed.");
        foreach(var tile in tiles)
        {
            var mesh=tile.GetComponent<ProBuilderMesh>();var box=tile.GetComponent<BoxCollider>();
            if(mesh==null||box==null||!box.enabled||tile.GetComponent<Unity.Netcode.NetworkObject>()==null)
                throw new System.InvalidOperationException("Missing tile mesh, collider or network identity.");
            float alpha=tile.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor").a;
            if(Mathf.Abs(alpha-.88f)>.001f)throw new System.InvalidOperationException("Glass opacity changed.");
        }
        Debug.Log("FALLING TEST PASS: 900 editable tiles, box colliders and network identities; glass alpha 0.88 preserved.");
    }

    [MenuItem("Tools/Battle Royal/Capture Falling Details")]
    public static void Capture()
    {
        var obj=new GameObject("Temporary Falling Preview");var camera=obj.AddComponent<Camera>();
        obj.transform.position=new Vector3(27,9,-29);obj.transform.LookAt(new Vector3(0,-12,0));camera.farClipPlane=200;
        var rt=new RenderTexture(1280,900,24);var previous=RenderTexture.active;camera.targetTexture=rt;
        camera.Render();RenderTexture.active=rt;var image=new Texture2D(1280,900,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,900),0,0);image.Apply();System.IO.Directory.CreateDirectory("Screenshots");
        System.IO.File.WriteAllBytes("Screenshots/falling-details.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);Object.DestroyImmediate(obj);
    }
}
