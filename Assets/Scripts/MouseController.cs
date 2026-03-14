using UnityEngine;

public class MouseController : MonoBehaviour
{
    [SerializeField] private bool lockCursorOnStart;

    private void Start()
    {
        if (lockCursorOnStart)
        {
            SetCursorLock(true);
        }
    }

    public void ApplyCursorState(bool shouldLock)
    {
        SetCursorLock(shouldLock);
    }

    public static void SetCursorLock(bool shouldLock)
    {
        Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLock;
    }
}
