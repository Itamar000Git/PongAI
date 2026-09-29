using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// HOW TO PLAY overlay for the main menu. Opens on top of the menu without changing scene;
/// BACK, ESC or a click on the dimmed backdrop closes it. While open, the menu behind it cannot be used.
/// </summary>
public class HowToPlayPanel : MonoBehaviour
{
    [SerializeField] private GameObject overlay;
    [SerializeField] private Button closeButton;
    [Tooltip("The menu content behind the overlay; made non-interactive while the overlay is open.")]
    [SerializeField] private CanvasGroup menuGroup;
    [Tooltip("Button that regains focus when the overlay closes.")]
    [SerializeField] private Selectable returnFocus;

    public bool IsOpen => overlay != null && overlay.activeSelf;

    private void Awake()
    {
        if (overlay != null) overlay.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    public void Open()
    {
        if (IsOpen) return;
        overlay.SetActive(true);
        if (menuGroup != null) menuGroup.interactable = false;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
    }

    public void Close()
    {
        if (!IsOpen) return;
        overlay.SetActive(false);
        if (menuGroup != null) menuGroup.interactable = true;
        if (EventSystem.current != null && returnFocus != null)
            EventSystem.current.SetSelectedGameObject(returnFocus.gameObject);
    }

    private void Update()
    {
        if (!IsOpen) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
    }
}
