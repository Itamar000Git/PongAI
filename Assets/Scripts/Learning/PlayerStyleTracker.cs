using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AGAINST LEARNING AI: observes the HUMAN's returns and keeps a short-term profile of how they
/// currently play (enabled by PongModeController only in Learning mode).
///
///   left paddle bounce (BallController.PaddleBounced)
///       -> one PlayerShot (hit position, outgoing velocity, direction, steepness)
///       -> rolling window of the most recent returns
///       -> PlayerStyleSnapshot (ratios, averages, dominant tendency)
///
/// Measurement and analysis only: nothing here moves or tunes the AI yet.
/// Reset at the start of every Learning match through GameManager's Learning reset hook.
/// </summary>
public class PlayerStyleTracker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BallController ball;
    [Tooltip("The human (left) paddle's collider. Only bounces off this collider are recorded.")]
    [SerializeField] private Collider2D playerPaddle;
    [SerializeField] private GameManager gameManager;

    [Header("Rolling Window")]
    [Tooltip("How many of the most recent returns the profile is built from. Older returns drop out.")]
    [SerializeField, Range(5, 50)] private int historySize = 20;

    [Header("Direction (outgoing angle from horizontal)")]
    [Tooltip("Returns within ± this angle are CENTER; steeper upward = HIGH, steeper downward = LOW.")]
    [SerializeField, Range(0f, 45f)] private float centerBandDegrees = 12f;

    [Header("Steepness (outgoing angle from horizontal)")]
    [Tooltip("At or below this angle a return is FLAT.")]
    [SerializeField, Range(0f, 60f)] private float flatMaxDegrees = 15f;
    [Tooltip("At or above this angle a return is STEEP (between the two: MODERATE).")]
    [SerializeField, Range(0f, 80f)] private float steepMinDegrees = 35f;

    [Header("Pattern Detection")]
    [Tooltip("Below this many recent returns the tendency stays Unknown.")]
    [SerializeField, Min(1)] private int minShotsForPattern = 7;
    [Tooltip("Share of recent returns one direction needs before it counts as a pattern.")]
    [SerializeField, Range(0.34f, 1f)] private float dominanceThreshold = 0.6f;

    [Header("Safety")]
    [Tooltip("A second left-paddle contact within this time is the same physical return and is ignored.")]
    [SerializeField, Min(0f)] private float minSecondsBetweenShots = 0.1f;

    // Ring buffer: history[head] is the oldest shot, count shots are valid.
    private PlayerShot[] history;
    private int head;
    private int count;
    private int totalShots;
    private float lastShotTime = float.NegativeInfinity;
    private PlayerStyleSnapshot snapshot;

    /// <summary>Raised after every recorded return and after every reset, with the new summary.</summary>
    public event Action<PlayerStyleSnapshot> StyleUpdated;

    /// <summary>Current summary of the recent window (a copy).</summary>
    public PlayerStyleSnapshot Snapshot => snapshot;
    public int HistorySize => historySize;
    public int MinShotsForPattern => minShotsForPattern;
    /// <summary>Share one direction needs to count as a pattern (configuration, not shot data).</summary>
    public float DominanceThreshold => dominanceThreshold;

    private void Awake()
    {
        EnsureHistory();
    }

    private void OnEnable()
    {
        if (ball != null) ball.PaddleBounced += OnPaddleBounced;
    }

    private void OnDisable()
    {
        if (ball != null) ball.PaddleBounced -= OnPaddleBounced;
    }

    /// <summary>
    /// Clears everything (history, counts, averages, tendency). Called at the start of every Learning
    /// match (first match, PLAY AGAIN, RESTART MATCH) - never on goals, pause, resume or settings.
    /// </summary>
    public void ResetTracking()
    {
        EnsureHistory();
        Array.Clear(history, 0, history.Length);
        head = 0;
        count = 0;
        totalShots = 0;
        lastShotTime = float.NegativeInfinity;
        snapshot = BuildSnapshot();
        StyleUpdated?.Invoke(snapshot);
    }

    /// <summary>Copies the recent window into the given list, oldest first. The tracker's own buffer stays private.</summary>
    public void CopyRecentShots(List<PlayerShot> into)
    {
        into.Clear();
        for (int i = 0; i < count; i++) into.Add(history[(head + i) % history.Length]);
    }

    public bool TryGetLatestShot(out PlayerShot shot)
    {
        if (count == 0)
        {
            shot = default;
            return false;
        }

        shot = history[(head + count - 1) % history.Length];
        return true;
    }

    /// <summary>Deterministic classification of an outgoing velocity (shared by recording and tests).</summary>
    public void Classify(Vector2 outgoingVelocity, out float angleDegrees, out ShotDirection direction, out ShotSteepness steepness)
    {
        float speed = outgoingVelocity.magnitude;
        float vertical = speed > 0.0001f ? Mathf.Clamp(outgoingVelocity.y / speed, -1f, 1f) : 0f;
        angleDegrees = Mathf.Asin(vertical) * Mathf.Rad2Deg;

        if (angleDegrees > centerBandDegrees) direction = ShotDirection.High;
        else if (angleDegrees < -centerBandDegrees) direction = ShotDirection.Low;
        else direction = ShotDirection.Center;

        float absolute = Mathf.Abs(angleDegrees);
        if (absolute <= flatMaxDegrees) steepness = ShotSteepness.Flat;
        else if (absolute >= steepMinDegrees) steepness = ShotSteepness.Steep;
        else steepness = ShotSteepness.Moderate;
    }

    // ---------------------------------------------------------------- recording

    private void OnPaddleBounced(Collider2D paddle, float hitPosition, Vector2 outgoingVelocity)
    {
        if (paddle != playerPaddle) return;
        if (gameManager == null || !gameManager.IsLearningMode || gameManager.State != MatchState.Playing) return;

        // One physical contact = one shot, even if the physics reports a second touch right after.
        if (Time.time - lastShotTime < minSecondsBetweenShots) return;
        lastShotTime = Time.time;

        Classify(outgoingVelocity, out float angle, out ShotDirection direction, out ShotSteepness steepness);
        totalShots++;
        Record(new PlayerShot(totalShots, Time.time, Mathf.Clamp(hitPosition, -1f, 1f), outgoingVelocity, angle, direction, steepness));
    }

    private void Record(PlayerShot shot)
    {
        EnsureHistory();
        if (count < history.Length)
        {
            history[(head + count) % history.Length] = shot;
            count++;
        }
        else
        {
            // Full: the newest return replaces the oldest.
            history[head] = shot;
            head = (head + 1) % history.Length;
        }

        snapshot = BuildSnapshot();
        StyleUpdated?.Invoke(snapshot);
    }

    // ---------------------------------------------------------------- analysis

    private PlayerStyleSnapshot BuildSnapshot()
    {
        if (count == 0)
            return new PlayerStyleSnapshot(totalShots, 0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, PlayerTendency.Unknown);

        int high = 0, center = 0, low = 0, flat = 0, moderate = 0, steep = 0;
        float hitSum = 0f, directionSum = 0f, speedSum = 0f;

        for (int i = 0; i < count; i++)
        {
            PlayerShot shot = history[(head + i) % history.Length];
            switch (shot.Direction)
            {
                case ShotDirection.High: high++; break;
                case ShotDirection.Low: low++; break;
                default: center++; break;
            }
            switch (shot.Steepness)
            {
                case ShotSteepness.Flat: flat++; break;
                case ShotSteepness.Steep: steep++; break;
                default: moderate++; break;
            }
            hitSum += shot.HitPosition;
            directionSum += shot.VerticalDirection;
            speedSum += shot.Speed;
        }

        float n = count;
        float highRatio = high / n, centerRatio = center / n, lowRatio = low / n;

        return new PlayerStyleSnapshot(totalShots, count,
            highRatio, centerRatio, lowRatio,
            flat / n, moderate / n, steep / n,
            hitSum / n, directionSum / n, speedSum / n,
            DominantTendency(highRatio, lowRatio));
    }

    /// <summary>Unknown until enough returns; then High / Low only if clearly dominant, otherwise Neutral.</summary>
    private PlayerTendency DominantTendency(float highRatio, float lowRatio)
    {
        if (count < minShotsForPattern) return PlayerTendency.Unknown;
        if (highRatio >= dominanceThreshold) return PlayerTendency.High;
        if (lowRatio >= dominanceThreshold) return PlayerTendency.Low;
        return PlayerTendency.Neutral;
    }

    private void EnsureHistory()
    {
        int size = Mathf.Max(1, historySize);
        if (history != null && history.Length == size) return;

        history = new PlayerShot[size];
        head = 0;
        count = 0;
    }
}
