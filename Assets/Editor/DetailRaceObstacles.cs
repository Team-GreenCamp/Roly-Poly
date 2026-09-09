using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;

public static class DetailRaceObstacles
{
    [MenuItem("Tools/Battle Royal/Validate Obstacle Details")]
    public static void Validate()
    {
        var root = GameObject.Find("Obstacle Race Course");
        int count=0;
        foreach (var mesh in root.GetComponentsInChildren<ProBuilderMesh>())
        {
            if (mesh.transform.parent.name != "ProBuilder Detail") continue;
            // 바깥쪽 면 방향을 검사해 빛과 뒷면 제거가 올바르게 적용되도록 합니다.
            foreach(var face in mesh.faces)
            {
                var indices=face.indexes;
                var a=mesh.positions[indices[0]];var b=mesh.positions[indices[1]];var c=mesh.positions[indices[2]];
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3)<0) face.Reverse();
            }
            mesh.ToMesh();mesh.Refresh();count++;
            if(mesh.GetComponent<Collider>() != null) throw new System.InvalidOperationException("Unexpected detail collider");
        }
        if(root.GetComponentsInChildren<RaceObstacle>().Length!=10) throw new System.InvalidOperationException("Motion count changed");
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("DETAIL TEST PASS: "+count+" editable meshes, outward faces, ten original obstacle motions, no detail colliders.");
    }

    [MenuItem("Tools/Battle Royal/Capture Obstacle Detail")]
    public static void Capture()
    {
        var go=new GameObject("Temporary Detail Camera");var camera=go.AddComponent<Camera>();
        go.transform.position=new Vector3(-5,4.5f,34);go.transform.LookAt(new Vector3(-12,1,43));
        var rt=new RenderTexture(1200,800,24);var previous=RenderTexture.active;
        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var image=new Texture2D(1200,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();
        System.IO.Directory.CreateDirectory("Screenshots");
        System.IO.File.WriteAllBytes("Screenshots/obstacle-details.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;
        Object.DestroyImmediate(image);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);
    }
    private static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    [MenuItem("Tools/Battle Royal/Detail Race Obstacles")]
    public static void Build()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "Obstacle Race")
            throw new System.InvalidOperationException("Open Obstacle Race in Edit Mode.");
        var root = GameObject.Find("Obstacle Race Course").transform;
        int count = 0;
        foreach (Transform item in root)
        {
            var obstacle = item.GetComponent<RaceObstacle>();
            if (obstacle == null && item.name != "Sweeper Hub") continue;
            if (item.Find("ProBuilder Detail") != null) continue;
            // 기존 강체와 박스 충돌체는 보존하고 시각 메시만 교체합니다.
            var renderer = item.GetComponent<MeshRenderer>();
            var oldMaterial = renderer.sharedMaterial;
            renderer.enabled = false;
            foreach (Transform child in item)
                if (child.name == "Sweeper Tip") child.gameObject.SetActive(false);
            var detail = new GameObject("ProBuilder Detail").transform;
            detail.SetParent(item, false);
            Vector3 size = item.localScale;
            detail.localScale = new Vector3(1 / size.x, 1 / size.y, 1 / size.z);
            BeveledBox("Chamfered Body", detail, Vector3.zero, size, Mathf.Min(size.y, size.z) * .2f,
                oldMaterial, Mat("Cream"));
            if (obstacle != null && obstacle.rotate)
            {
                // 봉 끝의 캡과 띠는 봉을 따라 움직이며 별도 충돌을 만들지 않습니다.
                foreach (int side in new[] { -1, 1 })
                {
                    BeveledBox("End Bumper", detail, new Vector3(side * (size.x / 2 - .28f),0,0),
                        new Vector3(.54f,size.y+.015f,size.z+.015f), .07f, Mat("Violet"), Mat("Cream"));
                    for (int i = 0; i < 3; i++)
                        BeveledBox("Safety Band", detail, new Vector3(side * (size.x / 2 - .75f - i*.48f),0,0),
                            new Vector3(.19f,size.y+.012f,size.z+.012f), .04f, Mat("Cream"), Mat("Cream"));
                }
            }
            else if (obstacle != null)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    BeveledBox("Inset Panel", detail, new Vector3(0,0,side*(size.z/2+.012f)),
                        new Vector3(size.x*.78f,size.y*.64f,.045f), .015f, Mat("Plum"), Mat("Cream"));
                    // 양면 꺾쇠 표식으로 왕복 장애물임을 알립니다.
                    for (int i = -1; i <= 1; i++)
                        foreach (int slope in new[] { -1, 1 })
                        {
                            var stripe = BeveledBox("Direction Chevron", detail,
                                new Vector3(i*size.x*.2f, slope*size.y*.07f, side*(size.z/2+.043f)),
                                new Vector3(size.x*.14f,.075f,.02f), .006f, Mat("Cream"), Mat("Cream"));
                            stripe.transform.localRotation = Quaternion.Euler(0,0,slope*-35);
                        }
                }
            }
            count++;
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("OBSTACLE DETAIL PASS: " + count + " bodies refined; original colliders and motion preserved.");
    }

    // 네 개의 팔각 단면을 이어 모서리와 앞뒤 테두리를 모두 모따기합니다.
    public static ProBuilderMesh BeveledBox(string name, Transform parent, Vector3 position, Vector3 size,
        float bevel, Material body, Material edge)
    {
        bevel = Mathf.Min(bevel, Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.24f);
        var vertices = new List<Vector3>(); var faces = new List<Face>();
        for (int ring = 0; ring < 4; ring++)
        {
            bool cap = ring == 0 || ring == 3;
            float x = size.x/2 - (cap ? bevel : 0), y = size.y/2 - (cap ? bevel : 0);
            float b = cap ? bevel*.35f : bevel;
            float z = ring < 2 ? -size.z/2 : size.z/2;
            if (!cap) z += ring == 1 ? bevel : -bevel;
            foreach (var p in new[] { new Vector2(-x+b,-y),new(x-b,-y),new(x,-y+b),new(x,y-b),
                new(x-b,y),new(-x+b,y),new(-x,y-b),new(-x,-y+b) })
                vertices.Add(new Vector3(p.x,p.y,z));
        }
        for (int ring=0;ring<3;ring++)
            for(int i=0;i<8;i++)
            {
                int a=ring*8+i,b=ring*8+(i+1)%8,c=b+8,d=a+8;
                faces.Add(new Face(new[]{a,b,c,a,c,d}) {submeshIndex=ring==1?0:1});
            }
        for(int i=1;i<7;i++)
        {
            faces.Add(new Face(new[]{0,i+1,i}) {submeshIndex=0});
            faces.Add(new Face(new[]{24,24+i,25+i}) {submeshIndex=0});
        }
        var mesh=ProBuilderMesh.Create(vertices,faces);
        mesh.name=name;mesh.transform.SetParent(parent,false);mesh.transform.localPosition=position;
        mesh.GetComponent<Renderer>().sharedMaterials=new[]{body,edge};mesh.ToMesh();mesh.Refresh();
        return mesh;
    }
}
