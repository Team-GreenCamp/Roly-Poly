using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;

public static class RefineRaceWithProBuilder
{
    [MenuItem("Tools/Battle Royal/Validate ProBuilder Race")]
    public static void Validate()
    {
        var track = GameObject.Find("Obstacle Race Course/ProBuilder Track");
        if (track == null) throw new System.InvalidOperationException("Missing ProBuilder track.");
        var colliders = track.GetComponentsInChildren<MeshCollider>();
        if (colliders.Length != 3) throw new System.InvalidOperationException("Expected three solid track sections.");
        Physics.SyncTransforms();
        for (int i = 0; i <= 96; i++)
        {
            float a = Mathf.PI * i / 96;
            foreach (float radius in new[] { 7.5f, 12f, 16.5f })
            {
                Vector3 origin = new(radius * Mathf.Cos(a), 3, 55 + radius * Mathf.Sin(a));
                if (!colliders.Any(c => c.Raycast(new Ray(origin, Vector3.down), out var hit, 3.1f)))
                    throw new System.InvalidOperationException("Missing bend collision at " + origin);
            }
        }
        foreach (float x in new[] { -12f, 12f })
            for (int z = -4; z <= 55; z++)
                if (!colliders.Any(c => c.Raycast(new Ray(new Vector3(x,3,z), Vector3.down), out var hit, 3.1f)))
                    throw new System.InvalidOperationException("Missing straight collision.");
        Debug.Log("PROBUILDER TEST PASS: 411 road samples hit; three persistent editable track meshes and colliders.");
    }
    private static readonly Vector2[] Profile = {
        new(7.15f, 0), new(16.85f, 0), new(17, -.15f), new(17, -.85f),
        new(16.85f, -1), new(7.15f, -1), new(7, -.85f), new(7, -.15f)
    };
    private static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FallingFloors/" + name + ".mat");

    [MenuItem("Tools/Battle Royal/Refine Race With ProBuilder")]
    public static void Refine()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Obstacle Race.unity") throw new System.InvalidOperationException("Open Obstacle Race first.");
        var root = scene.GetRootGameObjects().Single(g => g.name == "Obstacle Race Course").transform;
        if (root.Find("ProBuilder Track") != null)
            throw new System.InvalidOperationException("Track already refined; preserving existing edits.");
        // 기존 코스 크기와 상단 높이는 유지하고 주행면만 모따기 메시로 교체합니다.
        foreach (var item in root.Cast<Transform>().ToArray())
        {
            if (new[] { "Outbound Track", "Return Track", "Turn Track", "Track Edge", "Turn Edge" }.Contains(item.name))
                Object.DestroyImmediate(item.gameObject);
            else if ((item.name == "Rotating Sweeper" || item.name == "Sweeper Hub") && Mathf.Abs(item.position.x) < 1)
                item.position += Vector3.forward * 7;
        }
        var track = new GameObject("ProBuilder Track").transform;
        track.SetParent(root, false);
        BuildStrip("Left Beveled Track", Profile, false, -1, track, true);
        BuildStrip("Right Beveled Track", Profile, false, 1, track, true);
        BuildStrip("Sweeping U Bend", Profile, true, 1, track, true);
        // 낮은 테두리는 진행 경계를 보여주되 점프와 낙사 판정을 방해하지 않습니다.
        foreach (float radius in new[] { 7.25f, 16.75f })
        {
            var trim = new[] { new Vector2(radius - .09f, .018f), new Vector2(radius + .09f, .018f),
                new Vector2(radius + .09f, -.035f), new Vector2(radius - .09f, -.035f) };
            BuildStrip("Inner Outer Trim", trim, true, 1, track, false);
            BuildStrip("Straight Trim", trim, false, -1, track, false);
            BuildStrip("Straight Trim", trim, false, 1, track, false);
        }
        // 곡선 구간의 방향 표식은 진행 방향(왼쪽에서 오른쪽)으로 배치합니다.
        for (int i = 1; i < 8; i++)
        {
            float angle = Mathf.PI * i / 8;
            Vector3 pos = new(12 * Mathf.Cos(angle), .03f, 55 + 12 * Mathf.Sin(angle));
            var verts = new[] { pos + new Vector3(-.45f,0,-.5f), pos + new Vector3(.45f,0,-.5f), pos + new Vector3(0,0,.65f) };
            Vector3 tangent = new(Mathf.Sin(angle), 0, -Mathf.Cos(angle));
            Quaternion rotation = Quaternion.LookRotation(tangent);
            for (int j = 0; j < verts.Length; j++) verts[j] = pos + rotation * (verts[j] - pos);
            var arrow = ProBuilderMesh.Create(verts, new[] { new Face(new[] { 0, 2, 1 }) });
            arrow.name = "Turn Direction"; arrow.transform.SetParent(track, false);
            arrow.GetComponent<Renderer>().sharedMaterial = Mat("Cream"); arrow.ToMesh(); arrow.Refresh();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("PROBUILDER PASS: 48-segment U bend, beveled straight tracks, matching trim, relocated sweeper.");
    }

