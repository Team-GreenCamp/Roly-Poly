using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

// 데디케이티드 서버가 Node.js 백엔드(방 목록)에 자기를 등록/갱신/해제한다.
// 클라이언트는 로비의 방 브라우저에서 이 방을 보고 connectionType="local"(직접 IP)로 접속한다.
//
// 필요한 실행 인자: -apiUrl <백엔드 URL> -publicIp <EC2 공인 IP> (-roomName "이름" 선택)
public class DedicatedServerRoomRegistrar : MonoBehaviour
{
    private RoomApiClient api;
    private long roomId = -1;
    private string connectionValue;
    private string roomName;
    private bool registering;

    public void Init(string apiUrl, string publicIp, int port, string roomName)
    {
        this.roomName = string.IsNullOrWhiteSpace(roomName) ? "AWS 서버" : roomName;
        connectionValue = $"{publicIp}:{port}";

        if (string.IsNullOrWhiteSpace(publicIp))
        {
            Debug.LogWarning("[Dedicated] -publicIp 미지정: 방 목록 등록을 건너뜁니다(클라가 공인 IP를 알 수 없음). 직접 IP 접속은 여전히 가능.");
            return;
        }
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            Debug.LogWarning("[Dedicated] -apiUrl 미지정: 방 목록 등록을 건너뜁니다.");
            return;
        }

        api = new RoomApiClient(apiUrl);
        RegisterAsync();

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback += HandleClientChanged;
            nm.OnClientDisconnectCallback += HandleClientChanged;
        }

        StartCoroutine(Heartbeat());
    }

    private async void RegisterAsync()
    {
        if (api == null || registering)
        {
            return;
        }
        registering = true;

        try
        {
            // 재시작 시 같은 주소의 옛 방이 남아 있으면 닫아 중복을 피한다.
            try
            {
                RoomApiClient.RoomDto[] existing = await api.GetRoomsAsync();
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] != null && existing[i].connectionValue == connectionValue && existing[i].status != "closed")
                    {
                        await api.CloseRoomAsync(existing[i].id);
                    }
                }
            }
            catch { /* 목록 조회 실패는 무시하고 새로 등록 */ }

            RoomApiClient.RoomDto room = await api.CreateRoomAsync(new RoomApiClient.CreateRoomRequest
            {
                name = roomName,
                connectionType = "local", // 직접 IP 접속
                connectionValue = connectionValue,
                mapId = string.Empty,
                isPublic = true,
                maxPlayers = 8,
                currentPlayers = 0,
            });

            roomId = room != null ? room.id : -1;
            Debug.Log($"[Dedicated] 방 목록 등록 완료 id={roomId} ({connectionValue})");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Dedicated] 방 등록 실패: {e.Message}");
        }
        finally
        {
            registering = false;
        }
    }

    private void HandleClientChanged(ulong _)
    {
        UpdatePlayerCountAsync();
    }

    private async void UpdatePlayerCountAsync()
    {
        if (api == null || roomId < 0)
        {
            return;
        }

        int count = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;
        try
        {
            await api.SetRoomPlayerCountAsync(roomId, count);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Dedicated] 인원 갱신 실패: {e.Message}");
        }
    }

    // 백엔드가 오래된 방을 정리하지 않도록 주기적으로 updatedAt을 갱신한다.
    private IEnumerator Heartbeat()
    {
        var wait = new WaitForSecondsRealtime(30f);
        while (true)
        {
            yield return wait;
            if (roomId >= 0)
            {
                UpdatePlayerCountAsync();
            }
            else if (api != null && !registering)
            {
                RegisterAsync(); // 초기 등록이 실패했으면 재시도
            }
        }
    }

    private async void OnApplicationQuit()
    {
        if (api != null && roomId >= 0)
        {
            try
            {
                await api.CloseRoomAsync(roomId);
            }
            catch { /* 종료 중이라 무시 */ }
        }
    }

    private void OnDestroy()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback -= HandleClientChanged;
            nm.OnClientDisconnectCallback -= HandleClientChanged;
        }
    }
}
