using System;
using System.Globalization;

/// <summary>
/// One leaderboard row. Field names match the Supabase columns exactly because
/// JsonUtility maps JSON keys to field names.
/// </summary>
[Serializable]
public class LeaderboardEntry
{
    public string nickname;
    public int high_score;
    public string updated_at;

    public string Nickname => nickname;
    public int HighScore => high_score;

    /// <summary>updated_at parsed as UTC, or null if missing / unparsable.</summary>
    public DateTime? UpdatedAtUtc
    {
        get
        {
            if (string.IsNullOrEmpty(updated_at)) return null;
            return DateTime.TryParse(updated_at, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime value)
                ? value
                : (DateTime?)null;
        }
    }

    public override string ToString()
    {
        return $"{nickname} {high_score}";
    }
}
