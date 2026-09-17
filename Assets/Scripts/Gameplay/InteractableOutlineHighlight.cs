using UnityEngine;

[DisallowMultipleComponent]
public class InteractableOutlineHighlight : MonoBehaviour
{
    [SerializeField] private Outline outline;
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private float highlightWidth = 4f;
    [SerializeField] private Outline.Mode highlightMode = Outline.Mode.OutlineVisible;

    private bool wasOutlineEnabled;
    private Outline.Mode originalMode;
    private Color originalColor;
    private float originalWidth;
    private bool isHighlighted;

    private void Awake()
    {
        EnsureOutline();
        CacheOriginalOutlineState();
        SetHighlighted(false);
    }

    public void Configure(Color color, float width, Outline.Mode mode)
    {
        highlightColor = color;
        highlightWidth = Mathf.Max(0f, width);
        highlightMode = mode;

        if (isHighlighted)
        {
            ApplyHighlightStyle();
        }
    }

    public void SetHighlighted(bool shouldHighlight)
    {
        EnsureOutline();
        if (outline == null)
        {
            return;
        }

        isHighlighted = shouldHighlight;
        if (shouldHighlight)
        {
            CacheOriginalOutlineState();
            ApplyHighlightStyle();
            outline.enabled = true;
        }
        else
        {
            RestoreOriginalOutlineStyle();
            outline.enabled = wasOutlineEnabled;
        }
    }

    private void ApplyHighlightStyle()
    {
        outline.OutlineMode = highlightMode;
        outline.OutlineColor = highlightColor;
        outline.OutlineWidth = highlightWidth;
    }

    private void EnsureOutline()
    {
        if (outline != null)
        {
            return;
        }

        outline = GetComponent<Outline>();
        if (outline == null)
        {
            outline = gameObject.AddComponent<Outline>();
            outline.enabled = false;
        }
    }

    private void CacheOriginalOutlineState()
    {
        if (outline == null)
        {
            return;
        }

        // 하이라이트 해제 시 기존 Outline 설정으로 되돌리기 위해 원래 상태를 저장합니다.
        wasOutlineEnabled = outline.enabled;
        originalMode = outline.OutlineMode;
        originalColor = outline.OutlineColor;
        originalWidth = outline.OutlineWidth;
    }

    private void RestoreOriginalOutlineStyle()
    {
        if (outline == null)
        {
            return;
        }

        outline.OutlineMode = originalMode;
        outline.OutlineColor = originalColor;
        outline.OutlineWidth = originalWidth;
    }
}
