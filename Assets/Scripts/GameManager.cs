using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public enum GoalSide
{
    Left,
    Right
}

public enum MatchState
{
    Countdown,  // 3-2-1-GO before a match: frozen, no pause, no scoring
    Playing,
    Paused,     // frozen, pause menu open
    GameOver
}

/// <summary>
/// Central owner of the match: score, lives, match state
/// (Countdown / Playing / Paused / GameOver) and the reset / restart flow.
/// Also resets and relaunches the ball after each goal.
/// Classic and AGAINST LEARNING AI share every rule here; they only differ in the right-paddle
/// opponent (chosen by PongModeController) and the leaderboard table the final score goes to.
/// </summary>
public class GameManager : MonoBehaviour
{
    [SerializeField] private BallController ball;
    [SerializeField] private float relaunchDelay = 1f;

    [Tooltip("Human goals against the AI. This is the Classic Mode leaderboard score.")]
    [SerializeField] private int playerScore;
    [Tooltip("AI goals against the human.")]
    [SerializeField] private int enemyScore;

    [Header("Classic Mode")]
    [SerializeField] private int startingLives = 5;
    [SerializeField] private int playerLives = 5;
    [Tooltip("Lives are ignored while training (the training opponent is not a human).")]
    [SerializeField] private PongModeController modeController;

    [Header("Countdown")]
    [SerializeField, Min(0f)] private float countdownStepSeconds = 1f;

    [Header("Paddles")]
    [SerializeField] private PlayerPaddleController playerPaddle;
    [SerializeField] private Rigidbody2D leftPaddleBody;
    [SerializeField] private Rigidbody2D rightPaddleBody;