    // 각 단면을 이어 닫힌 메시를 만들고 면별로 주행면·모따기·측면 재질을 분리합니다.
    private static void BuildStrip(string name, Vector2[] profile, bool curve, int side, Transform parent, bool solid)
    {
        int segments = curve ? 48 : 1;
        var vertices = new List<Vector3>(); var faces = new List<Face>();
        Vector3 Point(Vector2 p, int step)
        {
            if (!curve) return new Vector3(p.x * side, p.y, -5 + 60f * step);
            float a = Mathf.PI * step / segments;
            return new Vector3(p.x * Mathf.Cos(a), p.y, 55 + p.x * Mathf.Sin(a));
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int material)
        {
            int index = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            bool reverse = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0;
            int[] indices = reverse ? new[] { index, index+2, index+1, index, index+3, index+2 }
                : new[] { index, index+1, index+2, index, index+2, index+3 };
            faces.Add(new Face(indices) { submeshIndex = material });
        }
        for (int i = 0; i < segments; i++)
            for (int j = 0; j < profile.Length; j++)
            {
                int next = (j + 1) % profile.Length;
                Vector2 edge = profile[next] - profile[j];
                float a = Mathf.PI * (i + .5f) / segments;
                Vector3 radial = curve ? new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) : Vector3.right * side;
                Vector3 normal = -edge.y * radial + edge.x * Vector3.up;
                int material = !solid ? 2 : j == 0 ? 0 : (j == 1 || j == profile.Length - 1) ? 2 : 1;
                Quad(Point(profile[j], i), Point(profile[next], i), Point(profile[next], i+1), Point(profile[j], i+1), normal, material);
            }
        foreach (int end in new[] { 0, segments })
            for (int j = 1; j < profile.Length - 1; j++)
            {
                int index = vertices.Count;
                var a = Point(profile[0], end); var b = Point(profile[j], end); var c = Point(profile[j+1], end);
                Vector3 normal = curve ? Vector3.back : end == 0 ? Vector3.back : Vector3.forward;
                vertices.AddRange(new[] { a, b, c });
                faces.Add(new Face(Vector3.Dot(Vector3.Cross(b-a,c-a), normal) < 0
                    ? new[] { index,index+2,index+1 } : new[] { index,index+1,index+2 }) { submeshIndex = 1 });
            }
        var mesh = ProBuilderMesh.Create(vertices, faces);
        mesh.name = name; mesh.transform.SetParent(parent, false);
        mesh.GetComponent<Renderer>().sharedMaterials = new[] { Mat("Gold"), Mat("Plum"), Mat("Cream") };
        mesh.ToMesh(); mesh.Refresh();
        if (solid) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.GetComponent<MeshFilter>().sharedMesh;
    }
}
