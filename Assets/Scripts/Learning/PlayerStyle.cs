using UnityEngine;

/// <summary>Where the player sends the ball, from the outgoing direction (not from where the paddle is on screen).</summary>
public enum ShotDirection
{
    Low,
    Center,
    High
}

/// <summary>How steep the outgoing shot is, from its angle to the horizontal.</summary>
public enum ShotSteepness
{
    Flat,
    Moderate,
    Steep
}

/// <summary>The player's current dominant tendency over the recent shot window.</summary>
public enum PlayerTendency
{
    Unknown,    // not enough recent returns yet
    High,
    Neutral,    // enough data, but no direction is dominant
    Low
}

/// <summary>One human return, as observed at the moment the ball leaves the left paddle.</summary>
public readonly struct PlayerShot
{
    /// <summary>1-based count of returns in this match.</summary>
    public readonly int ShotNumber;
    /// <summary>Game time of the return (Time.time).</summary>
    public readonly float Time;
    /// <summary>Where the ball struck the paddle: -1 bottom, 0 center, +1 top.</summary>
    public readonly float HitPosition;
    public readonly Vector2 OutgoingVelocity;
    public readonly float Speed;
    /// <summary>Outgoing vertical direction, normalized: velocity.y / speed (-1 straight down .. +1 straight up).</summary>
    public readonly float VerticalDirection;
    /// <summary>Signed outgoing angle from the horizontal in degrees (+ up, - down).</summary>
    public readonly float AngleDegrees;
    public readonly ShotDirection Direction;
    public readonly ShotSteepness Steepness;

    public PlayerShot(int shotNumber, float time, float hitPosition, Vector2 outgoingVelocity,
                      float angleDegrees, ShotDirection direction, ShotSteepness steepness)
    {
        ShotNumber = shotNumber;
        Time = time;
        HitPosition = hitPosition;
        OutgoingVelocity = outgoingVelocity;
        Speed = outgoingVelocity.magnitude;
        VerticalDirection = Speed > 0.0001f ? outgoingVelocity.y / Speed : 0f;
        AngleDegrees = angleDegrees;
        Direction = direction;
        Steepness = steepness;
    }
}

/// <summary>Read-only summary of the recent shot window. A copy: holding one never changes the tracker.</summary>
public readonly struct PlayerStyleSnapshot
{
    public readonly int TotalShotsObserved;     // this match
    public readonly int RecentShotCount;        // inside the rolling window

    public readonly float HighRatio;
    public readonly float CenterRatio;
    public readonly float LowRatio;

    public readonly float FlatRatio;
    public readonly float ModerateRatio;
    public readonly float SteepRatio;

    public readonly float AverageHitPosition;
    public readonly float AverageOutgoingVerticalDirection;
    public readonly float AverageSpeed;

    public readonly PlayerTendency DominantTendency;

    public PlayerStyleSnapshot(int totalShots, int recentShots,
                               float high, float center, float low,
                               float flat, float moderate, float steep,
                               float averageHit, float averageDirection, float averageSpeed,
                               PlayerTendency dominant)
    {
        TotalShotsObserved = totalShots;
        RecentShotCount = recentShots;
        HighRatio = high;
        CenterRatio = center;
        LowRatio = low;
        FlatRatio = flat;
        ModerateRatio = moderate;
        SteepRatio = steep;
        AverageHitPosition = averageHit;
        AverageOutgoingVerticalDirection = averageDirection;
        AverageSpeed = averageSpeed;
        DominantTendency = dominant;
    }

    public static PlayerStyleSnapshot Empty => default;

    /// <summary>True once a directional pattern (High or Low) is declared.</summary>
    public bool HasDirectionalPattern => DominantTendency == PlayerTendency.High || DominantTendency == PlayerTendency.Low;
}
