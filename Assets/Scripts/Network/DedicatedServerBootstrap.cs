using Unity.Netcode;
using UnityEngine;

// 데디케이티드 서버(헤드리스, AWS EC2 등) 부트스트랩.
// 서버 빌드(UNITY_SERVER) 또는 -dedicatedServer 커맨드라인 인자로 실행하면
// 로비/입력 없이 순수 서버로 열고 지정한 아레나 씬을 로드한다.
//
// 실행 예:  ./BattleRoyalServer.x86_64 -dedicatedServer -port 7777 -scene "Sumo Arena"
// (서버 빌드에서는 인자 없이도 자동 서버 모드로 동작)
public static class DedicatedServerBootstrap
{
    private const int DefaultPort = 7777;
    private const string DefaultScene = "Sumo Arena";

    // SGM이 매치 종료 시 로비 복귀 대신 아레나를 재시작(루프)할지 판단하는 플래그.
    public static bool IsDedicatedServer { get; private set; }
    // 서버가 반복 로드할 아레나 씬(매치 루프용).
    public static string ServerGameScene { get; private set; } = DefaultScene;

    // 빌드 scene 0(로비 씬)에 Network Manager가 있으므로 씬 로드 후 Singleton을 사용한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!ShouldRunAsServer())
        {
            return;
        }

        IsDedicatedServer = true;

        int port = GetIntArg("-port", DefaultPort);
        string scene = GetStringArg("-scene", DefaultScene);
        ServerGameScene = scene;

        // 헤드리스 서버는 CPU/전력 절약을 위해 프레임레이트를 낮춘다.
        Application.targetFrameRate = 30;
        QualitySettings.vSyncCount = 0;

        // 첫 씬(Title 등)에 Network Manager가 없을 수 있으므로 Resources에서 직접 생성한다.
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Network Manager");
            if (prefab == null)
            {
                Debug.LogError("[Dedicated] Resources/Network Manager 프리팹을 찾지 못했습니다.");
                return;
            }
            Object.Instantiate(prefab);
            networkManager = NetworkManager.Singleton;
        }

        if (networkManager == null)
        {
            Debug.LogError("[Dedicated] NetworkManager 초기화 실패.");
            return;
        }

        NetworkSessionManager session = networkManager.GetComponent<NetworkSessionManager>();
        if (session == null)
        {
            Debug.LogError("[Dedicated] NetworkSessionManager를 찾지 못했습니다.");
            return;
        }

        if (!session.StartDedicatedServer(port, scene))
        {
            return;
        }

        // 방 목록(Node.js 백엔드)에 자기 등록 — 클라이언트가 서버 브라우저에서 보고 접속.
        string apiUrl = GetStringArg("-apiUrl", null);
        string publicIp = GetStringArg("-publicIp", null);
        string roomName = GetStringArg("-roomName", "AWS 서버");
        if (!string.IsNullOrWhiteSpace(publicIp) && !string.IsNullOrWhiteSpace(apiUrl))
        {
            GameObject registrarGo = new GameObject("Dedicated Server Room Registrar");
            Object.DontDestroyOnLoad(registrarGo);
            DedicatedServerRoomRegistrar registrar = registrarGo.AddComponent<DedicatedServerRoomRegistrar>();
            registrar.Init(apiUrl, publicIp, port, roomName);
        }
        else
        {
            Debug.LogWarning("[Dedicated] -apiUrl/-publicIp 미지정 → 방 목록 미등록(직접 IP 접속만 가능).");
        }
    }

    private static bool ShouldRunAsServer()
    {
#if UNITY_EDITOR
        // 빌드 타깃이 Dedicated Server면 에디터에도 UNITY_SERVER가 켜진다.
        // 에디터 플레이는 절대 서버 모드로 부팅하지 않는다(입력 잠김/자동 서버 시작 방지).
        return false;
#elif UNITY_SERVER
        return true;
#else
        return HasArg("-dedicatedServer");
#endif
    }

    private static bool HasArg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name) return true;
        }
        return false;
    }

    private static int GetIntArg(string name, int fallback)
    {
        string raw = GetStringArg(name, null);
        return int.TryParse(raw, out int value) ? value : fallback;
    }

    private static string GetStringArg(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return fallback;
    }
}
