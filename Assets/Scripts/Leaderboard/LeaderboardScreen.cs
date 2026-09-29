using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Global Top 10 screen: loading, table, empty and error states. All data comes from
/// LeaderboardService; this script only presents it.
/// Shows one leaderboard type at a time (Classic by default); SetLeaderboardType switches and reloads.
/// </summary>
public class LeaderboardScreen : MonoBehaviour
{
    [SerializeField] private string backSceneName = "MainMenu";
    [Tooltip("Which table this screen shows. Classic = leaderboard, Learning = learning_leaderboard.")]
    [SerializeField] private LeaderboardType leaderboardType = LeaderboardType.Classic;

    [Header("UI")]
    [SerializeField] private LeaderboardRowView[] rows;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button backButton;
    [SerializeField] private PongUITheme theme;
    [Tooltip("Optional. When set, shows CLASSIC TOP 10 / LEARNING AI TOP 10.")]
    [SerializeField] private TMP_Text titleText;

    [Header("Tabs")]
    [SerializeField] private ArcadeButton classicTab;
    [SerializeField] private ArcadeButton learningTab;
    [Tooltip("Small underline shown under the active tab.")]
    [SerializeField] private GameObject classicTabMarker;
    [SerializeField] private GameObject learningTabMarker;

    private bool isLoading;
    private int loadSerial;

    public LeaderboardType LeaderboardType => leaderboardType;

    private void Start()
    {
        retryButton.onClick.AddListener(Load);
        backButton.onClick.AddListener(Back);
        if (classicTab != null) classicTab.onClick.AddListener(() => SetLeaderboardType(LeaderboardType.Classic));
        if (learningTab != null) learningTab.onClick.AddListener(() => SetLeaderboardType(LeaderboardType.Learning));

        // Main menu -> CLASSIC, Game Over -> the mode that was just played.
        leaderboardType = GameSession.TakeRequestedLeaderboard();
        ApplyTitle();
        Load();
    }

    /// <summary>Switches the screen to another leaderboard (e.g. from future CLASSIC / LEARNING AI tabs) and reloads.</summary>
    public void SetLeaderboardType(LeaderboardType type)
    {
        if (type == leaderboardType) return;

        leaderboardType = type;
        ApplyTitle();
        isLoading = false;   // a request for the previous type is ignored when it returns
        Load();
    }

    private void ApplyTitle()
    {
        if (titleText != null) titleText.text = LeaderboardTypes.Title(leaderboardType);

        // Active tab = bright cyan (Primary) + underline; the other tab = dim teal (Secondary).
        // (Orange stays reserved for hover / keyboard focus, like every other ArcadeButton.)
        bool classic = leaderboardType == LeaderboardType.Classic;
        if (classicTab != null) classicTab.SetStyle(classic ? ArcadeButton.Style.Primary : ArcadeButton.Style.Secondary);
        if (learningTab != null) learningTab.SetStyle(classic ? ArcadeButton.Style.Secondary : ArcadeButton.Style.Primary);
        if (classicTabMarker != null) classicTabMarker.SetActive(classic);
        if (learningTabMarker != null) learningTabMarker.SetActive(!classic);
    }

    private async void Load()
    {
        if (isLoading) return;
        isLoading = true;
        int serial = ++loadSerial;

        ShowRows(null);
        SetStatus("LOADING SCORES...", string.Empty, theme != null ? theme.primary : Color.white);
        retryButton.gameObject.SetActive(false);
        Select(backButton);

        TopScoresResult result = await LeaderboardService.CreateDefault(leaderboardType).GetTop10Async();

        // The player may have pressed BACK (or switched leaderboard) while the request was running.
        if (this == null || serial != loadSerial) return;
        isLoading = false;

        if (!result.Success)
        {
            SetStatus("COULDN'T LOAD LEADERBOARD", FriendlyReason(result.Error), theme != null ? theme.secondary : Color.white);
            retryButton.gameObject.SetActive(true);
            Select(retryButton);
            return;
        }

        if (result.Entries.Count == 0)
        {
            SetStatus("NO SCORES YET", "BE THE FIRST TO BEAT THE MACHINE!", theme != null ? theme.highlight : Color.white);
            return;
        }

        SetStatus(string.Empty, string.Empty, Color.white);
        ShowRows(result);
    }

    private void ShowRows(TopScoresResult result)
    {
        // Only highlight a nickname the player actually entered this session (not the "PLAYER" fallback).
        string currentNickname = GameSession.Instance != null ? GameSession.Instance.CurrentNickname : null;

        for (int i = 0; i < rows.Length; i++)
        {
            if (result != null && i < result.Entries.Count)
            {
                LeaderboardEntry entry = result.Entries[i];
                bool isCurrentPlayer = !string.IsNullOrEmpty(currentNickname) && string.Equals(entry.nickname, currentNickname, System.StringComparison.OrdinalIgnoreCase);
                rows[i].Show(i + 1, entry, isCurrentPlayer);
            }
            else
            {
                rows[i].Hide();
            }
        }
    }

    private void SetStatus(string status, string detail, Color color)
    {
        statusText.text = status;
        statusText.color = color;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(status));
        detailText.text = detail;
        detailText.gameObject.SetActive(!string.IsNullOrEmpty(detail));
    }

    private static string FriendlyReason(LeaderboardError error)
    {
        switch (error)
        {
            case LeaderboardError.NoConnection: return "CHECK YOUR INTERNET CONNECTION";
            case LeaderboardError.Timeout: return "THE SERVER TOOK TOO LONG TO RESPOND";
            case LeaderboardError.ServerUnavailable: return "THE LEADERBOARD SERVER IS UNAVAILABLE";
            case LeaderboardError.NotConfigured: return "LEADERBOARD IS NOT CONFIGURED";
            default: return "PLEASE TRY AGAIN";
        }
    }

    private static void Select(Button button)
    {
        if (EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private void Back()
    {
        SceneTransition.LoadScene(backSceneName);
    }
}
