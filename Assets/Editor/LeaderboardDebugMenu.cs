using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only checks for the Supabase leaderboard data layer (not included in builds).
/// Uses Resources/SupabaseConfig. Keys are never logged.
/// </summary>
public static class LeaderboardDebugMenu
{
    private const string TestNickname = "TEST_PLAYER";

    [MenuItem("PongAI/Leaderboard/Run Live Test (TEST_PLAYER 5 → 3 → 8)")]
    private static async void RunLiveTest()
    {
        var service = LeaderboardService.CreateDefault();
        if (!service.IsConfigured)
        {
            Debug.Log("[Leaderboard test] Not run: fill in Assets/Resources/SupabaseConfig (project URL + anon key) first.");
            return;
        }

        Debug.Log($"[Leaderboard test] A. submit {TestNickname} 5 -> {await service.SubmitScoreAsync(TestNickname, 5)}");
        Debug.Log($"[Leaderboard test] B. submit {TestNickname} 3 (lower) -> {await service.SubmitScoreAsync(TestNickname, 3)}");
        Debug.Log($"[Leaderboard test] C. submit {TestNickname} 8 (higher) -> {await service.SubmitScoreAsync(TestNickname, 8)}");
        await LogTop10();
        Debug.Log($"[Leaderboard test] Done. Delete the '{TestNickname}' row in the Supabase dashboard when finished.");
    }

    [MenuItem("PongAI/Leaderboard/Fetch Top 10")]
    private static async void FetchTop10()
    {
        await LogTop10();
    }

    private static async System.Threading.Tasks.Task LogTop10()
    {
        TopScoresResult top = await LeaderboardService.CreateDefault().GetTop10Async();
        if (!top.Success)
        {
            Debug.Log("[Leaderboard test] D. Top 10 failed -> " + top);
            return;
        }

        var lines = new System.Text.StringBuilder($"[Leaderboard test] D. Top 10 ({top.Entries.Count}):");
        for (int i = 0; i < top.Entries.Count; i++)
            lines.Append($"\n  {i + 1}. {top.Entries[i].nickname} {top.Entries[i].high_score} ({top.Entries[i].updated_at})");
        Debug.Log(lines.ToString());
    }
}
