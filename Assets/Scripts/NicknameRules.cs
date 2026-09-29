/// <summary>
/// Nickname validation shared by the entry screen (and later the leaderboard).
/// Rules: trimmed, 2-12 characters, ASCII letters, digits, underscore and spaces only.
/// </summary>
public static class NicknameRules
{
    public const int MinLength = 2;
    public const int MaxLength = 12;

    public enum Result { Valid, Empty, TooShort, TooLong, InvalidCharacters }

    public static string Clean(string raw)
    {
        return raw == null ? string.Empty : raw.Trim();
    }

    // ASCII only so every character exists in the UI font.
    public static bool IsAllowedChar(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == ' ';
    }

    public static Result Validate(string raw)
    {
        string nickname = Clean(raw);

        if (nickname.Length == 0) return Result.Empty;
        if (nickname.Length < MinLength) return Result.TooShort;
        if (nickname.Length > MaxLength) return Result.TooLong;

        foreach (char c in nickname)
        {
            if (!IsAllowedChar(c)) return Result.InvalidCharacters;
        }

        return Result.Valid;
    }

    public static string Message(Result result)
    {
        return result switch
        {
            Result.Valid => "READY!",
            Result.Empty => $"{MinLength}-{MaxLength} LETTERS, NUMBERS, _ OR SPACES",
            Result.TooShort => $"TOO SHORT - AT LEAST {MinLength} CHARACTERS",
            Result.TooLong => $"TOO LONG - MAX {MaxLength} CHARACTERS",
            _ => "ONLY LETTERS, NUMBERS, _ AND SPACES"
        };
    }
}
