using System.Threading.Tasks;

/// <summary>
/// Looks a nickname up in BOTH leaderboards (Classic and Learning) at once, whichever mode is being entered.
/// No Supabase code lives here: it only combines two LeaderboardService case-insensitive lookups.
/// If either lookup fails the whole result fails, so a network problem is never mistaken for "available".
/// </summary>
public static class NicknameLookup
{
    /// <param name="classic">Service for the Classic table (default: Resources/SupabaseConfig).</param>
    /// <param name="learning">Service for the Learning table (default: Resources/SupabaseConfig).</param>
    public static async Task<NicknameLookupResult> LookupAsync(string nickname, LeaderboardService classic = null, LeaderboardService learning = null)
    {
        classic = classic ?? LeaderboardService.CreateDefault(LeaderboardType.Classic);
        learning = learning ?? LeaderboardService.CreateDefault(LeaderboardType.Learning);

        // Both requests run at the same time; neither task ever throws (results carry failures).
        Task<NicknameCheckResult> classicTask = classic.CheckNicknameAvailabilityAsync(nickname);
        Task<NicknameCheckResult> learningTask = learning.CheckNicknameAvailabilityAsync(nickname);
        await Task.WhenAll(classicTask, learningTask);

        NicknameCheckResult classicResult = classicTask.Result;
        NicknameCheckResult learningResult = learningTask.Result;

        if (!classicResult.Success)
            return NicknameLookupResult.Fail(classicResult.Error, "Classic leaderboard: " + classicResult.Message, classicResult.StatusCode);
        if (!learningResult.Success)
            return NicknameLookupResult.Fail(learningResult.Error, "Learning leaderboard: " + learningResult.Message, learningResult.StatusCode);

        return NicknameLookupResult.Ok(classicResult.Nickname, classicResult, learningResult);
    }
}
