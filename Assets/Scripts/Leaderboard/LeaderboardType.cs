/// <summary>
/// Which Supabase leaderboard table a LeaderboardService talks to.
/// Each game mode has its own table; scores are never mixed between them.
/// </summary>
public enum LeaderboardType
{
    Classic,    // table "leaderboard"
    Learning    // table "learning_leaderboard"
}

public static class LeaderboardTypes
{
    /// <summary>The leaderboard a match of the given mode submits to.</summary>
    public static LeaderboardType ForMode(GameMode mode)
    {
        return mode == GameMode.Learning ? LeaderboardType.Learning : LeaderboardType.Classic;
    }

    /// <summary>Screen title for the leaderboard UI.</summary>
    public static string Title(LeaderboardType type)
    {
        return type == LeaderboardType.Learning ? "LEARNING AI TOP 10" : "CLASSIC TOP 10";
    }
}
