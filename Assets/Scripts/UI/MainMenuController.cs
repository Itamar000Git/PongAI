using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Main menu wiring: PLAY CLASSIC and AGAINST LEARNING AI store the chosen GameMode and open the
/// (shared) nickname screen, LEADERBOARD the global Top 10, QUIT quits.
/// HOW TO PLAY opens an overlay on this screen.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private string nicknameSceneName = "NicknameEntry";
    [SerializeField] private string leaderboardSceneName = "Leaderboard";

    [Header("Buttons")]
    [SerializeField] private Button playClassicButton;
    [SerializeField] private Button learningAIButton;
    [SerializeField] private Button leaderboardButton;
    [SerializeField] private Button howToPlayButton;
    [SerializeField] private Button quitButton;

    [SerializeField] private HowToPlayPanel howToPlayPanel;

    private bool isLeaving;

    private void Start()
    {
        playClassicButton.onClick.AddListener(PlayClassic);
        learningAIButton.onClick.AddListener(PlayLearning);
        leaderboardButton.onClick.AddListener(OpenLeaderboard);
        howToPlayButton.onClick.AddListener(OpenHowToPlay);
        quitButton.onClick.AddListener(Quit);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(playClassicButton.gameObject);
    }

    private void PlayClassic()
    {
        StartMode(GameMode.Classic);
    }

    private void PlayLearning()
    {
        StartMode(GameMode.Learning);
    }

    /// <summary>Both modes share the nickname screen and the Game scene; only the stored mode differs.</summary>
    private void StartMode(GameMode mode)
    {
        if (isLeaving) return;

        if (!Application.CanStreamedLevelBeLoaded(nicknameSceneName))
        {
            Debug.Log($"[MainMenu] Scene '{nicknameSceneName}' is not in Build Settings.");
            return;
        }

        isLeaving = true;
        GameSession.GetOrCreate().SetGameMode(mode);
        SceneTransition.LoadScene(nicknameSceneName);
    }

    private void OpenLeaderboard()
    {
        GameSession.RequestLeaderboard(LeaderboardType.Classic);
        SceneTransition.LoadScene(leaderboardSceneName);
    }

    private void OpenHowToPlay()
    {
        if (howToPlayPanel != null) howToPlayPanel.Open();
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
