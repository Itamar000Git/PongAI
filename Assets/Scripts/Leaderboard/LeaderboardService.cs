using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Supabase (PostgREST) data layer for the global leaderboards. All HTTP lives here.
/// One service talks to one table, chosen by LeaderboardType (Classic: leaderboard, Learning: learning_leaderboard).
/// Every public method returns a result object and never throws into gameplay.
/// Call from the main thread (UnityWebRequest requirement).
/// </summary>
public class LeaderboardService
{
    public const int TopCount = 10;
    public const int MaxScore = 1000000;

    private const string Columns = "nickname,high_score,updated_at";
    private const string UniqueViolation = "23505";

    private readonly SupabaseConfig config;
    private readonly LeaderboardType type;

    public LeaderboardService(SupabaseConfig config, LeaderboardType type = LeaderboardType.Classic)
    {
        this.config = config;
        this.type = type;
    }

    /// <summary>Service using Resources/SupabaseConfig. Classic unless a type is given.</summary>
    public static LeaderboardService CreateDefault(LeaderboardType type = LeaderboardType.Classic)
    {
        return new LeaderboardService(SupabaseConfig.LoadDefault(), type);
    }

    public LeaderboardType Type => type;

    /// <summary>The table this service reads and writes (null if there is no config).</summary>
    public string TableName => config != null ? config.TableFor(type) : null;

    public bool IsConfigured => ConfigError() == null;

    /// <summary>
    /// Keeps only the player's best score: inserts a new nickname, raises an existing best,
    /// and leaves a higher existing best untouched.
    /// </summary>
    public async Task<SubmitScoreResult> SubmitScoreAsync(string nickname, int score)
    {
        try
        {
            string configError = ConfigError();
            if (configError != null)
                return SubmitScoreResult.Fail(LeaderboardError.NotConfigured, configError);

            string name = NicknameRules.Clean(nickname);
            NicknameRules.Result nameCheck = NicknameRules.Validate(name);
            if (nameCheck != NicknameRules.Result.Valid)
                return SubmitScoreResult.Fail(LeaderboardError.InvalidInput, "Invalid nickname: " + NicknameRules.Message(nameCheck));
            if (score < 0 || score > MaxScore)
                return SubmitScoreResult.Fail(LeaderboardError.InvalidInput, $"Score must be between 0 and {MaxScore}.");

            EntryLookup existing = await FindEntryAsync(name);
            if (existing.Failure != null)
                return SubmitScoreResult.Fail(existing.Failure.Error, existing.Failure.Message, existing.Failure.StatusCode);

            if (existing.Entry == null)
            {
                string insertBody = JsonUtility.ToJson(new InsertBody { nickname = name, high_score = score });
                HttpResponse insert = await SendAsync("POST", TableUrl(), insertBody, returnRows: true);
                if (insert.IsSuccess)
                    return SubmitScoreResult.Ok(ScoreSubmitOutcome.Inserted, score, -1);

                if (!IsUniqueViolation(insert))
                    return FailSubmit(insert);

                // Another client created this nickname between our lookup and insert: treat it as existing.
                existing = await FindEntryAsync(name);
                if (existing.Failure != null)
                    return SubmitScoreResult.Fail(existing.Failure.Error, existing.Failure.Message, existing.Failure.StatusCode);
                if (existing.Entry == null)
                    return SubmitScoreResult.Fail(LeaderboardError.Conflict, "Nickname conflict could not be resolved. Try again.", insert.StatusCode);
            }

            int previous = existing.Entry.high_score;
            if (score <= previous)
                return SubmitScoreResult.Ok(ScoreSubmitOutcome.NotImproved, previous, previous);

            // Conditional update: only rows whose stored best is still lower than this score change,
            // so a concurrent higher submission is never overwritten.
            string updateBody = JsonUtility.ToJson(new UpdateBody { high_score = score, updated_at = DateTime.UtcNow.ToString("o") });
            // Filter on the stored spelling (the lookup above is case-insensitive).
            string updateUrl = $"{TableUrl()}?nickname=eq.{Escape(existing.Entry.nickname)}&high_score=lt.{score}";
            HttpResponse update = await SendAsync("PATCH", updateUrl, updateBody, returnRows: true);
            if (!update.IsSuccess)
                return FailSubmit(update);

            List<LeaderboardEntry> updatedRows = ParseEntries(update.Body);
            if (updatedRows == null)
                return SubmitScoreResult.Fail(LeaderboardError.InvalidResponse, "Supabase returned an unreadable response.", update.StatusCode);
            if (updatedRows.Count > 0)
                return SubmitScoreResult.Ok(ScoreSubmitOutcome.Updated, score, previous);

            // Nothing matched: someone stored an equal or higher score in the meantime.
            EntryLookup latest = await FindEntryAsync(name);
            int best = latest.Entry != null ? latest.Entry.high_score : previous;
            return SubmitScoreResult.Ok(ScoreSubmitOutcome.NotImproved, best, previous);
        }
        catch (Exception e)
        {
            return SubmitScoreResult.Fail(LeaderboardError.InvalidResponse, "Unexpected leaderboard error: " + e.Message);
        }
    }

