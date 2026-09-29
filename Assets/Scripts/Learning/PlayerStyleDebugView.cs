using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// DEVELOPMENT ONLY: raw PlayerStyleTracker numbers (shots, ratios, averages, pattern) plus the
/// Learning AI's style bias, confidence, predicted Y and adaptive target Y.
/// Off by default (showDebug). Never shown in Classic Mode. Redraws when the tracker reports a change
/// and a few times per second while visible (no work at all when hidden, no Console output).
/// </summary>
public class PlayerStyleDebugView : MonoBehaviour
{
    [Tooltip("Development only. Leave off for normal play.")]
    [SerializeField] private bool showDebug;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PlayerStyleTracker styleTracker;
    [SerializeField] private AdaptivePaddleController learningOpponent;
    [SerializeField] private OnlineQLearning qLearning;
    [Tooltip("Seconds between redraws of the live AI values while visible (unscaled time).")]
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;
    [Tooltip("Panel root (inactive unless showDebug is on in Learning mode).")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text text;

    private readonly StringBuilder builder = new StringBuilder(256);
    private bool subscribed;
    private bool visible;
    private float nextRefresh;

    public bool ShowDebug => showDebug;

    private void Start()
    {
        // The evidence lines make the panel taller than its box: shrink the text to fit (debug only).
        if (text != null && !text.enableAutoSizing)
        {
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = 12f;
            text.enableAutoSizing = true;
        }
        Apply();
    }

    private void Update()
    {
        if (!visible || Time.unscaledTime < nextRefresh) return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        Render(styleTracker.Snapshot);
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    /// <summary>Turns the debug panel on or off at runtime.</summary>
    public void SetShowDebug(bool show)
    {
        showDebug = show;
        Apply();
    }

    private void Apply()
    {
        visible = showDebug && styleTracker != null && gameManager != null && gameManager.IsLearningMode;
        if (panel != null) panel.SetActive(visible);

        if (visible)
        {
            if (!subscribed)
            {
                styleTracker.StyleUpdated += Render;
                subscribed = true;
            }
            Render(styleTracker.Snapshot);
        }
        else
        {
            Unsubscribe();
        }
    }

    private void Unsubscribe()
    {
        if (subscribed && styleTracker != null) styleTracker.StyleUpdated -= Render;
        subscribed = false;
    }

    private void Render(PlayerStyleSnapshot s)
    {
        if (text == null) return;

        builder.Clear();
        builder.Append("PLAYER STYLE (DEBUG)\n");
        builder.Append("Shots: ").Append(s.RecentShotCount).Append('/').Append(styleTracker.HistorySize)
               .Append("  (match ").Append(s.TotalShotsObserved).Append(")\n");
        builder.Append("High: ").Append(Percent(s.HighRatio))
               .Append("  Center: ").Append(Percent(s.CenterRatio))
               .Append("  Low: ").Append(Percent(s.LowRatio)).Append('\n');
        builder.Append("Flat: ").Append(Percent(s.FlatRatio))
               .Append("  Mod: ").Append(Percent(s.ModerateRatio))
               .Append("  Steep: ").Append(Percent(s.SteepRatio)).Append('\n');
        builder.Append("Avg Hit: ").Append(s.AverageHitPosition.ToString("+0.00;-0.00;0.00"))
               .Append("  Avg Dir: ").Append(s.AverageOutgoingVerticalDirection.ToString("+0.00;-0.00;0.00")).Append('\n');
        builder.Append("Pattern: ").Append(s.DominantTendency.ToString().ToUpperInvariant());

        if (styleTracker.TryGetLatestShot(out PlayerShot last))
        {
            builder.Append("\nLast #").Append(last.ShotNumber).Append(": ")
                   .Append(last.Direction.ToString().ToUpperInvariant()).Append(' ')
                   .Append(last.Steepness.ToString().ToLowerInvariant())
                   .Append("  hit ").Append(last.HitPosition.ToString("+0.00;-0.00;0.00"));
        }

        if (learningOpponent != null)
        {
            builder.Append("\nStyle Bias: ").Append(learningOpponent.CurrentStyleBias.ToString("+0.00;-0.00;0.00"))
                   .Append(" (to ").Append(learningOpponent.DesiredStyleBias.ToString("+0.00;-0.00;0.00")).Append(')')
                   .Append("  Confidence: ").Append(Percent(learningOpponent.CurrentStyleConfidence));
            // No prediction while the ball moves away: the AI then waits at its idle spot + bias.
            builder.Append("\nPredicted Y: ").Append(learningOpponent.HasPrediction ? learningOpponent.PredictedInterceptY.ToString("0.00") : "--")
                   .Append("  Adaptive Target Y: ").Append(learningOpponent.AdaptiveTargetY.ToString("0.00"));
        }

        if (qLearning != null)
        {
            int state = qLearning.CurrentStateIndex;
            builder.Append("\nQ State: ").Append(OnlineQLearning.StateLabel(state));
            builder.Append("\nReturn Action: ").Append(OnlineQLearning.ActionName(qLearning.CurrentAction))
                   .Append(qLearning.LastDecisionExplored ? " (explore)" : string.Empty);
            if (learningOpponent != null)
            {
                builder.Append("\nDesired Contact: ").Append(learningOpponent.CurrentDesiredContact.ToString("+0.00;-0.00;0.00"))
                       .Append(learningOpponent.AimDroppedForSurvival ? " (dropped: survival)" : string.Empty);
                builder.Append("\nActual Contact: ").Append(learningOpponent.ReturnsObserved > 0 ? learningOpponent.LastContactHit.ToString("+0.00;-0.00;0.00") : "--")
                       .Append("  AI Shot: ").Append(learningOpponent.ReturnsObserved > 0 ? learningOpponent.LastReturnDirection.ToString().ToUpperInvariant() + " " + learningOpponent.LastReturnAngle.ToString("+0;-0;0") + "deg" : "--");
            }
            builder.Append("\nQ Low: ").Append(qLearning.GetQ(state, QAction.ReturnLow).ToString("0.00"))
                   .Append("  Center: ").Append(qLearning.GetQ(state, QAction.ReturnCenter).ToString("0.00"))
                   .Append("  High: ").Append(qLearning.GetQ(state, QAction.ReturnHigh).ToString("0.00"));
            // Evidence / confidence gate (debug only).
            QConfidenceInfo info = qLearning.EvaluateConfidence(state);
            int trusted = qLearning.CurrentTrustedAction;
            builder.Append("\nQ Confidence: ").Append(info.Level.ToString().ToUpperInvariant())
                   .Append(qLearning.ConfidenceGateEnabled ? string.Empty : " (gate off)")
                   .Append("  Trusted: ").Append(trusted < 0 ? "NONE" : OnlineQLearning.ActionName((QAction)trusted))
                   .Append(qLearning.LastDecisionUntrusted ? " (varied)" : string.Empty);
            builder.Append("\nSamples L/C/H: ").Append(qLearning.AttemptsOf(state, QAction.ReturnLow)).Append('/')
                   .Append(qLearning.AttemptsOf(state, QAction.ReturnCenter)).Append('/').Append(qLearning.AttemptsOf(state, QAction.ReturnHigh));
            builder.Append("\nPlayer Fail L/C/H: ").Append(Percent(qLearning.PlayerFailRate(state, QAction.ReturnLow))).Append('/')
                   .Append(Percent(qLearning.PlayerFailRate(state, QAction.ReturnCenter))).Append('/').Append(Percent(qLearning.PlayerFailRate(state, QAction.ReturnHigh)));
            builder.Append("\nGaps (").Append(OnlineQLearning.ActionName(info.Action)).Append("): fail ").Append(info.PerformanceGap.ToString("+0.00;-0.00;0.00"))
                   .Append("  Q ").Append(info.QGap.ToString("+0.00;-0.00;0.00"));
            builder.Append("\nEpsilon: ").Append(Percent(qLearning.CurrentEpsilon))
                   .Append("  Decisions: ").Append(qLearning.Decisions)
                   .Append("  Updates: ").Append(qLearning.CompletedUpdates);
        }

        text.text = builder.ToString();
    }

    private static string Percent(float ratio)
    {
        return Mathf.RoundToInt(ratio * 100f) + "%";
    }
}
