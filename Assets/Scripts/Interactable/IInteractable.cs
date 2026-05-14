using UnityEngine;

public interface IInteractable
{
    // '실행'이 아닌 '요청'을 담당합니다.
    void RequestInteract(GameObject interactor);
}