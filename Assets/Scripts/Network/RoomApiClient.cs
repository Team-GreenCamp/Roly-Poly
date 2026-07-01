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

    // 백엔드 인증 토큰(선택). 비어 있으면 기존처럼 인증 헤더 없이 요청합니다.
    // 보안을 실제로 적용하려면 Express 백엔드가 이 토큰을 검증해야 합니다(방 생성/삭제/수정 보호).
    public string AuthToken { get; set; }

    public RoomApiClient(string baseUrl, string authToken = null)
    {
        this.baseUrl = NormalizeBaseUrl(baseUrl);
        AuthToken = authToken;
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
        string json = await SendAsync("POST", $"/rooms/{roomId}/leave", "{}");
        return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<RoomDto>(json);
    }

    public async Task<RoomDto> CloseRoomAsync(long roomId)
    {
        return await SetRoomStatusAsync(roomId, "closed");
    }

    public async Task<RoomDto> SetRoomPlayerCountAsync(long roomId, int currentPlayers)
    {
        // 호스트가 알고 있는 실제 접속자 수를 백엔드 방 목록에 반영합니다.
        string json = await SendAsync("PATCH", $"/rooms/{roomId}", $"{{\"currentPlayers\":{currentPlayers}}}");
        return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<RoomDto>(json);
    }

    public async Task<RoomDto> SetRoomMapIdAsync(long roomId, string mapId)
    {
        // 로비에서 확정한 맵 ID를 방 목록에도 즉시 반영합니다.
        string json = await SendAsync("PATCH", $"/rooms/{roomId}", $"{{\"mapId\":\"{EscapeJson(mapId)}\"}}");
        return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<RoomDto>(json);
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

            // 토큰이 설정돼 있으면 인증 헤더를 함께 보냅니다(백엔드 검증 전제).
            if (!string.IsNullOrWhiteSpace(AuthToken))
            {
                request.SetRequestHeader("Authorization", "Bearer " + AuthToken);
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
