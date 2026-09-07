using Michsky.UI.Heat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 일반 uGUI 버튼의 클릭/포커스 소리. 런타임 생성 버튼에는 이 컴포넌트를 프리팹에 붙입니다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class UIButtonSound : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    private Button button;
    private int lastHoverFrame = -1;

    private void OnEnable()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(PlayClick);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(PlayClick);
    }

    private bool CanPlay => button.IsActive() && button.IsInteractable() && !HasHeatSound(gameObject);

    private void PlayClick()
    {
        // Button이 입력 유효성을 확인합니다. 먼저 패널을 닫은 콜백 뒤에도 클릭음은 재생합니다.
        if (!HasHeatSound(gameObject) && UIManagerAudio.instance is UISoundController sounds) sounds.PlayClick();
    }

    public void OnPointerEnter(PointerEventData eventData) => PlayHover();
    public void OnSelect(BaseEventData eventData) => PlayHover();

    private void PlayHover()
    {
        // 같은 프레임의 포인터/선택 이벤트가 중복 소리를 내지 않게 합니다.
        if (!CanPlay || lastHoverFrame == Time.frameCount) return;
        lastHoverFrame = Time.frameCount;
        if (UIManagerAudio.instance is UISoundController sounds) sounds.PlayHover();
    }

    internal static bool HasHeatSound(GameObject target)
    {
        // 자체적으로 소리를 재생하는 Heat UI 컨트롤과 중복 연결하지 않습니다.
        return target.GetComponent<ButtonManager>() != null
            || target.GetComponent<BoxButtonManager>() != null
            || target.GetComponent<PanelButton>() != null
            || target.GetComponent<ShopButtonManager>() != null
            || target.GetComponent<SettingsElement>() != null
            || target.GetComponent<SwitchManager>() != null
            || target.GetComponent<SliderManager>() != null
            || target.GetComponent<Michsky.UI.Heat.Dropdown>() != null
            || target.GetComponent<ModeSelector>() != null;
    }
}
