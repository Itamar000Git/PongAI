using System;
using UnityEngine;

/// <summary>
/// The three Q-learning actions: where the AI tries to send its return (offense).
/// They choose the contact point on the AI paddle; the normal paddle bounce then produces the angle.
/// </summary>
public enum QAction
{
    ReturnLow = 0,      // ball meets the paddle's lower half -> leaves downward, toward the player's lower side
    ReturnCenter = 1,   // center contact -> relatively flat return
    ReturnHigh = 2      // ball meets the paddle's upper half -> leaves upward, toward the player's upper side
}

/// <summary>How the greedy part of action selection treats ties and untried actions.</summary>
public enum QTieBreak
{
    /// <summary>Ties go to RETURN_CENTER (the baseline), then random among the tied.</summary>
    CenterPreferred = 0,
    /// <summary>Ties go to a random one of the tied best actions.</summary>
    RandomTies = 1,
    /// <summary>Every action is tried a few times per state before greedy selection is allowed (ties as CenterPreferred).</summary>
    BalancedEarlySampling = 2
}

/// <summary>How far the evidence for a state's best action has come (internal; not shown on the normal HUD).</summary>
public enum QConfidence
{
    Unproven = 0,   // not enough completed samples yet
    Promising = 1,  // enough samples, but no meaningful advantage over the alternatives yet
    Trusted = 2     // enough samples + meaningful player-failure gap + Q-value separation
}

/// <summary>Read-only result of the confidence check for one state.</summary>
public readonly struct QConfidenceInfo
{
    public readonly QConfidence Level;
    /// <summary>The candidate: the state's best-Q action.</summary>
    public readonly QAction Action;
    /// <summary>Candidate's player-failure rate minus the best qualified alternative's.</summary>
    public readonly float PerformanceGap;
    /// <summary>Candidate's Q-value minus the best other Q-value.</summary>
    public readonly float QGap;

    public QConfidenceInfo(QConfidence level, QAction action, float performanceGap, float qGap)
    {
        Level = level; Action = action; PerformanceGap = performanceGap; QGap = qGap;
    }

    public bool IsTrusted => Level == QConfidence.Trusted;
}

/// <summary>
/// AGAINST LEARNING AI: small online tabular Q-learning layer for RETURN STRATEGY (enabled by
/// PongModeController only in Learning mode, driven by AdaptivePaddleController).
///
///   27 states  = intercept zone (LOW/MID/HIGH) x paddle vs defensive target (BELOW/ALIGNED/ABOVE)
///                x player tendency (LOW/NEUTRAL/HIGH)
///   3 actions  = RETURN_LOW / RETURN_CENTER / RETURN_HIGH (desired contact point for one incoming shot)
///   rewards    = AI returns the ball +returnReward, the player then misses that return +1, AI misses -1
///
/// Credit assignment: one decision per incoming shot. The decision stays pending after the AI's return
/// until the player either sends the ball back (non-terminal update when the next decision is made)
/// or fails to (terminal update: the scoring reward goes to the action that produced that return).
/// Everything is cleared at the start of every Learning match. Nothing is ever saved.
/// </summary>
public class OnlineQLearning : MonoBehaviour
{
    public const int ZoneCount = 3, RelationCount = 3, TendencyCount = 3;
    public const int StateCount = ZoneCount * RelationCount * TendencyCount;   // 27
    public const int ActionCount = 3;

    private static readonly string[] ZoneNames = { "LOW", "MID", "HIGH" };
    private static readonly string[] RelationNames = { "BELOW", "ALIGNED", "ABOVE" };
    private static readonly string[] TendencyNames = { "LOW", "NEUTRAL", "HIGH" };

    [Header("State Thresholds")]
    [Tooltip("Predicted intercept below -this is the LOW zone, above +this the HIGH zone, otherwise MID.")]
    [SerializeField, Min(0f)] private float zoneBoundary = 1.2f;
    [Tooltip("Paddle within ± this of the defensive target is ALIGNED; further below/above is BELOW/ABOVE.")]
    [SerializeField, Min(0f)] private float alignedTolerance = 0.5f;