    /// <summary>Global Top 10, best first; ties go to whoever reached the score first.</summary>
    public async Task<TopScoresResult> GetTop10Async()
    {
        try
        {
            string configError = ConfigError();
            if (configError != null)
                return TopScoresResult.Fail(LeaderboardError.NotConfigured, configError);

            string url = $"{TableUrl()}?select={Columns}&order=high_score.desc,updated_at.asc&limit={TopCount}";
            HttpResponse response = await SendAsync("GET", url, null, returnRows: false);
            if (!response.IsSuccess)
            {
                LeaderboardResult failure = ToFailure(response);
                return TopScoresResult.Fail(failure.Error, failure.Message, failure.StatusCode);
            }

            List<LeaderboardEntry> entries = ParseEntries(response.Body);
            if (entries == null)
                return TopScoresResult.Fail(LeaderboardError.InvalidResponse, "Supabase returned an unreadable leaderboard.", response.StatusCode);

            return TopScoresResult.Ok(entries);
        }
        catch (Exception e)
        {
            return TopScoresResult.Fail(LeaderboardError.InvalidResponse, "Unexpected leaderboard error: " + e.Message);
        }
    }

    /// <summary>
    /// Case-insensitive check whether a nickname already has a leaderboard row
    /// ("Itamar", "ITAMAR" and "itamar" are the same nickname). Fetches at most one row.
    /// </summary>
    public async Task<NicknameCheckResult> CheckNicknameAvailabilityAsync(string nickname)
    {
        try
        {
            string configError = ConfigError();
            if (configError != null)
                return NicknameCheckResult.Fail(LeaderboardError.NotConfigured, configError);

            string name = NicknameRules.Clean(nickname);
            NicknameRules.Result nameCheck = NicknameRules.Validate(name);
            if (nameCheck != NicknameRules.Result.Valid)
                return NicknameCheckResult.Fail(LeaderboardError.InvalidInput, "Invalid nickname: " + NicknameRules.Message(nameCheck));

            // ILIKE without wildcards = case-insensitive equality; '_' (allowed in nicknames) is a LIKE
            // wildcard, so it is escaped to match only a literal underscore.
            string url = $"{TableUrl()}?select={Columns}&nickname=ilike.{Escape(LikeLiteral(name))}&limit=1";
            HttpResponse response = await SendAsync("GET", url, null, returnRows: false);
            if (!response.IsSuccess)
            {
                LeaderboardResult failure = ToFailure(response);
                return NicknameCheckResult.Fail(failure.Error, failure.Message, failure.StatusCode);
            }

            List<LeaderboardEntry> rows = ParseEntries(response.Body);
            if (rows == null)
                return NicknameCheckResult.Fail(LeaderboardError.InvalidResponse, "Supabase returned an unreadable response.", response.StatusCode);

            return rows.Count == 0
                ? NicknameCheckResult.Ok(name, NicknameAvailability.Available, null)
                : NicknameCheckResult.Ok(name, NicknameAvailability.Taken, rows[0].nickname, rows[0].high_score);
        }
        catch (Exception e)
        {
            return NicknameCheckResult.Fail(LeaderboardError.InvalidResponse, "Unexpected leaderboard error: " + e.Message);
        }
    }

    // ---------------------------------------------------------------- lookups

    private sealed class EntryLookup
    {
        public LeaderboardEntry Entry;
        public LeaderboardResult Failure;
    }

    private async Task<EntryLookup> FindEntryAsync(string nickname)
    {
        string url = $"{TableUrl()}?select={Columns}&nickname=ilike.{Escape(LikeLiteral(nickname))}&limit=1";
        HttpResponse response = await SendAsync("GET", url, null, returnRows: false);
        if (!response.IsSuccess)
            return new EntryLookup { Failure = ToFailure(response) };

        List<LeaderboardEntry> rows = ParseEntries(response.Body);
        if (rows == null)
            return new EntryLookup { Failure = Failure(LeaderboardError.InvalidResponse, "Supabase returned an unreadable response.", response.StatusCode) };

        return new EntryLookup { Entry = rows.Count > 0 ? rows[0] : null };
    }

    // ---------------------------------------------------------------- HTTP

    private readonly struct HttpResponse
    {
        public readonly UnityWebRequest.Result Result;
        public readonly long StatusCode;
        public readonly string Body;
        public readonly string TransportError;

        public HttpResponse(UnityWebRequest.Result result, long statusCode, string body, string transportError)
        {
            Result = result;
            StatusCode = statusCode;
            Body = body;
            TransportError = transportError;
        }

        public bool IsSuccess => Result == UnityWebRequest.Result.Success && StatusCode >= 200 && StatusCode < 300;
    }

