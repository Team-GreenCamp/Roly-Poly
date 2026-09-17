using UnityEngine;

[DisallowMultipleComponent]
public class LobbySpawnPointGroup : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private bool wrapWhenFull = true;

    public bool TryGetSpawnPose(ulong ownerClientId, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        // 배열이 비어 있으면 자식 Transform들을 순서대로 사용한다(인스펙터 배선 불필요).
        int count = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints.Length : transform.childCount;
        if (count == 0)
        {
            return false;
        }

        // Netcode clientId 순서대로 미리 배치한 스폰 위치를 사용합니다.
        int index = (int)ownerClientId;
        if (wrapWhenFull)
        {
            index %= count;
        }
        else if (index >= count)
        {
            index = count - 1;
        }

        Transform spawnPoint = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[index]
            : transform.GetChild(index);
        if (spawnPoint == null)
        {
            return false;
        }

        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        return true;
    }
}