    [Header("Return Actions")]
    [Tooltip("Desired contact point of RETURN_LOW / RETURN_HIGH, as a fraction of the paddle half-height " +
             "(0 = center, 1 = edge). Kept well inside the edge: aim error still scatters it.")]
    [SerializeField, Range(0f, 0.8f)] private float returnContactOffset = 0.45f;

    [Header("Learning")]
    [SerializeField, Range(0f, 1f)] private float learningRate = 0.25f;
    [SerializeField, Range(0f, 1f)] private float discount = 0.9f;

    [Header("Exploration (per match)")]
    [SerializeField, Range(0f, 1f)] private float startEpsilon = 0.25f;
    [SerializeField, Range(0f, 1f)] private float minEpsilon = 0.05f;
    [Tooltip("Epsilon is multiplied by this after every completed update.")]
    [SerializeField, Range(0.5f, 1f)] private float epsilonDecay = 0.98f;

    [Header("Action Selection (ties / early sampling)")]
    [Tooltip("Production: BalancedEarlySampling (Experiment B). CenterPreferred / RandomTies are kept for comparison.")]
    [SerializeField] private QTieBreak tieBreak = QTieBreak.BalancedEarlySampling;
    [Tooltip("BalancedEarlySampling: how often each action is tried in a state before greedy selection is allowed there.")]
    [SerializeField, Range(1, 5)] private int minSamplesPerAction = 2;

    [Header("Confidence Gate (evidence before exploiting)")]
    [Tooltip("Learning always continues. The gate only decides whether the best Q action may be exploited: without a TRUSTED action the choice stays varied. OFF in production: tested (4 gate configs, 64 runs) it did not meaningfully reduce false dominance versus balanced sampling alone and cost exploit strength. The defaults below are the most conservative tested configuration (Gate C).")]
    [SerializeField] private bool useConfidenceGate = false;
    [Tooltip("Completed samples the candidate (best-Q) action needs before it can be trusted.")]
    [SerializeField, Min(1)] private int minSamplesBest = 5;
    [Tooltip("Completed samples at least one alternative action needs before the candidate can be trusted.")]
    [SerializeField, Min(1)] private int minSamplesAlternative = 4;
    [Tooltip("The candidate's player-failure rate must exceed every qualified alternative's by this much.")]
    [SerializeField, Range(0f, 1f)] private float performanceGap = 0.20f;
    [Tooltip("The candidate's Q-value must exceed every alternative's by this much.")]
    [SerializeField, Range(0f, 2f)] private float qSeparation = 0.15f;
    [Tooltip("Without a trusted action: chance of taking the greedy action (random tie-break); otherwise a uniformly random action.")]
    [SerializeField, Range(0f, 1f)] private float untrustedExploitChance = 0.4f;

    [Header("Rewards")]
    [SerializeField] private float returnReward = 0.2f;
    [SerializeField] private float scoreReward = 1f;
    [SerializeField] private float missReward = -1f;

    private readonly float[] q = new float[StateCount * ActionCount];
    private readonly int[] actionCounts = new int[ActionCount];
    private readonly int[] stateUpdates = new int[StateCount];
    private readonly int[] evAttempts = new int[StateCount * ActionCount];   // completed offensive attempts per (state, action)
    private readonly int[] evReturns = new int[StateCount * ActionCount];    // ...in which the AI returned the ball
    private readonly int[] evPlayerFails = new int[StateCount * ActionCount]; // ...in which the player then failed (AI scored)
    private readonly int[] evAiMisses = new int[StateCount * ActionCount];   // ...in which the AI missed
    private readonly int[] lastTrusted = new int[StateCount];                // trusted action last seen per state (-1 none)
    private int trustChanges;
    private int firstTrustedDecision;
    private bool pendingDropped;
    private readonly int[] samples = new int[StateCount * ActionCount];   // decisions per (state, action) that were actually carried out

