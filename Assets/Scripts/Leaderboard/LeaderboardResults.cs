using System.Collections.Generic;

public enum LeaderboardError
{
    None,
    NotConfigured,      // missing URL/key, or a forbidden service_role key
    InvalidInput,       // bad nickname or score
    NoConnection,       // no internet / host unreachable
    Timeout,
    Unauthorized,       // 401/403: wrong key or missing RLS policy
    NotFound,           // 404: table / endpoint missing
    Conflict,           // 409 that could not be resolved
    ServerUnavailable,  // 5xx: Supabase down or overloaded
    HttpError,          // any other HTTP error
    InvalidResponse     // unreadable / unexpected response body
}

public enum NicknameAvailability
{
    Available,
    Taken
}

public enum ScoreSubmitOutcome
{
    None,
    Inserted,       // first score for this nickname
    Updated,        // new personal best stored
    NotImproved     // existing best is equal or higher; nothing written
}

/// <summary>Result of a leaderboard call. Failures carry a readable message; nothing is thrown.</summary>
public class LeaderboardResult
{
    public bool Success { get; protected set; }
    public LeaderboardError Error { get; protected set; }
    public string Message { get; protected set; }
    public long StatusCode { get; protected set; }

    protected void SetFailure(LeaderboardError error, string message, long statusCode)
    {
        Success = false;
        Error = error;
        Message = message;
        StatusCode = statusCode;
    }

    public override string ToString()
    {
        return Success ? "OK" : $"{Error}{(StatusCode > 0 ? $" (HTTP {StatusCode})" : "")}: {Message}";
    }
}

public sealed class SubmitScoreResult : LeaderboardResult
{
    public ScoreSubmitOutcome Outcome { get; private set; }
    /// <summary>The nickname's best score after this call.</summary>
    public int HighScore { get; private set; }
    /// <summary>Best score before this call, or -1 if the nickname was new / unknown.</summary>
    public int PreviousHighScore { get; private set; } = -1;

    public static SubmitScoreResult Ok(ScoreSubmitOutcome outcome, int highScore, int previousHighScore)
    {
        return new SubmitScoreResult
        {
            Success = true,
            Outcome = outcome,
            HighScore = highScore,
            PreviousHighScore = previousHighScore,
            Message = outcome.ToString()
        };
    }

    public static SubmitScoreResult Fail(LeaderboardError error, string message, long statusCode = 0)
    {
        var result = new SubmitScoreResult();
        result.SetFailure(error, message, statusCode);
        return result;
    }

    public override string ToString()
    {
        return Success ? $"{Outcome} (best {HighScore}, previous {PreviousHighScore})" : base.ToString();
    }
}

public sealed class TopScoresResult : LeaderboardResult
{
    private static readonly IReadOnlyList<LeaderboardEntry> Empty = new List<LeaderboardEntry>();

    /// <summary>Ranked entries (best first). Empty, never null, when the leaderboard has no rows or the call failed.</summary>
    public IReadOnlyList<LeaderboardEntry> Entries { get; private set; } = Empty;

    public static TopScoresResult Ok(List<LeaderboardEntry> entries)
    {
        return new TopScoresResult
        {
            Success = true,
            Entries = entries ?? new List<LeaderboardEntry>(),
            Message = entries == null || entries.Count == 0 ? "Leaderboard is empty." : $"{entries.Count} entries"
        };
    }

    public static TopScoresResult Fail(LeaderboardError error, string message, long statusCode = 0)
    {
        var result = new TopScoresResult();
        result.SetFailure(error, message, statusCode);
        return result;
    }
}

public sealed class NicknameCheckResult : LeaderboardResult
{
    /// <summary>The trimmed nickname this result belongs to.</summary>
    public string Nickname { get; private set; }
    public NicknameAvailability Availability { get; private set; }
    /// <summary>The stored spelling of the nickname when it is taken (may differ in capitalization).</summary>
    public string ExistingNickname { get; private set; }
    /// <summary>The stored best score when the nickname is taken, otherwise -1.</summary>
    public int HighScore { get; private set; } = -1;
    public bool IsAvailable => Success && Availability == NicknameAvailability.Available;

    public static NicknameCheckResult Ok(string nickname, NicknameAvailability availability, string existingNickname, int highScore = -1)
    {
        return new NicknameCheckResult
        {
            Success = true,
            Nickname = nickname,
            Availability = availability,
            ExistingNickname = existingNickname,
            HighScore = highScore,
            Message = availability.ToString()
        };
    }

    public static NicknameCheckResult Fail(LeaderboardError error, string message, long statusCode = 0)
    {
        var result = new NicknameCheckResult();
        result.SetFailure(error, message, statusCode);
        return result;
    }

    public override string ToString()
    {
        return Success ? $"{Availability}{(ExistingNickname != null ? " (as '" + ExistingNickname + "')" : "")}" : base.ToString();
    }
}

/// <summary>
/// Result of looking a nickname up in BOTH leaderboards (Classic and Learning), whichever mode is being entered.
/// Success only if both lookups succeeded: a failed lookup is never reported as "not found".
/// </summary>
public sealed class NicknameLookupResult : LeaderboardResult
{
    /// <summary>The trimmed nickname that was looked up.</summary>
    public string Nickname { get; private set; }
    public bool ExistsInClassic { get; private set; }
    /// <summary>Stored Classic best, or -1 if the nickname has no Classic row.</summary>
    public int ClassicBest { get; private set; } = -1;
    /// <summary>The spelling stored in the Classic table (may differ in capitalization).</summary>
    public string ClassicStoredName { get; private set; }
    public bool ExistsInLearning { get; private set; }
    public int LearningBest { get; private set; } = -1;
    public string LearningStoredName { get; private set; }

    public bool ExistsAnywhere => Success && (ExistsInClassic || ExistsInLearning);

    /// <summary>
    /// The spelling to use for this player: the one stored in the given mode's table, else the other table's,
    /// else what was typed. Keeps a returning player's score updates on the existing row.
    /// </summary>
    public string CanonicalNickname(GameMode mode)
    {
        string primary = mode == GameMode.Learning ? LearningStoredName : ClassicStoredName;
        string secondary = mode == GameMode.Learning ? ClassicStoredName : LearningStoredName;
        return !string.IsNullOrEmpty(primary) ? primary : !string.IsNullOrEmpty(secondary) ? secondary : Nickname;
    }

    public static NicknameLookupResult Ok(string nickname, NicknameCheckResult classic, NicknameCheckResult learning)
    {
        return new NicknameLookupResult
        {
            Success = true,
            Nickname = nickname,
            ExistsInClassic = classic.Availability == NicknameAvailability.Taken,
            ClassicBest = classic.Availability == NicknameAvailability.Taken ? classic.HighScore : -1,
            ClassicStoredName = classic.ExistingNickname,
            ExistsInLearning = learning.Availability == NicknameAvailability.Taken,
            LearningBest = learning.Availability == NicknameAvailability.Taken ? learning.HighScore : -1,
            LearningStoredName = learning.ExistingNickname,
            Message = "Lookup complete"
        };
    }

    public static NicknameLookupResult Fail(LeaderboardError error, string message, long statusCode = 0)
    {
        var result = new NicknameLookupResult();
        result.SetFailure(error, message, statusCode);
        return result;
    }

    public override string ToString()
    {
        return Success
            ? $"classic={(ExistsInClassic ? ClassicBest.ToString() : "-")} learning={(ExistsInLearning ? LearningBest.ToString() : "-")}"
            : base.ToString();
    }
}
