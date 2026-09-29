using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AGAINST LEARNING AI opponent for the right paddle (enabled by PongModeController only in Learning mode).
///
/// Baseline: a moderate, human-like opponent that predicts where the ball will reach the paddle
/// (including wall bounces), reacts with a short delay, and aims with a little error plus the occasional
/// misjudged shot. Weaker than the Classic PPO agent on purpose, but not a pushover.
///
/// Style bias (first adaptation): when PlayerStyleTracker reports a clear HIGH or LOW tendency, the
/// paddle's target is shifted slightly in that direction (AdaptiveTargetY = PredictedInterceptY + bias).
/// The bias grows with confidence (dominance and sample count) and is smoothed over time. It only
/// changes positioning: speed, reaction delay, aim error and mistakes stay exactly as in the baseline.
///
/// Two separate adaptation layers:
///   DEFENSE (get to the ball):   AdaptiveTargetY = PredictedInterceptY + CurrentStyleBias
///   OFFENSE (where to send it):  OnlineQLearning picks RETURN_LOW / CENTER / HIGH once per incoming shot.
///     The action sets the desired contact point on this paddle; the paddle is shifted relative to the
///     ball so the normal BallController bounce produces the lower / flat / upper return. No ball velocity
///     is ever written here.
///   While Q has chosen a return strategy for the shot, the contact point is set relative to the ball ONLY
///   (FinalTargetY = PredictedInterceptY + contact shift); the style bias must not move the contact point, or it
///   would silently turn every return into a LOW/HIGH shot. The style bias shapes where the paddle waits
///   between shots (idle anticipation / reach) only: with styleBiasAffectsContact off (production) it never moves
///   the contact point, not even when the aim is dropped or Q is off.
///   Then the baseline aim error / mistakes exactly as before.
/// Survival first: if the aiming spot cannot be reached in time but the defensive spot can, the aim is
/// dropped for that shot. Returns and goals are reported back to the Q layer as rewards.
///
/// Uses the same paddle, collider, Rigidbody2D movement and vertical limits as PongAgent.
/// Only moves while the match is Playing: frozen during the countdown and pause (timeScale 0), idle at Game Over.
///
/// All per-match adaptive data lives behind ResetLearningState(), which GameManager calls at the
/// start of every Learning match (first match, PLAY AGAIN, RESTART MATCH) - never on resume or goals.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AdaptivePaddleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D paddleRb;
    [SerializeField] private Collider2D paddleCollider;
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private Collider2D ballCollider;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private Collider2D topWall;
    [SerializeField] private Collider2D bottomWall;

    [Header("Movement (same limits as PongAgent)")]
    [SerializeField, Min(0f)] private float moveSpeed = 4.75f;
    [SerializeField] private float minY = -3.4f;
    [SerializeField] private float maxY = 3.4f;
    [Tooltip("Paddle stops when it is this close to its target (prevents jitter).")]
    [SerializeField, Min(0f)] private float deadZone = 0.08f;

    [Header("Reaction")]
    [Tooltip("The AI sees the ball as it was this many seconds ago.")]
    [SerializeField, Range(0f, 0.5f)] private float reactionDelay = 0.14f;

    [Header("Imperfection")]
    [Tooltip("Max random aim error per incoming shot (world units).")]
    [SerializeField, Min(0f)] private float predictionError = 0.3f;
    [Tooltip("Extra aim error per predicted wall bounce (bank shots are harder to read).")]
    [SerializeField, Min(0f)] private float errorPerWallBounce = 0.25f;
    [Tooltip("Chance that an incoming shot is badly misjudged.")]
    [SerializeField, Range(0f, 1f)] private float mistakeChance = 0.15f;
    [Tooltip("Aim error of a misjudged shot (world units, applied at 60-100%).")]
    [SerializeField, Min(0f)] private float mistakeError = 1.6f;

    [Header("Idle")]
    [Tooltip("Where the paddle drifts while the ball moves away or is being served.")]
    [SerializeField] private float idleY = 0f;
    [SerializeField, Range(0f, 1f)] private float idleSpeedFactor = 0.6f;

    [Header("Style Bias (learned positioning)")]
    [Tooltip("Reads the player's recent tendency. Only the snapshot is used, never the shot list.")]
    [SerializeField] private PlayerStyleTracker styleTracker;
    [SerializeField] private bool useStyleBias = true;
    [Tooltip("Off (production): the style bias only shapes the idle waiting spot (reach); an incoming shot is always centred on the predicted intercept, so the bias never moves the contact point. On (legacy): the defensive target is predicted Y + style bias, so the bias also moves the contact point whenever Q is off or its aim is dropped.")]
    [SerializeField] private bool styleBiasAffectsContact = false;
    [Tooltip("Largest target shift toward the player's dominant direction (world units).")]
    [SerializeField, Range(0f, 1f)] private float maxStyleBias = 0.45f;
    [Tooltip("Dominant-direction ratio at which the ratio part of confidence reaches 100% (0% at the tracker's pattern threshold).")]
    [SerializeField, Range(0.5f, 1f)] private float fullConfidenceRatio = 0.9f;
    [Tooltip("Recent returns needed for the sample part of confidence to reach 100% (linear from 0).")]
    [SerializeField, Min(1)] private int fullConfidenceShots = 15;
    [Tooltip("Time constant of the bias smoothing (seconds of play for ~63% of a change).")]
    [SerializeField, Min(0.01f)] private float biasSmoothingTime = 1.5f;

    [Header("Q-Learning (return strategy)")]
    [Tooltip("Chooses RETURN_LOW / CENTER / HIGH once per incoming shot. Optional.")]
    [SerializeField] private OnlineQLearning qLearning;
    [Tooltip("Hard cap on style bias + contact shift together (world units). Keeps the ball well on the paddle.")]
    [SerializeField, Range(0f, 1f)] private float maxTotalAdaptiveOffset = 0.65f;
    [Tooltip("Outgoing angle within ± this counts as a CENTER return (debug/statistics only).")]
    [SerializeField, Range(0f, 45f)] private float returnCenterBandDegrees = 12f;

    [Header("Debug (read-only)")]
    [SerializeField] private string learningStage = "BASELINE";

    private struct BallSnapshot
    {
        public float Time;
        public Vector2 Position;
        public Vector2 Velocity;
    }

    // ---------------------------------------------------------------- per-match adaptive data
    // Everything below is cleared by ResetLearningState(). Later stages add the style tracker,
    // Q-table, epsilon and tendencies here.
    private readonly List<BallSnapshot> snapshots = new List<BallSnapshot>();
    private bool shotIncoming;
    private float shotAimError;
    private int shotsFaced;
    private int shotsReturned;
    private int shotsMissed;
    private int mistakesMade;
    private float styleBias;
    private float desiredStyleBias;
    private float styleConfidence;
    private float desiredContact;     // paddle half-heights, + = ball above paddle center
    private float qContactShift;      // world units the paddle is moved relative to the ball (= -desiredContact * half height)
    private bool aimDropped;          // survival: aiming given up for the current shot
    private bool qDecided;            // Q chose a return strategy for the current shot
    private int aimDropCount;         // survival fallbacks this match (statistics only)
    private BallController ballController;

    private int resetCount;

    /// <summary>Predicted ball-center Y at the paddle's hitting line, from what the AI currently perceives.</summary>
    public float PredictedInterceptY { get; private set; }
    /// <summary>True while the perceived ball is heading toward this paddle.</summary>
    public bool HasPrediction { get; private set; }
    public int PredictedWallBounces { get; private set; }
    /// <summary>Learned position before this shot's aim error: PredictedInterceptY (or the idle spot) + CurrentStyleBias.</summary>
    public float AdaptiveTargetY { get; private set; }
    /// <summary>Defensive target + offensive contact shift, clamped (before this shot's aim error).</summary>
    public float FinalTargetY { get; private set; }
    /// <summary>Where the paddle is actually steering (FinalTargetY + aim error while a shot is incoming).</summary>
    public float TargetY { get; private set; }

    /// <summary>Return strategy held for the current incoming shot (the last decision while no shot is incoming).</summary>
    public QAction CurrentQAction => qLearning != null ? qLearning.CurrentAction : QAction.ReturnCenter;
    /// <summary>Desired contact point for the current shot (paddle half-heights, + = upper half; 0 while idle).</summary>
    public float CurrentDesiredContact => desiredContact;
    /// <summary>How far the paddle is shifted relative to the ball to get that contact (world units; 0 while idle or dropped).</summary>
    public float CurrentQContactShift => qDecided && !aimDropped ? qContactShift : 0f;
    /// <summary>True if the aim was given up this shot so the paddle could still reach the ball.</summary>
    public bool AimDroppedForSurvival => aimDropped;
    public int CurrentStateIndex => qLearning != null ? qLearning.CurrentStateIndex : -1;
    /// <summary>What is actually added to the prediction right now (contact shift while aiming, otherwise style bias).</summary>
    public float CurrentTotalAdaptiveOffset => FinalTargetY - PredictedInterceptY;

    /// <summary>Where the ball actually struck this paddle on its last return (-1 bottom .. +1 top).</summary>
    public float LastContactHit { get; private set; }
    /// <summary>Outgoing angle of the AI's last return (degrees, + up toward the player's upper side).</summary>
    public float LastReturnAngle { get; private set; }
    /// <summary>Direction class of the AI's last return (LOW / CENTER / HIGH).</summary>
    public ShotDirection LastReturnDirection { get; private set; } = ShotDirection.Center;
    /// <summary>Number of AI returns this match (the last-return values are valid once this is > 0).</summary>
    public int ReturnsObserved { get; private set; }

    /// <summary>Smoothed target shift from the player's style (+ up, - down, world units).</summary>
    public float CurrentStyleBias => styleBias;
    /// <summary>The bias the smoothing is moving toward.</summary>
    public float DesiredStyleBias => desiredStyleBias;
    /// <summary>0..1: how sure the AI is about the player's High/Low tendency.</summary>
    public float CurrentStyleConfidence => styleConfidence;
    /// <summary>X of the line where the ball center meets this paddle's front face.</summary>
    public float InterceptX => HitLineX();

    public string LearningStage => learningStage;
    /// <summary>Experiment switch: whether the style bias may move the contact point (see the serialized field).</summary>
    public bool StyleBiasAffectsContact => styleBiasAffectsContact;
    /// <summary>How many shots this match gave up their aim so the paddle could still reach the ball (statistics only).</summary>
    public int AimDropCount => aimDropCount;
    public int ShotsFaced => shotsFaced;
    public int ShotsReturned => shotsReturned;
    public int ShotsMissed => shotsMissed;
    public int MistakesMade => mistakesMade;
    /// <summary>How many times ResetLearningState() ran (diagnostics / tests).</summary>
    public int ResetCount => resetCount;

    private void Reset()
    {
        paddleRb = GetComponent<Rigidbody2D>();
        paddleCollider = GetComponent<Collider2D>();
    }

    private void Awake()
    {
        if (paddleRb == null) paddleRb = GetComponent<Rigidbody2D>();
        if (paddleCollider == null) paddleCollider = GetComponent<Collider2D>();
        if (ball != null && ballCollider == null) ballCollider = ball.GetComponent<Collider2D>();
        if (ball != null) ballController = ball.GetComponent<BallController>();
    }

    private void OnEnable()
    {
        if (gameManager != null) gameManager.GoalCounted += OnGoalCounted;
        if (ballController != null) ballController.PaddleBounced += OnPaddleBounced;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.GoalCounted -= OnGoalCounted;
        if (ballController != null) ballController.PaddleBounced -= OnPaddleBounced;
    }

    /// <summary>
    /// Clears all per-match adaptive data so every Learning match starts fresh.
    /// Called on the first Learning match, PLAY AGAIN and RESTART MATCH. Not on RESUME or goals.
    /// </summary>
    public void ResetLearningState()
    {
        snapshots.Clear();
        shotIncoming = false;
        shotAimError = 0f;
        shotsFaced = 0;
        shotsReturned = 0;
        shotsMissed = 0;
        mistakesMade = 0;

        HasPrediction = false;
        PredictedWallBounces = 0;
        PredictedInterceptY = idleY;
        AdaptiveTargetY = idleY;
        FinalTargetY = idleY;
        TargetY = idleY;
        desiredContact = 0f;
        qContactShift = 0f;
        aimDropped = false;
        qDecided = false;
        aimDropCount = 0;
        LastContactHit = 0f;
        LastReturnAngle = 0f;
        LastReturnDirection = ShotDirection.Center;
        ReturnsObserved = 0;
        styleBias = 0f;
        desiredStyleBias = 0f;
        styleConfidence = 0f;
        learningStage = "BASELINE";

        resetCount++;
        Debug.Log($"[LearningAI] Learning state reset (match start #{resetCount}).");
    }

    private void FixedUpdate()
    {
        // Countdown and pause freeze time (no FixedUpdate at all); Game Over leaves the paddle idle.
        if (gameManager != null && gameManager.State != MatchState.Playing) return;
        if (ball == null || paddleRb == null) return;

        RecordSnapshot();
        UpdateStyleBias(Time.fixedDeltaTime);
        UpdateTarget();
        MoveToward(TargetY);
    }

    // ---------------------------------------------------------------- perception

    private void RecordSnapshot()
    {
        snapshots.Add(new BallSnapshot { Time = Time.fixedTime, Position = ball.position, Velocity = ball.linearVelocity });

        // Keep only what the reaction delay needs (plus the newest one older than it).
        float oldest = Time.fixedTime - reactionDelay;
        int remove = 0;
        while (remove + 1 < snapshots.Count && snapshots[remove + 1].Time <= oldest) remove++;
        if (remove > 0) snapshots.RemoveRange(0, remove);
    }

    /// <summary>The ball as the AI "sees" it: reactionDelay seconds in the past.</summary>
    private BallSnapshot Perceived()
    {
        return snapshots[0];
    }

    // ---------------------------------------------------------------- decision

    private void UpdateTarget()
    {
        BallSnapshot seen = Perceived();
        float hitX = HitLineX();
        BallInterceptPredictor.Prediction prediction = PredictIntercept(seen.Position, seen.Velocity);

        HasPrediction = prediction.Valid;
        PredictedWallBounces = prediction.WallBounces;
        PredictedInterceptY = prediction.InterceptY;

        if (!prediction.Valid || seen.Position.x > hitX)
        {
            // Ball going away (or already past): no shot pending, wait at the middle shifted by the learned bias.
            shotIncoming = false;
            desiredContact = 0f;
            qContactShift = 0f;
            aimDropped = false;
            qDecided = false;
            AdaptiveTargetY = Mathf.Clamp(idleY + TotalAdaptiveOffset(0f), minY, maxY);
            FinalTargetY = AdaptiveTargetY;
            TargetY = AdaptiveTargetY;
            return;
        }

        if (!shotIncoming)
        {
            // A new incoming shot: decide once how well this one is read.
            shotIncoming = true;
            shotsFaced++;
            shotAimError = RollAimError(prediction.WallBounces);
            qDecided = qLearning != null && qLearning.isActiveAndEnabled;
            desiredContact = DecideQReturn(prediction.InterceptY);
            // Ball above the paddle center (contact > 0) = paddle below the ball.
            qContactShift = -desiredContact * PaddleHalfHeight();
            aimDropped = false;
        }

        // DEFENSE: get to the ball.
        float defensive = Mathf.Clamp(prediction.InterceptY + (styleBiasAffectsContact ? styleBias : 0f), minY, maxY);
        AdaptiveTargetY = defensive;

        // OFFENSE: place the paddle relative to the ball for the chosen contact point (no style bias here)...
        float attack = Mathf.Clamp(prediction.InterceptY + Mathf.Clamp(qContactShift, -maxTotalAdaptiveOffset, maxTotalAdaptiveOffset), minY, maxY);

        // ...unless that spot is out of reach in the time left while the defensive spot is closer (survival first).
        if (qDecided && !aimDropped)
        {
            float timeLeft = Mathf.Max(0f, prediction.TimeToIntercept - reactionDelay);
            float reach = moveSpeed * timeLeft;
            float y = paddleRb.position.y;
            float attackDistance = Mathf.Abs(attack + shotAimError - y);
            if (attackDistance > reach && Mathf.Abs(defensive + shotAimError - y) < attackDistance)
            {
                aimDropped = true;
                aimDropCount++;
                qLearning.OnAimDropped();
            }
        }

        FinalTargetY = qDecided && !aimDropped ? attack : defensive;
        TargetY = Mathf.Clamp(FinalTargetY + shotAimError, minY, maxY);
    }

    private float TotalAdaptiveOffset(float qOffset)
    {
        return Mathf.Clamp(styleBias + qOffset, -maxTotalAdaptiveOffset, maxTotalAdaptiveOffset);
    }

    private float PaddleHalfHeight()
    {
        return paddleCollider != null ? paddleCollider.bounds.extents.y : 1f;
    }

    // ---------------------------------------------------------------- Q-learning hook

    /// <summary>One return-strategy decision per incoming shot; held until the AI returns or misses it.</summary>
    private float DecideQReturn(float predictedInterceptY)
    {
        if (qLearning == null || !qLearning.isActiveAndEnabled) return 0f;

        PlayerTendency tendency = styleTracker != null ? styleTracker.Snapshot.DominantTendency : PlayerTendency.Unknown;
        float styleTarget = Mathf.Clamp(predictedInterceptY + styleBias, minY, maxY);
        int state = qLearning.EncodeState(predictedInterceptY, paddleRb.position.y, styleTarget, tendency);
        return qLearning.Decide(state);
    }

    // ---------------------------------------------------------------- style bias

    /// <summary>Reads the tracker's snapshot, derives confidence and the desired bias, and eases toward it.</summary>
    private void UpdateStyleBias(float deltaTime)
    {
        PlayerStyleSnapshot snapshot = styleTracker != null ? styleTracker.Snapshot : PlayerStyleSnapshot.Empty;
        float threshold = styleTracker != null ? styleTracker.DominanceThreshold : 0.6f;

        styleConfidence = StyleConfidence(snapshot, threshold, out float direction);
        desiredStyleBias = useStyleBias ? direction * maxStyleBias * styleConfidence : 0f;

        // Exponential smoothing: never snaps, and a strategy change moves the bias back through zero gradually.
        float blend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, biasSmoothingTime));
        styleBias = Mathf.Lerp(styleBias, desiredStyleBias, blend);
        if (Mathf.Abs(styleBias - desiredStyleBias) < 0.0005f) styleBias = desiredStyleBias;

        learningStage = Mathf.Abs(styleBias) > 0.01f ? "STYLE BIAS" : "BASELINE";
    }

    /// <summary>
    /// 0 unless the tendency is High or Low. Then:
    ///   ratio part  = 0 at the pattern threshold .. 1 at fullConfidenceRatio
    ///   sample part = recent returns / fullConfidenceShots (capped at 1)
    ///   confidence  = ratio part x sample part
    /// </summary>
    private float StyleConfidence(PlayerStyleSnapshot snapshot, float threshold, out float direction)
    {
        float ratio;
        switch (snapshot.DominantTendency)
        {
            case PlayerTendency.High: direction = 1f; ratio = snapshot.HighRatio; break;
            case PlayerTendency.Low: direction = -1f; ratio = snapshot.LowRatio; break;
            default: direction = 0f; return 0f;
        }

        float ratioPart = Mathf.InverseLerp(threshold, Mathf.Max(threshold + 0.01f, fullConfidenceRatio), ratio);
        float samplePart = Mathf.Clamp01(snapshot.RecentShotCount / (float)Mathf.Max(1, fullConfidenceShots));
        return ratioPart * samplePart;
    }

    private float RollAimError(int wallBounces)
    {
        if (Random.value < mistakeChance)
        {
            mistakesMade++;
            float side = Random.value < 0.5f ? -1f : 1f;
            return side * mistakeError * Random.Range(0.6f, 1f);
        }

        float spread = predictionError + errorPerWallBounce * wallBounces;
        return Random.Range(-spread, spread);
    }

    // ---------------------------------------------------------------- prediction

    /// <summary>Predicts where a ball with this position/velocity reaches this paddle (walls included).</summary>
    public BallInterceptPredictor.Prediction PredictIntercept(Vector2 ballPosition, Vector2 ballVelocity)
    {
        GetBallYLimits(out float low, out float high);
        return BallInterceptPredictor.Predict(ballPosition, ballVelocity, HitLineX(), low, high);
    }

    /// <summary>Ball-center Y range inside the walls.</summary>
    public void GetBallYLimits(out float low, out float high)
    {
        float radius = BallRadius();
        low = bottomWall != null ? bottomWall.bounds.max.y + radius : -4.5f + radius;
        high = topWall != null ? topWall.bounds.min.y - radius : 4.5f - radius;
    }

    private float HitLineX()
    {
        float paddleX = paddleRb != null ? paddleRb.position.x : transform.position.x;
        float halfWidth = paddleCollider != null ? paddleCollider.bounds.extents.x : 0f;
        float side = paddleX >= 0f ? 1f : -1f;   // the front face points toward the middle
        return paddleX - side * (halfWidth + BallRadius());
    }

    private float BallRadius()
    {
        return ballCollider != null ? ballCollider.bounds.extents.y : 0f;
    }

    // ---------------------------------------------------------------- movement

    private void MoveToward(float targetY)
    {
        Vector2 position = paddleRb.position;
        float difference = targetY - position.y;
        if (Mathf.Abs(difference) <= deadZone) return;

        float speed = shotIncoming ? moveSpeed : moveSpeed * idleSpeedFactor;
        float step = Mathf.Min(Mathf.Abs(difference), speed * Time.fixedDeltaTime) * Mathf.Sign(difference);
        float y = Mathf.Clamp(position.y + step, minY, maxY);

        // X is kept fixed so the paddle only moves vertically (same as PongAgent).
        paddleRb.MovePosition(new Vector2(position.x, y));
    }

    // ---------------------------------------------------------------- outcome counters

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isActiveAndEnabled || ball == null || collision.rigidbody != ball) return;

        shotsReturned++;
        if (qLearning != null && qLearning.isActiveAndEnabled) qLearning.OnReturned();
    }

    /// <summary>Records where the ball actually met this paddle and how the return left (debug / statistics).</summary>
    private void OnPaddleBounced(Collider2D paddle, float hit, Vector2 outgoingVelocity)
    {
        if (paddle != paddleCollider || !isActiveAndEnabled) return;

        float speed = outgoingVelocity.magnitude;
        float angle = speed > 0.0001f ? Mathf.Asin(Mathf.Clamp(outgoingVelocity.y / speed, -1f, 1f)) * Mathf.Rad2Deg : 0f;
        LastContactHit = hit;
        LastReturnAngle = angle;
        LastReturnDirection = angle > returnCenterBandDegrees ? ShotDirection.High : angle < -returnCenterBandDegrees ? ShotDirection.Low : ShotDirection.Center;
        ReturnsObserved++;
    }

    private void OnGoalCounted(GoalSide side, bool lifeLost)
    {
        // The human scores in the right goal: this paddle missed.
        if (side == GoalSide.Right) shotsMissed++;
        shotIncoming = false;

        // Left goal = the human missed = the AI scored. Goals keep the Q-table (terminal update only).
        if (isActiveAndEnabled && qLearning != null && qLearning.isActiveAndEnabled)
            qLearning.OnRallyEnded(side == GoalSide.Left);
    }
}
