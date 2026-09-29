using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small AGAINST LEARNING AI status tag on the Game HUD. Hidden completely in Classic Mode.
/// The status line comes from real learning data only (no timers):
///   ANALYZING...         fewer returns than the tracker needs for a pattern
///   ADAPTING...          enough returns, but fewer than learningMinUpdates Q updates
///   LEARNING...          meaningful Q-learning activity
///   READING YOUR GAME    clear High/Low pattern + learned AND trusted (evidence-backed) preferences in the states of that pattern
/// Never shows the direction, numbers, Q-values or actions. A status change gets a small punch and
/// a brief accent flash on the tag.
/// </summary>
public class LearningModeHUD : MonoBehaviour
{
    public enum Phase { Analyzing, Adapting, Learning, ReadingYourGame }

    [SerializeField] private GameManager gameManager;
    [Tooltip("The tag's root object (shown only in Learning mode).")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private PlayerStyleTracker styleTracker;
    [SerializeField] private PongUITheme theme;
    [SerializeField] private OnlineQLearning qLearning;

    [Header("Phase Rules")]
    [Tooltip("Completed Q updates this match before ADAPTING becomes LEARNING.")]
    [SerializeField, Min(0)] private int learningMinUpdates = 12;
    [Tooltip("Completed Q updates this match before READING YOUR GAME is possible.")]
    [SerializeField, Min(0)] private int readingMinUpdates = 18;
    [Tooltip("A state counts as a learned preference after this many updates...")]
    [SerializeField, Min(1)] private int preferenceMinStateUpdates = 4;
    [Tooltip("...with its best action ahead of the runner-up by at least this much.")]
    [SerializeField, Min(0f)] private float preferenceMargin = 0.1f;

    [Header("Status Text")]
    [SerializeField] private string analyzingText = "ANALYZING...";
    [SerializeField] private string adaptingText = "ADAPTING...";
    [SerializeField] private string learningText = "LEARNING...";
    [SerializeField] private string readingText = "READING YOUR GAME";

    [Header("Change Feedback")]
    [Tooltip("Tag background that briefly flashes the accent color when the phase changes.")]
    [SerializeField] private Graphic accentGraphic;
    [SerializeField, Min(0.01f)] private float flashSeconds = 0.35f;

    private UITween statusTween;
    private UITween rootTween;
    private bool learning;
    private Phase phase;
    private Color accentBaseColor;
    private float flashLeft;

    /// <summary>The status line currently shown (diagnostics / tests).</summary>
    public string CurrentStatus => statusText != null ? statusText.text : string.Empty;
    public Phase CurrentPhase => phase;

    private void Start()
    {
        learning = gameManager != null && gameManager.IsLearningMode;
        if (root != null) root.SetActive(learning);
        if (!learning) return;

        if (titleText != null) titleText.text = "LEARNING AI";
        if (statusText != null) statusTween = statusText.GetComponent<UITween>();
        if (root != null) rootTween = root.GetComponent<UITween>();
        if (accentGraphic != null) accentBaseColor = accentGraphic.color;

        if (styleTracker != null) styleTracker.StyleUpdated += OnStyleUpdated;
        if (qLearning != null) qLearning.LearningChanged += Refresh;
        Show(EvaluatePhase(), false);
    }

    private void OnDestroy()
    {
        if (styleTracker != null) styleTracker.StyleUpdated -= OnStyleUpdated;
        if (qLearning != null) qLearning.LearningChanged -= Refresh;
    }

    private void Update()
    {
        if (flashLeft <= 0f || accentGraphic == null) return;

        flashLeft -= Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(flashLeft / flashSeconds);
        Color accent = theme != null ? theme.highlight : Color.white;
        accentGraphic.color = Color.Lerp(accentBaseColor, accent, t * 0.6f);
        if (flashLeft <= 0f) accentGraphic.color = accentBaseColor;
    }

    /// <summary>Overrides the status line (hook for later stages).</summary>
    public void SetStatus(string status)
    {
        if (statusText != null) statusText.text = status;
    }

    private void OnStyleUpdated(PlayerStyleSnapshot snapshot)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (learning) Show(EvaluatePhase(), true);
    }

    /// <summary>Phase from actual data: tracker shots/pattern, Q update count and learned preferences.</summary>
    public Phase EvaluatePhase()
    {
        PlayerStyleSnapshot snapshot = styleTracker != null ? styleTracker.Snapshot : PlayerStyleSnapshot.Empty;
        if (snapshot.DominantTendency == PlayerTendency.Unknown) return Phase.Analyzing;

        int updates = qLearning != null && qLearning.isActiveAndEnabled ? qLearning.CompletedUpdates : 0;
        if (updates < learningMinUpdates) return Phase.Adapting;

        if (snapshot.HasDirectionalPattern && updates >= readingMinUpdates)
        {
            // Hysteresis: once reading the game, keep it while the pattern holds (no flicker when a
            // Q-value hovers around the preference margin). Losing the pattern drops back to LEARNING.
            if (phase == Phase.ReadingYourGame) return Phase.ReadingYourGame;

            int tendency = snapshot.DominantTendency == PlayerTendency.Low ? 0 : 2;
            // A learned preference alone is not enough: with the confidence gate on, the AI must also TRUST an
            // offensive action in a state of that pattern (enough evidence, not just a lucky Q-value).
            if (qLearning.EstablishedPreferences(tendency, preferenceMargin, preferenceMinStateUpdates) > 0
                && (!qLearning.ConfidenceGateEnabled || qLearning.TrustedStates(tendency) > 0))
                return Phase.ReadingYourGame;
        }
        return Phase.Learning;
    }

    private void Show(Phase newPhase, bool animate)
    {
        bool changed = newPhase != phase;
        phase = newPhase;
        if (statusText == null) return;

        switch (newPhase)
        {
            case Phase.Adapting:
                statusText.text = adaptingText;
                statusText.color = theme != null ? theme.primary : Color.white;
                break;
            case Phase.Learning:
                statusText.text = learningText;
                statusText.color = theme != null ? theme.secondary : Color.white;
                break;
            case Phase.ReadingYourGame:
                statusText.text = readingText;
                statusText.color = theme != null ? theme.highlight : Color.white;
                break;
            default:
                statusText.text = analyzingText;
                statusText.color = theme != null ? theme.highlight : Color.white;
                break;
        }

        if (!animate || !changed) return;

        // Subtle feedback only: small punch + short accent flash. No popups, no interruption.
        if (statusTween != null) statusTween.Punch(1.25f, 0.3f);
        if (rootTween != null) rootTween.Punch(1.08f, 0.25f);
        if (accentGraphic != null) flashLeft = flashSeconds;
    }
}
