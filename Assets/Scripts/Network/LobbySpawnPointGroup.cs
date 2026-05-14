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

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return false;
        }

        // Netcode clientId 순서대로 미리 배치한 대기 위치를 사용합니다.
        int index = (int)ownerClientId;
        if (wrapWhenFull)
        {
            index %= spawnPoints.Length;
        }
        else if (index >= spawnPoints.Length)
        {
            index = spawnPoints.Length - 1;
        }

        Transform spawnPoint = spawnPoints[index];
        if (spawnPoint == null)
        {
            return false;
        }

        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        return true;
    }
}
