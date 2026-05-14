using TMPro;
using UnityEngine;
using UnityEngine.Events;
using CanvasButton = UnityEngine.UI.Button;

[DisallowMultipleComponent]
public class RoomListItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text roomDetailText;
    [SerializeField] private CanvasButton joinButton;

    public void BindReferences(TMP_Text nameText, TMP_Text detailText, CanvasButton button)
    {
        roomNameText = nameText;
        roomDetailText = detailText;
        joinButton = button;
    }

    public void Bind(RoomApiClient.RoomDto room, UnityAction onJoinClicked)
    {
        if (room == null)
        {
            return;
        }

        if (roomNameText != null)
        {
            roomNameText.text = string.IsNullOrWhiteSpace(room.name) ? "이름 없는 방" : room.name;
        }

        if (roomDetailText != null)
        {
            string mapText = string.IsNullOrWhiteSpace(room.mapId) ? "맵 미지정" : room.mapId;
            roomDetailText.text = $"{room.currentPlayers} / {room.maxPlayers}  {GetRoomStatusText(room.status)}  {mapText}";
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(onJoinClicked);
            joinButton.interactable = room.status == "open" && room.currentPlayers < room.maxPlayers;
        }
    }

    private static string GetRoomStatusText(string status)
    {
        return status switch
        {
            "open" => "대기중",
            "in_game" => "진행중",
            "closed" => "닫힘",
            _ => string.IsNullOrWhiteSpace(status) ? "알 수 없음" : status
        };
    }
}