    // Outcome statistics per action (this match): how often the AI returned with it, how often the
    // player then failed to return that ball (AI scored), and how often the AI missed with it.
    private readonly int[] returnsBy = new int[ActionCount];
    private readonly int[] scoresBy = new int[ActionCount];
    private readonly int[] missesBy = new int[ActionCount];

    private float epsilon;
    private int decisions;
    private int updates;
    private int exploratoryDecisions;

    // The decision currently waiting for its outcome.
    private bool hasPending;
    private int pendingState;
    private QAction pendingAction;
    private float pendingReward;
    private bool pendingReturned;

    /// <summary>Raised after every decision, update and reset (HUD / debug refresh; no per-frame work).</summary>
    public event Action LearningChanged;

    public int CurrentStateIndex { get; private set; } = -1;
    public QAction CurrentAction { get; private set; } = QAction.ReturnCenter;
    /// <summary>Desired contact point of the current action (-offset / 0 / +offset, paddle half-heights).</summary>
    public float CurrentDesiredContact => DesiredContactFor(CurrentAction);
    public float ReturnContactOffset => returnContactOffset;
    public float CurrentEpsilon => epsilon;
    public int Decisions => decisions;
    public int CompletedUpdates => updates;
    public int ExploratoryDecisions => exploratoryDecisions;
    /// <summary>True if the current action was chosen at random (exploration).</summary>
    public bool LastDecisionExplored { get; private set; }
    /// <summary>True if the current action was forced by balanced early sampling (an untried action of this state).</summary>
    public bool LastDecisionSampled { get; private set; }
    public QTieBreak TieBreakMode => tieBreak;
    public bool ConfidenceGateEnabled => useConfidenceGate;
    /// <summary>Confidence level of the state the last decision was made in (evaluated before that decision).</summary>
    public QConfidence LastDecisionConfidence { get; private set; }
    /// <summary>The trusted action of that state at that moment, or -1 if none.</summary>
    public int LastDecisionTrustedAction { get; private set; } = -1;
    /// <summary>True if the last decision came from the gate's varied (no trusted action) path.</summary>
    public bool LastDecisionUntrusted { get; private set; }
    /// <summary>Times a state's trusted action appeared, changed or was lost (match total).</summary>
    public int TrustChanges => trustChanges;
    /// <summary>Decision number at which any state first became trusted (0 = not yet).</summary>
    public int FirstTrustedDecision => firstTrustedDecision;
    public QConfidence CurrentConfidence => EvaluateConfidence(CurrentStateIndex).Level;
    /// <summary>The current state's trusted action, or -1 if none.</summary>
    public int CurrentTrustedAction { get { QConfidenceInfo i = EvaluateConfidence(CurrentStateIndex); return i.IsTrusted ? (int)i.Action : -1; } }

    private void Awake()
    {
        ResetLearning();
    }

    /// <summary>
    /// Q-learning starts from zero every match: all 81 values, epsilon, pending decision, counters and stats.
    /// Called by GameManager on the first Learning match, PLAY AGAIN and RESTART MATCH.
    /// </summary>
    public void ResetLearning()
    {
        Array.Clear(q, 0, q.Length);
        Array.Clear(actionCounts, 0, actionCounts.Length);
        Array.Clear(stateUpdates, 0, stateUpdates.Length);
        Array.Clear(samples, 0, samples.Length);
        Array.Clear(evAttempts, 0, evAttempts.Length);
        Array.Clear(evReturns, 0, evReturns.Length);
        Array.Clear(evPlayerFails, 0, evPlayerFails.Length);
        Array.Clear(evAiMisses, 0, evAiMisses.Length);
        for (int s = 0; s < StateCount; s++) lastTrusted[s] = -1;
        trustChanges = 0;
        firstTrustedDecision = 0;
        pendingDropped = false;
        LastDecisionConfidence = QConfidence.Unproven;
        LastDecisionTrustedAction = -1;
        LastDecisionUntrusted = false;
        Array.Clear(returnsBy, 0, ActionCount);
        Array.Clear(scoresBy, 0, ActionCount);
        Array.Clear(missesBy, 0, ActionCount);
        epsilon = startEpsilon;
        decisions = 0;
        updates = 0;
        exploratoryDecisions = 0;
        hasPending = false;
        pendingReward = 0f;
        pendingReturned = false;
        CurrentStateIndex = -1;
        CurrentAction = QAction.ReturnCenter;
        LastDecisionExplored = false;
        LearningChanged?.Invoke();
    }

