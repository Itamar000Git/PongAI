using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Button with the PongAI arcade look: themed colors per state, hover pop, pressed "push-down".
/// Colors come from PongUITheme so every button in the game stays consistent.
/// </summary>
public class ArcadeButton : Button
{
    public enum Style { Primary, Secondary }

    [SerializeField] private PongUITheme theme;
    [SerializeField] private Style style = Style.Primary;

    [Header("Parts")]
    [SerializeField] private RectTransform visual;   // scaled on hover
    [SerializeField] private RectTransform body;     // pushed toward the shadow on press
    [SerializeField] private Graphic face;
    [SerializeField] private Graphic shadow;
    [SerializeField] private TMP_Text label;

    [Header("Motion")]
    [SerializeField] private float hoverScale = 1.06f;
    [SerializeField] private float pressedScale = 0.98f;
    [SerializeField] private Vector2 pressOffset = new Vector2(5f, -5f);
    [SerializeField] private float animationSpeed = 16f;

    private float targetScale = 1f;
    private Vector2 targetOffset;

    /// <summary>Switches between the primary and secondary look (e.g. for ON/OFF toggles).</summary>
    public void SetStyle(Style newStyle)
    {
        if (style == newStyle) return;
        style = newStyle;
        DoStateTransition(currentSelectionState, true);
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        // Selection follows the mouse so only one button is ever highlighted (mouse + keyboard/gamepad).
        if (IsInteractable() && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (theme == null) return;

        Color faceColor;
        Color labelColor = theme.text;
        Color shadowColor = theme.outline;
        targetScale = 1f;
        targetOffset = Vector2.zero;

        switch (state)
        {
            case SelectionState.Highlighted:
            case SelectionState.Selected:
                faceColor = theme.secondary;
                labelColor = theme.highlight;
                targetScale = hoverScale;
                break;
            case SelectionState.Pressed:
                faceColor = theme.secondaryDark;
                labelColor = theme.highlight;
                targetScale = pressedScale;
                targetOffset = pressOffset;
                break;
            case SelectionState.Disabled:
                faceColor = theme.disabled;
                labelColor = theme.disabledText;
                shadowColor = new Color(theme.outline.r, theme.outline.g, theme.outline.b, 0.6f);
                break;
            default:
                faceColor = style == Style.Primary ? theme.primary : theme.primaryDark;
                break;
        }

        if (face != null) face.color = faceColor;
        if (shadow != null) shadow.color = shadowColor;
        if (label != null) label.color = labelColor;

        if (instant || !Application.isPlaying) SnapMotion();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        float t = 1f - Mathf.Exp(-animationSpeed * Time.unscaledDeltaTime);
        if (visual != null)
            visual.localScale = Vector3.one * Mathf.Lerp(visual.localScale.x, targetScale, t);
        if (body != null)
            body.anchoredPosition = Vector2.Lerp(body.anchoredPosition, targetOffset, t);
    }

    private void SnapMotion()
    {
        if (visual != null) visual.localScale = Vector3.one * targetScale;
        if (body != null) body.anchoredPosition = targetOffset;
    }
}
