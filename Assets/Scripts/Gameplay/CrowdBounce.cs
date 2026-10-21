using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 관중석의 오뚝이 관중이 무작위로 통통 튀는 로컬 연출 (순수 클라이언트, 네트워킹 없음).
//
// 관중마다 GameObject를 만들지 않고 위치/색/박자 데이터만 직렬화해 두었다가,
// 매 프레임 Graphics.RenderMeshInstanced로 색(머티리얼)별 한 번씩 그린다(수백 명도 드로우콜 몇 개).
// 관중 데이터는 에디터 도구로 좌석 위치에서 생성한다. 머티리얼은 GPU Instancing이 켜져 있어야 한다.
[ExecuteAlways]
[DisallowMultipleComponent]
public class CrowdBounce : MonoBehaviour
{
    [Serializable]
    public struct Spectator
    {
        public Vector3 position;   // 좌석 윗면(로컬 좌표)
        public float yaw;          // 바라보는 방향(도)
        public float scale;
        public byte bodyMaterial;
        public byte headMaterial;
        public float phase;        // 박자 시작 오프셋(0~1)
        public float tempo;        // 초당 점프 횟수
        public float hopHeight;    // 기본 점프 높이(m)
    }

    [SerializeField] private Mesh bodyMesh;
    [SerializeField] private Material[] materials;
    [SerializeField] private List<Spectator> spectators = new List<Spectator>();

    [Header("Motion")]
    [Range(0f, 1f)] [SerializeField] private float skipChance = 0.35f;  // 이번 박자를 쉬고 가만히 있을 확률
    [Range(0f, 1f)] [SerializeField] private float airPortion = 0.45f;  // 한 박자 중 공중에 있는 비율
    [SerializeField] private float squash = 0.18f;                      // 착지 시 납작해지는 정도
    [SerializeField] private float stretch = 0.12f;                     // 공중에서 늘어나는 정도

    private const float LandPortion = 0.18f;
    private static readonly Vector3 BodyScale = new Vector3(0.62f, 0.66f, 0.58f);
    private static readonly Vector3 HeadScale = new Vector3(0.40f, 0.40f, 0.40f);

    private Matrix4x4[][] matrixBuffers;
    private int[] matrixCounts;
    private RenderParams[] renderParams;

    public void SetData(Mesh mesh, Material[] crowdMaterials, List<Spectator> crowd)
    {
        bodyMesh = mesh;
        materials = crowdMaterials;
        spectators = crowd;
        matrixBuffers = null;
    }

    private void Update()
    {
        // 헤드리스 데디케이티드 서버에서는 그릴 필요가 없다.
        if (bodyMesh == null || materials == null || materials.Length == 0 || spectators.Count == 0
            || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return;
        }

        EnsureBuffers();
        Array.Clear(matrixCounts, 0, matrixCounts.Length);

        float time = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 10000.0);
        Matrix4x4 local = transform.localToWorldMatrix;

        for (int i = 0; i < spectators.Count; i++)
        {
            Spectator s = spectators[i];
            Evaluate(i, s, time, out float lift, out float scaleY);
            float scaleXZ = 1f / Mathf.Sqrt(Mathf.Max(0.5f, scaleY)); // 부피 보존 느낌으로 옆으로 퍼진다

            Quaternion face = Quaternion.Euler(0f, s.yaw, 0f);
            Vector3 basePos = s.position + Vector3.up * lift;
            Vector3 squashScale = new Vector3(scaleXZ, scaleY, scaleXZ);

            // 오뚝이 체형: 아래가 무거운 달걀형 몸 + 작은 머리(몸이 눌리면 머리도 함께 내려온다)
            Vector3 bodySize = Vector3.Scale(BodyScale, squashScale) * s.scale;
            Matrix4x4 body = local * Matrix4x4.TRS(basePos + Vector3.up * (bodySize.y * 0.5f), face, bodySize);
            Matrix4x4 head = local * Matrix4x4.TRS(basePos + Vector3.up * (bodySize.y + HeadScale.y * 0.4f * s.scale), face, HeadScale * s.scale);
            Push(s.bodyMaterial, body);
            Push(s.headMaterial, head);
        }

        for (int m = 0; m < materials.Length; m++)
        {
            if (matrixCounts[m] > 0 && materials[m] != null)
            {
                Graphics.RenderMeshInstanced(renderParams[m], bodyMesh, 0, matrixBuffers[m], matrixCounts[m]);
            }
        }
    }

    // 박자 하나 = 점프(공중) → 착지 찌그러짐 → 대기. 박자마다 해시로 쉬기/높이를 달리해 무작위로 보이게 한다.
    private void Evaluate(int index, Spectator s, float time, out float lift, out float scaleY)
    {
        float beat = time * Mathf.Max(0.1f, s.tempo) + s.phase;
        int beatIndex = Mathf.FloorToInt(beat);
        float u = beat - beatIndex;

        float random = Hash(index, beatIndex);
        lift = 0f;
        scaleY = 1f;
        if (random < skipChance)
        {
            // 쉬는 박자: 제자리에서 살짝 들썩이기만 한다.
            scaleY = 1f + 0.03f * Mathf.Sin(u * Mathf.PI * 2f);
            return;
        }

        float height = s.hopHeight * Mathf.Lerp(0.6f, 1.3f, Hash(index * 7 + 3, beatIndex));
        if (u < airPortion)
        {
            float a = u / airPortion;
            lift = height * 4f * a * (1f - a);
            scaleY = 1f + stretch * Mathf.Sin(a * Mathf.PI);
        }
        else if (u < airPortion + LandPortion)
        {
            float b = (u - airPortion) / LandPortion;
            scaleY = 1f - squash * Mathf.Sin(b * Mathf.PI);
        }
    }

    private static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private void Push(byte materialIndex, Matrix4x4 matrix)
    {
        int m = Mathf.Min(materialIndex, materials.Length - 1);
        if (matrixCounts[m] < matrixBuffers[m].Length)
        {
            matrixBuffers[m][matrixCounts[m]++] = matrix;
        }
    }

    private void EnsureBuffers()
    {
        if (matrixBuffers != null && matrixBuffers.Length == materials.Length)
        {
            return;
        }

        int capacity = Mathf.Min(1023, spectators.Count * 2);
        matrixBuffers = new Matrix4x4[materials.Length][];
        matrixCounts = new int[materials.Length];
        renderParams = new RenderParams[materials.Length];
        for (int m = 0; m < materials.Length; m++)
        {
            matrixBuffers[m] = new Matrix4x4[capacity];
            renderParams[m] = new RenderParams(materials[m])
            {
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                layer = gameObject.layer,
                worldBounds = new Bounds(transform.position, Vector3.one * 200f),
            };
        }
    }

    private void OnValidate()
    {
        matrixBuffers = null;
    }
}