    // ---------------------------------------------------------------- state encoding

    /// <summary>index = zone * 9 + relation * 3 + tendency  (0..26).</summary>
    public int EncodeState(float predictedInterceptY, float paddleY, float defensiveTargetY, PlayerTendency tendency)
    {
        int zone = predictedInterceptY < -zoneBoundary ? 0 : predictedInterceptY > zoneBoundary ? 2 : 1;

        float offset = paddleY - defensiveTargetY;
        int relation = offset < -alignedTolerance ? 0 : offset > alignedTolerance ? 2 : 1;

        int style = tendency == PlayerTendency.Low ? 0 : tendency == PlayerTendency.High ? 2 : 1;   // Unknown -> NEUTRAL

        return zone * (RelationCount * TendencyCount) + relation * TendencyCount + style;
    }

    public static string StateLabel(int state)
    {
        if (state < 0 || state >= StateCount) return "-";
        int zone = state / (RelationCount * TendencyCount);
        int relation = state / TendencyCount % RelationCount;
        int style = state % TendencyCount;
        return $"{ZoneNames[zone]}/{RelationNames[relation]}/{TendencyNames[style]} (#{state})";
    }

    public static string ActionName(QAction action)
    {
        return action == QAction.ReturnLow ? "LOW" : action == QAction.ReturnHigh ? "HIGH" : "CENTER";
    }

    // ---------------------------------------------------------------- decisions

    /// <summary>
    /// One decision per incoming shot (made when the player's ball starts travelling toward the AI).
    /// Completes the previous decision's update with this new state, then picks a return strategy
    /// epsilon-greedily and returns its desired contact point (paddle half-heights).
    /// </summary>
    public float Decide(int state)
    {
        state = Mathf.Clamp(state, 0, StateCount - 1);

        if (hasPending)
        {
            RecordEvidence(0);
            CompleteUpdate(pendingReward, state, terminal: false);
        }

        // Confidence of this state, from the evidence gathered so far (before this decision).
        QConfidenceInfo confidence = EvaluateConfidence(state);
        LastDecisionConfidence = confidence.Level;
        LastDecisionTrustedAction = confidence.IsTrusted ? (int)confidence.Action : -1;
        NoteTrust(state, LastDecisionTrustedAction);
        LastDecisionUntrusted = false;

        QAction action;
        LastDecisionSampled = false;
        LastDecisionExplored = UnityEngine.Random.value < epsilon;
        if (LastDecisionExplored)
        {
            action = (QAction)UnityEngine.Random.Range(0, ActionCount);
            exploratoryDecisions++;
        }
        else if (tieBreak == QTieBreak.BalancedEarlySampling && TryPickUndersampled(state, out QAction untried))
        {
            action = untried;
            LastDecisionSampled = true;
        }
        else if (useConfidenceGate && !confidence.IsTrusted)
        {
            // No trusted action: exploit only mildly (random tie-break, never a hidden CENTER default) and stay varied.
            LastDecisionUntrusted = true;
            action = UnityEngine.Random.value < untrustedExploitChance ? GreedyRandomTie(state) : (QAction)UnityEngine.Random.Range(0, ActionCount);
        }
        else
        {
            action = BestAction(state);
        }
        samples[Index(state, action)]++;

        CurrentStateIndex = state;
        CurrentAction = action;
        actionCounts[(int)action]++;
        decisions++;

        hasPending = true;
        pendingState = state;
        pendingAction = action;
        pendingReward = 0f;
        pendingReturned = false;
        pendingDropped = false;

        LearningChanged?.Invoke();
        return CurrentDesiredContact;
    }

