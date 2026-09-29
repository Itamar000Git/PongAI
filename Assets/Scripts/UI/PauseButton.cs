using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// HUD PAUSE button. Calls the same GameManager.PauseGame() as ESC and is only clickable while
/// the match is Playing (disabled during Countdown, Paused and Game Over).
/// </summary>
[RequireComponent(typeof(Button))]
public class PauseButton : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClicked);
    }

    private void OnEnable()
    {
        gameManager.StateChanged += OnStateChanged;
        Refresh();
    }

    private void OnDisable()
    {
        gameManager.StateChanged -= OnStateChanged;
    }

    private void OnClicked()
    {
        gameManager.PauseGame();
    }

    private void OnStateChanged(MatchState state)
    {
        Refresh();
    }

    private void Refresh()
    {
        button.interactable = gameManager.CanPause;

        // Don't leave the HUD button focused (keyboard Submit would re-trigger it after resuming).
        if (!button.interactable && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