    [Header("UI")]
    [FormerlySerializedAs("playerScoreText")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text livesText;
    [SerializeField] private GameOverScreen gameOverScreen;
    [SerializeField] private PauseMenuScreen pauseMenuScreen;
    [SerializeField] private CountdownView countdownView;
    [SerializeField] private PongUITheme theme;
    [SerializeField] private int lowLivesWarning = 3;

    [Header("ML-Agents")]
    [Tooltip("Agent controlling the enemy (right) paddle. Optional.")]
    [SerializeField] private PongAgent enemyAgent;

    [Header("Learning Mode")]
    [Tooltip("Right-paddle opponent in AGAINST LEARNING AI. Reset at the start of every Learning match.")]
    [SerializeField] private AdaptivePaddleController learningOpponent;
    [Tooltip("Observes the human's returns in AGAINST LEARNING AI. Reset at the start of every Learning match.")]
    [SerializeField] private PlayerStyleTracker playerStyleTracker;
    [Tooltip("Online Q-learning layer of the Learning AI. Reset at the start of every Learning match.")]
    [SerializeField] private OnlineQLearning qLearning;

    private MatchState state = MatchState.Countdown;
    private double countdownStartedAt;   // real time (the match is frozen during the countdown)
    private int countdownNumberShown;
    private bool isResetting;
    private bool isFrozen;
    private float normalTimeScale = 1f;
    private Vector2 leftPaddleStart;
    private Vector2 rightPaddleStart;

    // Leaderboard submission: at most one per match (PLAY AGAIN starts a new match).
    private int matchNumber;
    private bool scoreSubmitted;
    private bool scoreSubmitInProgress;
    private bool lastSubmitFailed;

    /// <summary>Raised whenever the match state changes (HUD buttons listen to this).</summary>
    public event Action<MatchState> StateChanged;

    /// <summary>Raised once for every goal that counts (side, and whether the human lost a life). Visual/audio hooks only.</summary>
    public event Action<GoalSide, bool> GoalCounted;

    public MatchState State => state;
    public int PlayerScore => playerScore;
    public int EnemyScore => enemyScore;
    public int PlayerLives => playerLives;
    public int StartingLives => startingLives;
    public bool IsGameOver => state == MatchState.GameOver;
    public bool IsPaused => state == MatchState.Paused;
    public bool IsCountingDown => state == MatchState.Countdown;
    /// <summary>Pausing is only for an active Classic match (not during countdown, at Game Over or while training).</summary>
    public bool CanPause => state == MatchState.Playing && ClassicRulesActive;
    public bool CanRetryScoreSubmission => IsGameOver && lastSubmitFailed && !scoreSubmitInProgress;

    /// <summary>The mode this match is played in (Classic when there is no mode controller or session).</summary>
    public GameMode Mode => modeController != null ? modeController.ActiveGameMode : GameMode.Classic;
    public bool IsLearningMode => Mode == GameMode.Learning;
    /// <summary>Where this match's final score is submitted. Never crosses modes.</summary>
    public LeaderboardType SubmissionLeaderboard => LeaderboardTypes.ForMode(Mode);

    private bool ClassicRulesActive => modeController == null || !modeController.TrainingMode;

    private void Awake()
    {
        if (leftPaddleBody != null) leftPaddleStart = leftPaddleBody.position;
        if (rightPaddleBody != null) rightPaddleStart = rightPaddleBody.position;
    }

    private void Start()
    {
        playerScore = 0;
        enemyScore = 0;
        playerLives = startingLives;
        UpdateHUD();
        BeginLearningMatch();

        if (ClassicRulesActive)
            StartCountdown();
        else
            SetState(MatchState.Playing);   // training: the ball serves itself, no countdown
    }

    /// <summary>
    /// Called by a GoalZone when the ball enters it.
    /// The left goal is the player's, so the enemy scores there (and vice versa).
    /// </summary>
    public void GoalScored(GoalSide side)
    {
        // Goals only count while playing, and not while the ball is being reset.
        if (isResetting || state != MatchState.Playing)
            return;

        bool lifeLost = false;
        if (side == GoalSide.Left)
        {
            enemyScore++;
            if (ClassicRulesActive)
            {
                playerLives = Mathf.Max(0, playerLives - 1);
                lifeLost = true;
            }
        }
        else
        {
            playerScore++;
        }

        if (AudioManager.Instance != null)
        {
            if (side == GoalSide.Right) AudioManager.Instance.PlayPositiveScore();
            else if (lifeLost) AudioManager.Instance.PlayLifeLost();
        }

        UpdateHUD();
        GoalCounted?.Invoke(side, lifeLost);
        Debug.Log($"Goal on {side} side! Score — Player: {playerScore} | Enemy: {enemyScore} | Lives: {playerLives}");

        // Rewards only for the PPO agent; it is switched off in Learning mode.
        if (enemyAgent != null && !IsLearningMode)
            enemyAgent.OnGoalScored(side == GoalSide.Left);

        if (ClassicRulesActive && playerLives == 0)
        {
            GameOver();
            return;
        }

        // Normal goal-to-goal serve keeps the short relaunch delay (no countdown).
        StartCoroutine(ResetAndRelaunch());
    }

    /// <summary>Starts a fresh match with the same nickname and mode (PLAY AGAIN / RESTART MATCH).</summary>
    public void RestartMatch()
    {
        StopAllCoroutines();
        Unfreeze();
        if (pauseMenuScreen != null) pauseMenuScreen.Hide();

        playerScore = 0;
        enemyScore = 0;
        playerLives = startingLives;
        isResetting = false;

        matchNumber++;
        scoreSubmitted = false;
        scoreSubmitInProgress = false;
        lastSubmitFailed = false;

        ResetPaddle(leftPaddleBody, leftPaddleStart);
        ResetPaddle(rightPaddleBody, rightPaddleStart);
        if (playerPaddle != null) playerPaddle.enabled = true;

        if (gameOverScreen != null) gameOverScreen.Hide();
        UpdateHUD();
        BeginLearningMatch();
        Debug.Log(IsLearningMode ? "Learning Mode restarted." : "Classic Mode restarted.");

        if (ClassicRulesActive)
        {
            StartCountdown();
        }
        else
        {
            SetState(MatchState.Playing);
            StartCoroutine(ResetAndRelaunch());
        }
    }

    /// <summary>
    /// Call before loading another scene. Shuts the match down while every object still exists:
    /// ML-Agents collects a final observation when the agent is disabled, and during a scene unload
    /// the ball may already be destroyed by then.
    /// </summary>
    public void PrepareForSceneExit()
    {
        StopAllCoroutines();
        Unfreeze();
        if (pauseMenuScreen != null) pauseMenuScreen.Hide();
        if (countdownView != null) countdownView.Hide();
        isResetting = false;
        if (enemyAgent != null) enemyAgent.enabled = false;
        if (learningOpponent != null) learningOpponent.enabled = false;
        if (playerStyleTracker != null) playerStyleTracker.enabled = false;
        if (qLearning != null) qLearning.enabled = false;
    }

    /// <summary>
    /// The one Learning reset hook. Everything the Learning AI knows about this match starts fresh:
    /// first match, PLAY AGAIN and RESTART MATCH (never resume, pause, settings or goals).
    /// </summary>
    private void BeginLearningMatch()
    {
        if (!IsLearningMode) return;

        if (learningOpponent != null) learningOpponent.ResetLearningState();
        if (playerStyleTracker != null) playerStyleTracker.ResetTracking();
        if (qLearning != null) qLearning.ResetLearning();
    }

    // ------------------------------------------------------------------ pause

    /// <summary>ESC entry point: pauses while playing, resumes while paused.</summary>
    public void TogglePause()
    {
        if (state == MatchState.Paused) ResumeGame();
        else PauseGame();
    }

    /// <summary>
    /// The one pause method (ESC and the HUD PAUSE button both end here). Freezes the match with
    /// timeScale 0: physics, the agent's decisions/actions and the relaunch delay all stop.
    /// </summary>
    public void PauseGame()
    {
        if (!CanPause) return;

        Freeze();
        SetState(MatchState.Paused);
        if (pauseMenuScreen != null) pauseMenuScreen.Show();
    }

    /// <summary>Continues exactly where the match stopped (no countdown).</summary>
    public void ResumeGame()
    {
        if (state != MatchState.Paused) return;

        if (pauseMenuScreen != null) pauseMenuScreen.Hide();
        Unfreeze();
        SetState(MatchState.Playing);
    }

    // ------------------------------------------------------------------ countdown

    private void StartCountdown()
    {
        StopAllCoroutines();
        isResetting = false;
        ball.ResetToCenter();
        Freeze();
        SetState(MatchState.Countdown);

        countdownStartedAt = Time.realtimeSinceStartupAsDouble;
        countdownNumberShown = 3;
        if (countdownView != null) countdownView.ShowNumber(3);
    }

    /// <summary>
    /// 3 - 2 - 1 on real time while the match is frozen, then GO! and an immediate serve.
    /// Driven from Update (which runs every frame even at timeScale 0) rather than a coroutine,
    /// so the countdown can never be left waiting.
    /// </summary>
    private void Update()
    {
        if (state != MatchState.Countdown) return;

        // The ball serves itself in its own Start on scene load; keep it parked during the countdown.
        ball.ResetToCenter();

        double elapsed = Time.realtimeSinceStartupAsDouble - countdownStartedAt;
        float step = Mathf.Max(0.01f, countdownStepSeconds);
        if (elapsed < 3f * step)
        {
            int number = 3 - (int)(elapsed / step);
            if (number != countdownNumberShown)
            {
                countdownNumberShown = number;
                if (countdownView != null) countdownView.ShowNumber(number);
            }
            return;
        }

        if (countdownView != null) countdownView.ShowGo();
        Unfreeze();
        SetState(MatchState.Playing);
        ball.Launch();
    }

    // ------------------------------------------------------------------ freeze helpers

    private void Freeze()
    {
        if (isFrozen) return;

        isFrozen = true;
        normalTimeScale = Time.timeScale;
        Time.timeScale = 0f;
    }

    private void Unfreeze()
    {
        if (!isFrozen) return;

        isFrozen = false;
        Time.timeScale = normalTimeScale;
    }

    private void SetState(MatchState newState)
    {
        state = newState;
        StateChanged?.Invoke(state);
    }

    private void OnDestroy()
    {
        // Never leave the game frozen if this scene is unloaded while paused or counting down.
        if (isFrozen) Time.timeScale = normalTimeScale;
    }

    // ------------------------------------------------------------------ game over

    private void GameOver()
    {
        Unfreeze();
        SetState(MatchState.GameOver);

        // Stop the match: no pending relaunch, ball parked at center, player input off.
        StopAllCoroutines();
        isResetting = false;
        ball.ResetToCenter();
        if (playerPaddle != null) playerPaddle.enabled = false;

        Debug.Log($"GAME OVER — Final score: {playerScore}");
        if (AudioManager.Instance != null) AudioManager.Instance.PlayGameOver();
        if (gameOverScreen != null) gameOverScreen.Show(GameSession.NicknameOrDefault, playerScore);

        SubmitFinalScore();
    }

    /// <summary>Uploads this match's final score once, in the background (Game Over is already shown).</summary>
    private void SubmitFinalScore()
    {
        if (scoreSubmitted || !ClassicRulesActive) return;

        scoreSubmitted = true;
        SendScoreAsync(GameSession.NicknameOrDefault, playerScore, matchNumber);
    }

    /// <summary>RETRY SAVE on the Game Over screen, only after a failed upload of this match.</summary>
    public void RetryScoreSubmission()
    {
        if (!CanRetryScoreSubmission) return;

        SendScoreAsync(GameSession.NicknameOrDefault, playerScore, matchNumber);
    }

    private async void SendScoreAsync(string nickname, int score, int match)
    {
        scoreSubmitInProgress = true;
        lastSubmitFailed = false;
        if (gameOverScreen != null) gameOverScreen.ShowScoreSaving();

        // Classic -> leaderboard, Learning -> learning_leaderboard.
        SubmitScoreResult result = await LeaderboardService.CreateDefault(SubmissionLeaderboard).SubmitScoreAsync(nickname, score);

        // The scene may have been left, or PLAY AGAIN pressed, while the upload was running.
        if (this == null || match != matchNumber) return;

        scoreSubmitInProgress = false;
        lastSubmitFailed = !result.Success;
        if (!result.Success) Debug.Log($"Leaderboard: score not saved ({result.Error}).");
        if (gameOverScreen != null) gameOverScreen.ShowScoreResult(result);
    }

    // ------------------------------------------------------------------ helpers

    private static void ResetPaddle(Rigidbody2D body, Vector2 position)
    {
        if (body == null) return;

        body.position = position;
        body.linearVelocity = Vector2.zero;
        body.transform.position = new Vector3(position.x, position.y, body.transform.position.z);
    }

    private void UpdateHUD()
    {
        if (scoreText != null)
            scoreText.text = playerScore.ToString();

        if (livesText != null)
        {
            livesText.text = $"{playerLives} / {startingLives}";
            if (theme != null)
                livesText.color = playerLives <= lowLivesWarning ? theme.secondary : theme.highlight;
        }
    }

    private IEnumerator ResetAndRelaunch()
    {
        isResetting = true;
        ball.ResetToCenter();

        yield return new WaitForSeconds(relaunchDelay);

        ball.Launch();
        isResetting = false;
    }
}