    /// <summary>
    /// Survival fallback: the paddle could not reach the aimed contact point, so the pending action was not
    /// carried out this time. Balanced early sampling does not count it, so the action is tried again later.
    /// </summary>
    public void OnAimDropped()
    {
        if (!hasPending) return;

        pendingDropped = true;   // not evidence for the pending action either
        if (tieBreak != QTieBreak.BalancedEarlySampling) return;

        int i = Index(pendingState, pendingAction);
        if (samples[i] > 0) samples[i]--;
    }

    /// <summary>The AI paddle returned the ball for the pending decision (+returnReward, once per decision).</summary>
    public void OnReturned()
    {
        if (!hasPending || pendingReturned) return;

        pendingReturned = true;
        pendingReward += returnReward;
        returnsBy[(int)pendingAction]++;
    }

    /// <summary>
    /// A goal ended the rally: terminal update of the pending decision (no future value).
    /// aiScored = the player failed to return the AI's last return -> that return's action gets the score.
    /// </summary>
    public void OnRallyEnded(bool aiScored)
    {
        if (!hasPending) return;

        if (aiScored && pendingReturned) scoresBy[(int)pendingAction]++;
        if (!aiScored) missesBy[(int)pendingAction]++;
        RecordEvidence(aiScored ? 1 : 2);

        CompleteUpdate(pendingReward + (aiScored ? scoreReward : missReward), -1, terminal: true);
        LearningChanged?.Invoke();
    }

    // ---------------------------------------------------------------- evidence + confidence gate

    /// <summary>Counts the pending decision as a completed attempt (unless survival dropped its aim). outcome: 0 rally went on, 1 player failed, 2 AI missed.</summary>
    private void RecordEvidence(int outcome)
    {
        if (!hasPending || pendingDropped) return;

        int i = Index(pendingState, pendingAction);
        evAttempts[i]++;
        if (pendingReturned) evReturns[i]++;
        if (outcome == 1 && pendingReturned) evPlayerFails[i]++;
        if (outcome == 2) evAiMisses[i]++;
    }

    /// <summary>Tracks when a state's trusted action appears, changes or is lost (analysis / debug).</summary>
    private void NoteTrust(int state, int trustedAction)
    {
        if (trustedAction == lastTrusted[state]) return;

        trustChanges++;
        if (trustedAction >= 0 && firstTrustedDecision == 0) firstTrustedDecision = decisions + 1;
        lastTrusted[state] = trustedAction;
    }

    /// <summary>
    /// "Is the best-Q action meaningfully better than the alternatives?"
    ///   samples:     the candidate has >= minSamplesBest completed attempts and an alternative has >= minSamplesAlternative
    ///   performance: candidate's player-failure rate exceeds every qualified alternative's by >= performanceGap
    ///   Q:           candidate's Q-value exceeds every other action's by >= qSeparation
    /// UNPROVEN = samples missing, PROMISING = samples ok but the advantage is not there yet, TRUSTED = all three.
    /// </summary>
    public QConfidenceInfo EvaluateConfidence(int state)
    {
        if (state < 0 || state >= StateCount) return new QConfidenceInfo(QConfidence.Unproven, QAction.ReturnCenter, 0f, 0f);

        int best = 0;
        for (int a = 1; a < ActionCount; a++) if (q[Index(state, (QAction)a)] > q[Index(state, (QAction)best)] + 1e-6f) best = a;

        float otherQ = float.NegativeInfinity, altRate = float.NegativeInfinity;
        bool altQualified = false;
        for (int a = 0; a < ActionCount; a++)
        {
            if (a == best) continue;
            otherQ = Mathf.Max(otherQ, q[Index(state, (QAction)a)]);
            if (evAttempts[Index(state, (QAction)a)] >= minSamplesAlternative)
            {
                altQualified = true;
                altRate = Mathf.Max(altRate, PlayerFailRate(state, (QAction)a));
            }
        }

        float qGap = q[Index(state, (QAction)best)] - otherQ;
        float perfGap = altQualified ? PlayerFailRate(state, (QAction)best) - altRate : 0f;
        bool samplesOk = evAttempts[Index(state, (QAction)best)] >= minSamplesBest && altQualified;

        QConfidence level = !samplesOk ? QConfidence.Unproven
            : perfGap >= performanceGap && qGap >= qSeparation ? QConfidence.Trusted : QConfidence.Promising;
        return new QConfidenceInfo(level, (QAction)best, perfGap, qGap);
    }

