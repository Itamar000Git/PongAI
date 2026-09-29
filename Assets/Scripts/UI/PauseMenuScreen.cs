using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pause overlay view. GameManager owns the pause state; this reads ESC, shows the
/// PAUSED / SETTINGS panels and routes the buttons.
/// </summary>
public class PauseMenuScreen : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("UI")]
    [SerializeField] private GameObject overlay;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button settingsBackButton;

    [Header("Transitions (unscaled time)")]
    [SerializeField] private UITween overlayTween;
    [SerializeField] private UITween pausePanelTween;
    [SerializeField] private UITween settingsPanelTween;
    [SerializeField] private float showSeconds = 0.18f;
    [SerializeField] private float hideSeconds = 0.15f;

    public bool IsSettingsOpen => settingsPanel.activeSelf;

    private void Awake()
    {
        overlay.SetActive(false);
    }

    private void Start()
    {
        resumeButton.onClick.AddListener(gameManager.ResumeGame);
        restartButton.onClick.AddListener(gameManager.RestartMatch);
        settingsButton.onClick.AddListener(OpenSettings);
        mainMenuButton.onClick.AddListener(GoToMainMenu);
        settingsBackButton.onClick.AddListener(CloseSettings);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            HandleEscape();
    }

    /// <summary>ESC steps back out of Settings first; otherwise it toggles pause (ignored at Game Over).</summary>
    private void HandleEscape()
    {
        if (overlay.activeSelf && IsSettingsOpen)
            CloseSettings();
        else
            gameManager.TogglePause();
    }

    public void Show()
    {
        overlay.SetActive(true);
        pausePanel.SetActive(true);
        settingsPanel.SetActive(false);
        if (overlayTween != null)
        {
            overlayTween.StopFade();
            overlayTween.Group.blocksRaycasts = true;
            overlayTween.FadeIn(showSeconds);
        }
        if (pausePanelTween != null) pausePanelTween.FadeIn(showSeconds, 0.9f);
        Select(resumeButton);
    }

    public void Hide()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
        if (!overlay.activeSelf) return;

        if (overlayTween == null)
        {
            overlay.SetActive(false);
            return;
        }

        // Gameplay resumes immediately; the overlay just fades away without catching clicks.
        overlayTween.Group.blocksRaycasts = false;
        overlayTween.FadeOut(hideSeconds, () => overlay.SetActive(false));
    }

    private void OpenSettings()
    {
        pausePanel.SetActive(false);
        settingsPanel.SetActive(true);
        if (settingsPanelTween != null) settingsPanelTween.FadeIn(showSeconds, 0.92f);
        Select(settingsBackButton);
    }

    private void CloseSettings()
    {
        settingsPanel.SetActive(false);
        pausePanel.SetActive(true);
        if (pausePanelTween != null) pausePanelTween.FadeIn(showSeconds, 0.92f);
        Select(settingsButton);
    }

    private void GoToMainMenu()
    {
        // Stay paused under the fade; the safe PongAgent shutdown runs right before the load.
        SceneTransition.LoadScene(mainMenuSceneName, gameManager.PrepareForSceneExit);
    }

    private static void Select(Button button)
    {
        if (EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
