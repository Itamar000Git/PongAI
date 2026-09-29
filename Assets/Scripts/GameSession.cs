using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight data that must survive scene loads (nickname and selected game mode).
/// Exactly one instance exists; it is created on demand and kept with DontDestroyOnLoad.
/// </summary>
public class GameSession : MonoBehaviour
{
    public const string DefaultNickname = "PLAYER";

    public static GameSession Instance { get; private set; }

    [SerializeField] private string currentNickname;
    [SerializeField] private GameMode currentGameMode = GameMode.Classic;

    // Which leaderboard tab the Leaderboard scene should open on next (set by whoever opens it).
    private static LeaderboardType requestedLeaderboard = LeaderboardType.Classic;

    // Modes whose leaderboard confirmed currentNickname as available this session (each mode has its own table).
    private readonly HashSet<GameMode> nicknameClaimedFor = new HashSet<GameMode>();

    /// <summary>The nickname entered for this session, or null/empty if none was set.</summary>
    public string CurrentNickname => currentNickname;

    /// <summary>The mode chosen on the main menu. Classic unless AGAINST LEARNING AI was picked.</summary>
    public GameMode CurrentGameMode => currentGameMode;

    /// <summary>Safe to call from any scene: returns the session nickname, or "PLAYER" if there is none.</summary>
    public static string NicknameOrDefault =>
        Instance != null && !string.IsNullOrEmpty(Instance.currentNickname) ? Instance.currentNickname : DefaultNickname;

    /// <summary>Safe to call from any scene: the selected mode, or Classic if there is no session (e.g. Game scene opened directly).</summary>
    public static GameMode CurrentModeOrDefault => Instance != null ? Instance.currentGameMode : GameMode.Classic;

    /// <summary>Returns the existing session or creates it.</summary>
    public static GameSession GetOrCreate()
    {
        if (Instance != null) return Instance;

        GameObject go = new GameObject(nameof(GameSession));
        return go.AddComponent<GameSession>();
    }

    /// <summary>Main menu -> Classic; Game Over -> the mode that was just played.</summary>
    public static void RequestLeaderboard(LeaderboardType type)
    {
        requestedLeaderboard = type;
    }

    /// <summary>The tab the Leaderboard scene should open on. Resets to Classic after it is read.</summary>
    public static LeaderboardType TakeRequestedLeaderboard()
    {
        LeaderboardType type = requestedLeaderboard;
        requestedLeaderboard = LeaderboardType.Classic;
        return type;
    }

    public void SetGameMode(GameMode mode)
    {
        currentGameMode = mode;
    }

    /// <summary>Stores the nickname that was confirmed available for the current mode's leaderboard.</summary>
    public void SetNickname(string nickname)
    {
        string cleaned = NicknameRules.Clean(nickname);
        if (cleaned != currentNickname) nicknameClaimedFor.Clear();

        currentNickname = cleaned;
        nicknameClaimedFor.Add(currentGameMode);
    }

    /// <summary>
    /// True if this nickname (case-insensitive) is the one the player already accepted this session
    /// (entered as new, or confirmed with YES for an existing one). Never persisted between app launches.
    /// </summary>
    public bool IsSessionNickname(string nickname)
    {
        return !string.IsNullOrEmpty(currentNickname)
               && string.Equals(currentNickname, NicknameRules.Clean(nickname), System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if this session already claimed exactly this nickname on the given mode's leaderboard.</summary>
    public bool HasClaimedNickname(string nickname, GameMode mode)
    {
        return !string.IsNullOrEmpty(currentNickname) && currentNickname == nickname && nicknameClaimedFor.Contains(mode);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