    /// <summary>Greedy action; every tied best action is equally likely (no CENTER preference).</summary>
    private QAction GreedyRandomTie(int state)
    {
        float best = MaxQ(state);
        int tied = 0;
        for (int a = 0; a < ActionCount; a++) if (Mathf.Approximately(q[Index(state, (QAction)a)], best)) tied++;
        int pick = UnityEngine.Random.Range(0, tied);
        for (int a = 0; a < ActionCount; a++)
        {
            if (!Mathf.Approximately(q[Index(state, (QAction)a)], best)) continue;
            if (pick-- == 0) return (QAction)a;
        }
        return QAction.ReturnCenter;
    }

    /// <summary>Completed offensive attempts with an action in a state this match (aim-dropped ones excluded).</summary>
    public int AttemptsOf(int state, QAction action) => state < 0 || state >= StateCount ? 0 : evAttempts[Index(state, action)];
    public int ReturnsOf(int state, QAction action) => state < 0 || state >= StateCount ? 0 : evReturns[Index(state, action)];
    public int PlayerFailsOf(int state, QAction action) => state < 0 || state >= StateCount ? 0 : evPlayerFails[Index(state, action)];
    public int AiMissesOf(int state, QAction action) => state < 0 || state >= StateCount ? 0 : evAiMisses[Index(state, action)];

    /// <summary>Player failures caused by returns with this action / completed attempts with it (0 without attempts).</summary>
    public float PlayerFailRate(int state, QAction action)
    {
        int n = AttemptsOf(state, action);
        return n > 0 ? PlayerFailsOf(state, action) / (float)n : 0f;
    }

    /// <summary>Number of states with a TRUSTED action (tendency filter as in EstablishedPreferences; -1 = any).</summary>
    public int TrustedStates(int tendency)
    {
        int count = 0;
        for (int s = 0; s < StateCount; s++)
        {
            if (tendency >= 0 && s % TendencyCount != tendency) continue;
            if (EvaluateConfidence(s).IsTrusted) count++;
        }
        return count;
    }

    // ---------------------------------------------------------------- Q update

    private void CompleteUpdate(float reward, int nextState, bool terminal)
    {
        int i = Index(pendingState, pendingAction);
        float future = terminal ? 0f : discount * MaxQ(nextState);

        // Q(s,a) = Q(s,a) + alpha * [reward + gamma * max Q(s',*) - Q(s,a)]
        q[i] += learningRate * (reward + future - q[i]);

        hasPending = false;
        pendingReward = 0f;
        pendingReturned = false;
        stateUpdates[pendingState]++;
        updates++;
        epsilon = Mathf.Max(minEpsilon, epsilon * epsilonDecay);
    }

    /// <summary>Balanced early sampling: a random action of this state that has been carried out fewer than minSamplesPerAction times.</summary>
    private bool TryPickUndersampled(int state, out QAction action)
    {
        int open = 0;
        for (int a = 0; a < ActionCount; a++) if (samples[Index(state, (QAction)a)] < minSamplesPerAction) open++;
        action = QAction.ReturnCenter;
        if (open == 0) return false;

        int pick = UnityEngine.Random.Range(0, open);
        for (int a = 0; a < ActionCount; a++)
        {
            if (samples[Index(state, (QAction)a)] >= minSamplesPerAction) continue;
            if (pick-- == 0) { action = (QAction)a; return true; }
        }
        return false;
    }

