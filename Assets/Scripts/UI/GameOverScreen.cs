using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Game Over overlay view. Holds no match state: GameManager decides when to show it,
/// owns the restart and the score submission; this only displays results and routes the buttons.
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string leaderboardSceneName = "Leaderboard";
    [SerializeField] private PongUITheme theme;

    [Header("UI")]
    [SerializeField] private GameObject overlay;
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private TMP_Text finalScoreText;
    [FormerlySerializedAs("infoText")]
    [SerializeField] private TMP_Text saveStatusText;
    [SerializeField] private Button retrySaveButton;
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button leaderboardButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Polish")]
    [SerializeField] private UITween panelTween;
    [SerializeField] private UITween finalScoreTween;
    [SerializeField] private UITween saveStatusTween;

    private void Awake()
    {
        overlay.SetActive(false);
    }

    private void Start()
    {
        playAgainButton.onClick.AddListener(gameManager.RestartMatch);
        leaderboardButton.onClick.AddListener(OpenLeaderboard);
        mainMenuButton.onClick.AddListener(GoToMainMenu);
        if (retrySaveButton != null) retrySaveButton.onClick.AddListener(gameManager.RetryScoreSubmission);
    }

    public void Show(string nickname, int finalScore)
    {
        nicknameText.text = nickname;
        finalScoreText.text = finalScore.ToString();
        SetSaveStatus(string.Empty, Color.white, false);
        overlay.SetActive(true);

        // Short intro; buttons stay clickable the whole time.
        if (panelTween != null) panelTween.FadeIn(0.25f, 0.85f);
        if (finalScoreTween != null) finalScoreTween.Punch(1.7f, 0.45f);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(playAgainButton.gameObject);
    }

    public void Hide()
    {
        SetSaveStatus(string.Empty, Color.white, false);
        overlay.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowScoreSaving()
    {
        SetSaveStatus("SAVING SCORE...", theme != null ? theme.disabledText : Color.gray, false);
    }

    public void ShowScoreResult(SubmitScoreResult result)
    {
        if (!result.Success)
        {
            SetSaveStatus("SCORE NOT SAVED", theme != null ? theme.secondary : Color.red, true);
            return;
        }

        switch (result.Outcome)
        {
            case ScoreSubmitOutcome.Updated:
                SetSaveStatus("NEW HIGH SCORE!", theme != null ? theme.highlight : Color.yellow, false);
                // Celebrate: bigger punch plus three pulses, then it rests.
                if (saveStatusTween != null)
                {
                    saveStatusTween.Punch(1.8f, 0.4f);
                    saveStatusTween.SetPulse(true, 0.12f, 1.6f, 3);
                }
                break;
            case ScoreSubmitOutcome.NotImproved:
                SetSaveStatus($"BEST SCORE: {result.HighScore}", theme != null ? theme.text : Color.white, false);
                break;
            default:
                SetSaveStatus("SCORE SAVED!", theme != null ? theme.primary : Color.cyan, false);
                break;
        }
    }

    private void SetSaveStatus(string text, Color color, bool showRetry)
    {
        if (saveStatusTween != null) saveStatusTween.SetPulse(false);
        saveStatusText.text = text;
        saveStatusText.color = color;
        if (retrySaveButton != null) retrySaveButton.gameObject.SetActive(showRetry);
    }

    private void OpenLeaderboard()
    {
        // Open on the leaderboard of the mode that was just played.
        GameSession.RequestLeaderboard(LeaderboardTypes.ForMode(gameManager != null ? gameManager.Mode : GameMode.Classic));
        SceneTransition.LoadScene(leaderboardSceneName, gameManager.PrepareForSceneExit);
    }

    private void GoToMainMenu()
    {
        SceneTransition.LoadScene(mainMenuSceneName, gameManager.PrepareForSceneExit);
    }
}
