using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class RoomApiClient
{
    [Serializable]
    public class RoomDto
    {
        public long id;
        public string name;
        public string connectionType;
        public string connectionValue;
        public string mapId;
        public bool isPublic;
        public int maxPlayers;
        public int currentPlayers;
        public string status;
        public string createdAt;
        public string updatedAt;
    }

    [Serializable]
    public class CreateRoomRequest
    {
        public string name;
        public string connectionType;
        public string connectionValue;
        public string mapId;
        public bool isPublic;
        public int maxPlayers;
        public int currentPlayers;
    }

    [Serializable]
    private class RoomListWrapper
    {
        public RoomDto[] rooms;
    }

    private readonly string baseUrl;

    public RoomApiClient(string baseUrl)
    {
        this.baseUrl = NormalizeBaseUrl(baseUrl);
    }

    public async Task<RoomDto[]> GetRoomsAsync()
    {
        string json = await SendAsync("GET", "/rooms", null);
        RoomListWrapper wrapper = JsonUtility.FromJson<RoomListWrapper>("{\"rooms\":" + json + "}");
        return wrapper != null && wrapper.rooms != null ? wrapper.rooms : Array.Empty<RoomDto>();
    }

    public async Task<RoomDto> CreateRoomAsync(CreateRoomRequest request)
    {
        string json = JsonUtility.ToJson(request);
        return JsonUtility.FromJson<RoomDto>(await SendAsync("POST", "/rooms", json));
    }

    public async Task<RoomDto> JoinRoomAsync(long roomId)
    {
        return JsonUtility.FromJson<RoomDto>(await SendAsync("POST", $"/rooms/{roomId}/join", "{}"));
    }

    public async Task<RoomDto> LeaveRoomAsync(long roomId)
    {
        return JsonUtility.FromJson<RoomDto>(await SendAsync("POST", $"/rooms/{roomId}/leave", "{}"));
    }

    public async Task<RoomDto> CloseRoomAsync(long roomId)
    {
        return await SetRoomStatusAsync(roomId, "closed");
    }

    public async Task<RoomDto> SetRoomStatusAsync(long roomId, string status)
    {
        // 호스트 상태 변경만 가볍게 보낼 수 있도록 작은 PATCH 본문을 만듭니다.
        return JsonUtility.FromJson<RoomDto>(await SendAsync("PATCH", $"/rooms/{roomId}", $"{{\"status\":\"{EscapeJson(status)}\"}}"));
    }

    private async Task<string> SendAsync(string method, string path, string bodyJson)
    {
        using (UnityWebRequest request = new UnityWebRequest(baseUrl + path, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();

            if (bodyJson != null)
            {
                byte[] body = Encoding.UTF8.GetBytes(bodyJson);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                throw new InvalidOperationException($"{request.responseCode} {request.error} {response}".Trim());
            }

            return request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        }
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "http://localhost:3000";
        }

        return url.Trim().TrimEnd('/');
    }

    private static string EscapeJson(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
