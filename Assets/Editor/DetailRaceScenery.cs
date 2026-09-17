using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DetailRaceScenery
{
    private static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/"+name+".mat");
    private static void Piece(string name, Transform parent, Vector3 position, Vector3 size, string color, float bevel=.08f)
        => DetailRaceObstacles.BeveledBox(name,parent,position,size,bevel,Mat(color),Mat("Cream"));

    [MenuItem("Tools/Battle Royal/Detail Race Scenery")]
    public static void Build()
    {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().name!="Obstacle Race")
            throw new System.InvalidOperationException("Open Obstacle Race in Edit Mode.");
        var root=GameObject.Find("Obstacle Race Course").transform;
        int count=0;
        // 장식의 위치와 충돌 설정은 그대로 두고 편집 가능한 메시만 추가합니다.
        foreach(var item in root.Cast<Transform>().ToArray())
        {
            string name=item.name;
            if(!(name=="Stadium Plinth" || name=="Stadium Seat" || name=="Festival Banner" || name=="Banner Pole"
                || name.EndsWith(" Pillar") || name.EndsWith(" Header") || name=="Checkpoint Marker")) continue;
            if(item.Find("Scenery Detail")!=null) continue;
            var renderer=item.GetComponent<MeshRenderer>(); if(renderer==null) continue;
            var material=renderer.sharedMaterial; renderer.enabled=false;
            var detail=new GameObject("Scenery Detail").transform;detail.SetParent(item,false);
            Vector3 size=item.localScale;
            detail.localScale=new Vector3(1/size.x,1/size.y,1/size.z);
            if(name=="Stadium Seat")
            {
                // 트랙 쪽은 낮고 바깥쪽은 높은 세 단의 관람석으로 만듭니다.
                float side=Mathf.Sign(item.position.x);
                for(int tier=0;tier<3;tier++)
                {
                    float x=side*(-1.3f+tier*1.3f);
                    DetailRaceObstacles.BeveledBox("Seating Tier",detail,new Vector3(x,-.25f+tier*.3f,0),
                        new Vector3(1.28f,.35f,5.8f),.1f,material,Mat("Cream"));
                    for(int seat=0;seat<5;seat++)
                        Piece("Seat Pad",detail,new Vector3(x,-.045f+tier*.3f,-2.25f+seat*1.12f),
                            new Vector3(.8f,.075f,.75f),"Plum",.025f);
                }
            }
            else
            {
                DetailRaceObstacles.BeveledBox("Beveled Shell",detail,Vector3.zero,size,
                    Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.18f,material,Mat("Cream"));
                if(name=="Stadium Plinth")
                {
                    float x=-Mathf.Sign(item.position.x)*(size.x/2+.02f);
                    Piece("Stand Fascia",detail,new Vector3(x,.15f,0),new Vector3(.08f,1.65f,5.35f),"Violet",.02f);
                    for(int i=-2;i<=2;i++)
                        Piece("Fascia Slat",detail,new Vector3(x-Mathf.Sign(item.position.x)*.06f,.15f,i*1.05f),
                            new Vector3(.06f,1.2f,.12f),"Gold",.02f);
                }
                else if(name=="Festival Banner")
                {
                    foreach(int side in new[]{-1,1})
                    {
                        Piece("Banner Inset",detail,new Vector3(0,0,side*.075f),new Vector3(1.65f,1.92f,.035f),"Plum",.01f);
                        var badge=DetailRaceObstacles.BeveledBox("Diamond Emblem",detail,new Vector3(0,0,side*.105f),
                            new Vector3(.7f,.7f,.025f),.006f,Mat("Gold"),Mat("Cream"));
                        badge.transform.localRotation=Quaternion.Euler(0,0,45);
                        Piece("Banner Accent",detail,new Vector3(0,-.67f,side*.105f),new Vector3(1.15f,.08f,.025f),"Cream",.006f);
                    }
                }
                else if(name=="Banner Pole")
                {
                    Piece("Pole Foot",detail,new Vector3(0,-size.y/2+.16f,0),new Vector3(.55f,.32f,.55f),"Plum");
                    Piece("Pole Finial",detail,new Vector3(0,size.y/2+.08f,0),new Vector3(.32f,.22f,.32f),"Gold");
                }
                else if(name.EndsWith(" Pillar"))
                {
                    Piece("Gate Foot",detail,new Vector3(0,-size.y/2+.22f,0),new Vector3(.9f,.44f,.9f),"Plum");
                    for(int i=0;i<3;i++)
                        Piece("Gate Band",detail,new Vector3(0,-1.3f+i*1.3f,0),new Vector3(size.x+.04f,.14f,size.z+.04f),"Cream",.03f);
                }
                else if(name.EndsWith(" Header"))
                {
                    // 문구 영역은 비워 두고 양쪽에 밝은 캡과 램프를 배치합니다.
                    foreach(int side in new[]{-1,1})
                    {
                        Piece("Header End Cap",detail,new Vector3(side*(size.x/2-.25f),0,0),new Vector3(.45f,size.y+.04f,size.z+.04f),"Plum");
                        foreach(int face in new[]{-1,1})
                            Piece("Gate Lamp",detail,new Vector3(side*(size.x/2-.75f),0,face*(size.z/2+.03f)),new Vector3(.2f,.45f,.06f),"Gold",.015f);
                    }
                }
                else if(name=="Checkpoint Marker")
                    Piece("Checkpoint Cap",detail,new Vector3(0,size.y/2-.08f,0),new Vector3(.34f,.16f,.34f),"Cream",.035f);
            }
            count++;
        }
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("SCENERY DETAIL PASS: "+count+" decorations refined; no new colliders or runtime behaviours.");
    }
}