    private async Task<HttpResponse> SendAsync(string method, string url, string jsonBody, bool returnRows)
    {
        using (var request = new UnityWebRequest(url, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            if (jsonBody != null)
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));

            request.SetRequestHeader("apikey", config.AnonKey);
            // Legacy anon keys are JWTs and go in Authorization too; new sb_publishable_ keys are not JWTs
            // and must only be sent as apikey.
            if (config.KeyIsJwt)
                request.SetRequestHeader("Authorization", "Bearer " + config.AnonKey);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            if (returnRows)
                request.SetRequestHeader("Prefer", "return=representation");
            request.timeout = config.RequestTimeoutSeconds;

            await SendWebRequest(request);

            return new HttpResponse(request.result, request.responseCode,
                request.downloadHandler != null ? request.downloadHandler.text : null, request.error);
        }
    }

    private static Task SendWebRequest(UnityWebRequest request)
    {
        var done = new TaskCompletionSource<bool>();
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        if (operation.isDone)
            done.TrySetResult(true);
        else
            operation.completed += _ => done.TrySetResult(true);
        return done.Task;
    }

    // ---------------------------------------------------------------- errors

    [Serializable]
    private class SupabaseErrorBody
    {
        public string code;
        public string message;
        public string hint;
    }

    private static bool IsUniqueViolation(HttpResponse response)
    {
        if (response.StatusCode != 409) return false;
        SupabaseErrorBody error = ParseError(response.Body);
        return error == null || error.code == UniqueViolation;
    }

    private static SubmitScoreResult FailSubmit(HttpResponse response)
    {
        LeaderboardResult failure = ToFailure(response);
        return SubmitScoreResult.Fail(failure.Error, failure.Message, failure.StatusCode);
    }

    private static LeaderboardResult ToFailure(HttpResponse response)
    {
        switch (response.Result)
        {
            case UnityWebRequest.Result.ConnectionError:
                bool timedOut = response.TransportError != null &&
                                response.TransportError.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                response.TransportError.IndexOf("out", StringComparison.OrdinalIgnoreCase) >= 0;
                return timedOut
                    ? Failure(LeaderboardError.Timeout, "Supabase did not respond in time.", 0)
                    : Failure(LeaderboardError.NoConnection, "Could not reach Supabase. Check the internet connection.", 0);

            case UnityWebRequest.Result.DataProcessingError:
                return Failure(LeaderboardError.InvalidResponse, "Supabase response could not be processed.", response.StatusCode);
        }

        long status = response.StatusCode;
        SupabaseErrorBody body = ParseError(response.Body);
        string detail = body != null && !string.IsNullOrEmpty(body.message) ? body.message : response.TransportError;

        if (status == 401 || status == 403)
            return Failure(LeaderboardError.Unauthorized, "Access denied. Check the anon key and the table's RLS policies. " + detail, status);
        if (status == 404)
            return Failure(LeaderboardError.NotFound, "Leaderboard table not found. " + detail, status);
        if (status == 409)
            return Failure(LeaderboardError.Conflict, "Conflicting leaderboard write. " + detail, status);
        if (status >= 500 || status == 0)
            return Failure(LeaderboardError.ServerUnavailable, "Supabase is unavailable right now. " + detail, status);

        return Failure(LeaderboardError.HttpError, $"Leaderboard request failed. {detail}", status);
    }

    private static LeaderboardResult Failure(LeaderboardError error, string message, long status)
    {
        return SubmitScoreResult.Fail(error, message?.Trim(), status);
    }

    // ---------------------------------------------------------------- JSON

    [Serializable]
    private class EntryList
    {
        public List<LeaderboardEntry> items;
    }

    [Serializable]
    private class InsertBody
    {
        public string nickname;
        public int high_score;
    }

    [Serializable]
    private class UpdateBody
    {
        public int high_score;
        public string updated_at;
    }

    /// <summary>Parses a PostgREST JSON array. Returns null if the body is not a valid array.</summary>
    private static List<LeaderboardEntry> ParseEntries(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        string trimmed = json.Trim();
        if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]")) return null;

        try
        {
            EntryList list = JsonUtility.FromJson<EntryList>("{\"items\":" + trimmed + "}");
            if (list == null) return null;

            List<LeaderboardEntry> items = list.items ?? new List<LeaderboardEntry>();
            items.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.nickname));
            return items;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static SupabaseErrorBody ParseError(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return null;
        try
        {
            return JsonUtility.FromJson<SupabaseErrorBody>(json);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- helpers

    private string ConfigError()
    {
        if (config == null)
            return "Supabase config asset not found (expected Resources/" + SupabaseConfig.DefaultResourcePath + ").";
        if (!config.IsConfigured)
            return "Supabase project URL or anon key is not set.";
        if (config.LooksLikeServiceRoleKey)
            return "Refusing to use a service_role / secret key in the client. Use the public anon key.";
        return null;
    }

    private string TableUrl()
    {
        return config.RestUrl + config.TableFor(type);
    }

    /// <summary>Escapes LIKE/ILIKE pattern characters so the value only matches itself.</summary>
    private static string LikeLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    private static string Escape(string value)
    {
        return Uri.EscapeDataString(value);
    }
}
