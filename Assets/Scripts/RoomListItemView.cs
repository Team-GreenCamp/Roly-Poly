using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using CanvasButton = UnityEngine.UI.Button;
using HeatBoxButtonManager = Michsky.UI.Heat.BoxButtonManager;
using HeatModalWindowManager = Michsky.UI.Heat.ModalWindowManager;
using HeatPanelManager = Michsky.UI.Heat.PanelManager;
using HeatUIPopup = Michsky.UI.Heat.UIPopup;

[DisallowMultipleComponent]
public class RoomListItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text roomDetailText;
    [SerializeField] private CanvasButton joinButton;
    [SerializeField] private HeatBoxButtonManager heatJoinButton;

    private RoomApiClient.RoomDto boundRoom;
    private UnityAction joinClicked;
    private readonly List<GameObject> panelsToCloseOnJoin = new List<GameObject>();

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

        boundRoom = room;
        joinClicked = onJoinClicked;

        if (roomNameText != null)
        {
            roomNameText.text = string.IsNullOrWhiteSpace(room.name) ? "이름 없는 방" : room.name;
        }

        if (roomDetailText != null)
        {
            string mapText = string.IsNullOrWhiteSpace(room.mapId) ? "맵 미지정" : room.mapId;
            roomDetailText.text = $"{room.currentPlayers} / {room.maxPlayers}  {GetRoomStatusText(room.status)}  {mapText}";
        }

        bool canJoin = room.status == "open" && room.currentPlayers < room.maxPlayers;
        ResolveButtonReferences();

        if (heatJoinButton != null)
        {
            // 프리팹 OnClick에 HandleJoinClicked를 연결해도 되고, 없으면 런타임에서 자동 연결합니다.
            heatJoinButton.onClick.RemoveListener(HandleJoinClicked);
            if (!HasPersistentClickHandler(heatJoinButton.onClick))
            {
                heatJoinButton.onClick.AddListener(HandleJoinClicked);
            }

            heatJoinButton.Interactable(canJoin);
            return;
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(HandleJoinClicked);
            if (!HasPersistentClickHandler(joinButton.onClick))
            {
                joinButton.onClick.AddListener(HandleJoinClicked);
            }

            joinButton.interactable = canJoin;
        }
    }

    public void HandleJoinClicked()
    {
        if (boundRoom == null || joinClicked == null)
        {
            return;
        }

        ClosePanelsWithHeatAnimation();
        joinClicked.Invoke();
    }

    public void BindPanelToClose(GameObject panelRoot)
    {
        panelsToCloseOnJoin.Clear();
        if (panelRoot != null)
        {
            panelsToCloseOnJoin.Add(panelRoot);
        }
    }

    public void BindPanelsToClose(List<GameObject> panelRoots)
    {
        panelsToCloseOnJoin.Clear();
        if (panelRoots == null)
        {
            return;
        }

        for (int i = 0; i < panelRoots.Count; i++)
        {
            if (panelRoots[i] != null)
            {
                panelsToCloseOnJoin.Add(panelRoots[i]);
            }
        }
    }

    private void ClosePanelsWithHeatAnimation()
    {
        for (int i = 0; i < panelsToCloseOnJoin.Count; i++)
        {
            ClosePanelWithHeatAnimation(panelsToCloseOnJoin[i]);
        }
    }

    private void ClosePanelWithHeatAnimation(GameObject panelRoot)
    {
        if (panelRoot == null)
        {
            return;
        }

        HeatModalWindowManager modalWindow = panelRoot.GetComponent<HeatModalWindowManager>();
        if (modalWindow != null)
        {
            // Heat Modal은 CloseWindow를 호출해야 닫힘 애니메이션과 onClose 이벤트가 같이 실행됩니다.
            modalWindow.CloseWindow();
            return;
        }

        HeatUIPopup popup = panelRoot.GetComponent<HeatUIPopup>();
        if (popup != null)
        {
            popup.PlayOut();
            return;
        }

        HeatPanelManager panelManager = panelRoot.GetComponent<HeatPanelManager>();
        if (panelManager == null)
        {
            panelManager = panelRoot.GetComponentInParent<HeatPanelManager>();
        }

        if (panelManager != null)
        {
            panelManager.HideCurrentPanel();
            return;
        }

        Debug.LogWarning("룸 클릭 시 닫을 패널에 Heat UI 닫기 컴포넌트가 없어 닫힘 애니메이션을 실행하지 못했습니다.", panelRoot);
    }

    private void ResolveButtonReferences()
    {
        if (heatJoinButton == null)
        {
            heatJoinButton = GetComponent<HeatBoxButtonManager>();
        }

        if (joinButton == null)
        {
            joinButton = GetComponentInChildren<CanvasButton>(true);
        }
    }

    private static bool HasPersistentClickHandler(UnityEvent clickEvent)
    {
        if (clickEvent == null)
        {
            return false;
        }

        for (int i = 0; i < clickEvent.GetPersistentEventCount(); i++)
        {
            if (!string.IsNullOrWhiteSpace(clickEvent.GetPersistentMethodName(i)))
            {
                return true;
            }
        }

        return false;
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
