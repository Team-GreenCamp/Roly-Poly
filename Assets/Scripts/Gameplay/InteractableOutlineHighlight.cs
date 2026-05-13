using UnityEngine;

[DisallowMultipleComponent]
public class InteractableOutlineHighlight : MonoBehaviour
{
    [SerializeField] private Outline outline;
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private float highlightWidth = 4f;
    [SerializeField] private Outline.Mode highlightMode = Outline.Mode.OutlineVisible;

    private bool wasOutlineEnabled;
    private bool isHighlighted;

    private void Awake()
    {
        EnsureOutline();
        wasOutlineEnabled = outline != null && outline.enabled;
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
            ApplyHighlightStyle();
            outline.enabled = true;
        }
        else
        {
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
}