    /// <summary>Greedy action. CenterPreferred / BalancedEarlySampling: ties prefer RETURN_CENTER, then random among the tied. RandomTies: random among all tied best actions.</summary>
    private QAction BestAction(int state)
    {
        float best = float.NegativeInfinity;
        for (int a = 0; a < ActionCount; a++) best = Mathf.Max(best, q[Index(state, (QAction)a)]);

        if (tieBreak != QTieBreak.RandomTies && Mathf.Approximately(q[Index(state, QAction.ReturnCenter)], best)) return QAction.ReturnCenter;

        int tied = 0;
        for (int a = 0; a < ActionCount; a++) if (Mathf.Approximately(q[Index(state, (QAction)a)], best)) tied++;
        int pick = UnityEngine.Random.Range(0, tied);
        for (int a = 0; a < ActionCount; a++)
        {
            if (!Mathf.Approximately(q[Index(state, (QAction)a)], best)) continue;
            if (pick-- == 0) return (QAction)a;
        }
        return QAction.ReturnCenter;
    }

    private float MaxQ(int state)
    {
        float best = float.NegativeInfinity;
        for (int a = 0; a < ActionCount; a++) best = Mathf.Max(best, q[Index(state, (QAction)a)]);
        return best;
    }

    // ---------------------------------------------------------------- read-only access

    public float GetQ(int state, QAction action)
    {
        return state < 0 || state >= StateCount ? 0f : q[Index(state, action)];
    }

    /// <summary>How often an action was chosen this match.</summary>
    public int TimesChosen(QAction action) => actionCounts[(int)action];

    /// <summary>This match's outcomes for one action: AI returns with it, player misses after those returns, AI misses.</summary>
    public void ActionOutcomes(QAction action, out int returns, out int playerMisses, out int aiMisses)
    {
        returns = returnsBy[(int)action];
        playerMisses = scoresBy[(int)action];
        aiMisses = missesBy[(int)action];
    }

    /// <summary>How often an action was carried out (not dropped by survival) in a state this match.</summary>
    public int SamplesOf(int state, QAction action) => state < 0 || state >= StateCount ? 0 : samples[Index(state, action)];

    /// <summary>Completed updates of one state this match.</summary>
    public int StateUpdates(int state) => state < 0 || state >= StateCount ? 0 : stateUpdates[state];

    /// <summary>
    /// States with a learned preference: updated at least minUpdates times, best action value positive and
    /// ahead of the runner-up by at least margin. tendency filters by the player-tendency part of the state
    /// (0 LOW, 1 NEUTRAL, 2 HIGH, -1 any).
    /// </summary>
    public int EstablishedPreferences(int tendency, float margin, int minUpdates)
    {
        int count = 0;
        for (int s = 0; s < StateCount; s++)
        {
            if (tendency >= 0 && s % TendencyCount != tendency) continue;
            if (stateUpdates[s] < minUpdates) continue;

            float best = float.NegativeInfinity, second = float.NegativeInfinity;
            for (int a = 0; a < ActionCount; a++)
            {
                float v = q[Index(s, (QAction)a)];
                if (v > best) { second = best; best = v; }
                else if (v > second) second = v;
            }
            if (best > 0f && best - second >= margin) count++;
        }
        return count;
    }

    /// <summary>Number of non-zero entries in the 81-value table (tests / debug).</summary>
    public int NonZeroValues()
    {
        int n = 0;
        foreach (float v in q) if (v != 0f) n++;
        return n;
    }

    /// <summary>Desired contact point for an action, in paddle half-heights (+ = ball above paddle center = upward return).</summary>
    public float DesiredContactFor(QAction action)
    {
        return action == QAction.ReturnHigh ? returnContactOffset : action == QAction.ReturnLow ? -returnContactOffset : 0f;
    }

    private static int Index(int state, QAction action) => state * ActionCount + (int)action;
}
