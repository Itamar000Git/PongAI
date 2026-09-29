using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Supabase connection settings for the leaderboard. Lives as an asset in a Resources folder
/// (Resources/SupabaseConfig) so no URL or key is hardcoded in gameplay code.
/// Only ever put the PUBLIC anon / publishable key here - never the service_role / secret key.
/// </summary>
[CreateAssetMenu(fileName = "SupabaseConfig", menuName = "PongAI/Supabase Config")]
public class SupabaseConfig : ScriptableObject
{
    public const string DefaultResourcePath = "SupabaseConfig";

    [Tooltip("Project URL, e.g. https://abcdefghijklmnop.supabase.co")]
    [SerializeField] private string projectUrl = "https://urvdmwanvsvcrrmtzttb.supabase.co";

    [Tooltip("PUBLIC anon (legacy JWT) or publishable (sb_publishable_...) key. Never the service_role / sb_secret_ key.")]
    [SerializeField] private string anonKey = "sb_publishable_Gm_vr05vWrups_34zc7fLA_1Jn74E33";

    [Tooltip("Classic Mode table.")]
    [SerializeField] private string leaderboardTable = "leaderboard";

    [Tooltip("AGAINST LEARNING AI table (same schema and rules, separate scores).")]
    [SerializeField] private string learningLeaderboardTable = "learning_leaderboard";

    [SerializeField, Min(1)] private int requestTimeoutSeconds = 10;

    public string ProjectUrl => (projectUrl ?? string.Empty).Trim().TrimEnd('/');
    public string AnonKey => (anonKey ?? string.Empty).Trim();
    public string LeaderboardTable => string.IsNullOrWhiteSpace(leaderboardTable) ? "leaderboard" : leaderboardTable.Trim();
    public string LearningLeaderboardTable => string.IsNullOrWhiteSpace(learningLeaderboardTable) ? "learning_leaderboard" : learningLeaderboardTable.Trim();

    /// <summary>Table name for a leaderboard type. Classic keeps using LeaderboardTable.</summary>
    public string TableFor(LeaderboardType type)
    {
        return type == LeaderboardType.Learning ? LearningLeaderboardTable : LeaderboardTable;
    }

    public int RequestTimeoutSeconds => Mathf.Max(1, requestTimeoutSeconds);
    public string RestUrl => ProjectUrl + "/rest/v1/";

    public bool IsConfigured => ProjectUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && AnonKey.Length > 0;

    /// <summary>Legacy anon keys are JWTs (three dot-separated parts); new publishable keys are not.</summary>
    public bool KeyIsJwt => AnonKey.Split('.').Length == 3;

    /// <summary>True if the key is a privileged server key that must never ship in a client build.</summary>
    public bool LooksLikeServiceRoleKey
    {
        get
        {
            string key = AnonKey;
            if (key.StartsWith("sb_secret_", StringComparison.Ordinal)) return true;
            if (!KeyIsJwt) return false;

            try
            {
                string payload = key.Split('.')[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return json.Replace(" ", string.Empty).Contains("\"role\":\"service_role\"");
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    public static SupabaseConfig LoadDefault()
    {
        return Resources.Load<SupabaseConfig>(DefaultResourcePath);
    }
}
