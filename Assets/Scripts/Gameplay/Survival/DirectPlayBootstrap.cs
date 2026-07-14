using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 개발용(에디터 전용): 로비를 거치지 않고 Survival Arena 씬에서 바로 Play를 눌러도 게임이 돌게 한다.
//
// 동작: 플레이 시작 시 활성 씬이 아레나이고 네트워크 세션이 없으면 Network Manager 프리팹을
// 생성하고 로컬 호스트를 시작한다(Relay/로그인 불필요 → 즉시 시작). 그러면 NGO가 플레이어와
// 인씬 NetworkObject(SurvivalGameManager, FallingPlatform 등)를 정상 스폰한다.
//
// 씬에 아무것도 배치할 필요 없는 정적 부트스트랩이며, 정상 플로우(로비 → Start)로 들어온
// 경우에는 세션이 이미 있으므로 아무것도 하지 않는다. 빌드에는 포함되지 않는다.
//
// 주의: MPPM 가상 플레이어로 이 씬을 직접 열면 각 인스턴스가 자기 호스트를 시작한다
// (같은 방 접속이 아님). 멀티 테스트는 기존처럼 로비에서 호스트/참가로 진행할 것.
public static class DirectPlayBootstrap
{
#if UNITY_EDITOR
    // 바로 플레이를 허용하는 모드 씬들(로비 없이 Play 시 자동 호스트).
    private static readonly string[] ArenaSceneNames = { "Survival Arena", "Sumo Arena", "Falling Floors" };
    private const string NetworkManagerPrefabPath = "Assets/Resources/Network Manager.prefab";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static async void AutoHostIfArenaDirectPlay()
    {
        if (System.Array.IndexOf(ArenaSceneNames, SceneManager.GetActiveScene().name) < 0)
        {
            return;
        }

        // 정상 플로우로 들어왔으면(세션 존재) 관여하지 않는다.
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
        {
            return;
        }

        if (networkManager == null)
        {
            GameObject prefab =
                UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[DirectPlay] Network Manager 프리팹을 찾지 못했습니다: {NetworkManagerPrefabPath}");
                return;
            }

            Object.Instantiate(prefab);
            networkManager = NetworkManager.Singleton;
        }

        if (networkManager == null)
        {
            Debug.LogWarning("[DirectPlay] NetworkManager 초기화에 실패했습니다.");
            return;
        }

        NetworkSessionManager sessionManager = networkManager.GetComponent<NetworkSessionManager>();
        if (sessionManager == null)
        {
            Debug.LogWarning("[DirectPlay] NetworkSessionManager를 찾지 못했습니다.");
            return;
        }

        // 고정 포트(7777)는 MPPM 가상 플레이어/다른 에디터 인스턴스/직전 플레이의 소켓과 충돌할 수 있다.
        // 직접 실행은 혼자 하는 테스트라 아무도 join하지 않으므로 매번 랜덤 포트를 쓴다.
        int port = Random.Range(7800, 7999);
        sessionManager.SetLocalPort(port);

        Debug.Log($"[DirectPlay] 세션이 없어 로컬 호스트를 시작합니다 (로비 건너뛰기, port={port}).");
        await sessionManager.StartLocalHostAsync();
    }
#endif
}
