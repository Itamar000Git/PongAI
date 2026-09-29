using UnityEngine;

/// <summary>
/// Moves the player's paddle. WHAT the player wants comes from PaddleInputReader (keyboard W/S, mouse height,
/// one gameplay touch); HOW the paddle moves is decided here, once, for every device:
///
///   input source -> PaddleIntent (axis or target Y) -> desired velocity -> the same speed / acceleration /
///   deceleration ramp -> Rigidbody2D.MovePosition, clamped to the play area.
///
/// A pointer or finger only ever sets a TARGET height; the paddle never jumps to it. The desired speed toward a
/// target is capped by moveSpeed and braked so the paddle stops at the target, so mouse and touch are never faster
/// than the keyboard. Movement is applied in FixedUpdate. The controller is disabled at Game Over and enabled again
/// by PLAY AGAIN / RESTART MATCH (which also resets the input state); the countdown and pause freeze it with
/// timeScale 0, exactly as before.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerPaddleController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6.4f;
    [Tooltip("How quickly the paddle speeds up toward moveSpeed while a key is held (units/s²).")]
    [SerializeField] private float acceleration = 26f;
    [Tooltip("How quickly the paddle slows down when keys are released or it reverses direction (units/s²).")]
    [SerializeField] private float deceleration = 30f;
    [SerializeField] private float minY = -3.4f;
    [SerializeField] private float maxY = 3.4f;

    [Header("Input")]
    [Tooltip("Reads keyboard, mouse and touch. Added automatically if missing.")]
    [SerializeField] private PaddleInputReader inputReader;
    [Tooltip("Mouse / touch: the paddle stops when it is this close to the target height (world units). Prevents jitter around the pointer.")]
    [SerializeField, Min(0f)] private float pointerDeadZone = 0.08f;
    [Tooltip("Every new countdown (match start, RESTART MATCH, PLAY AGAIN) starts with a clean input state. Found automatically if empty.")]
    [SerializeField] private GameManager gameManager;

    private Rigidbody2D rb;
    private PaddleIntent intent;
    private float verticalVelocity;

    /// <summary>Current vertical speed (world units/s, sign = direction). Never exceeds moveSpeed in magnitude.</summary>
    public float CurrentVelocity => verticalVelocity;
    public float MoveSpeed => moveSpeed;
    public PaddleIntent Intent => intent;
    /// <summary>Device that currently steers the paddle (Keyboard / Pointer / Touch, None until one is used).</summary>
    public PaddleInputMode InputMode => inputReader != null ? inputReader.ActiveMode : PaddleInputMode.None;
    public PaddleInputReader Reader => inputReader;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (inputReader == null) inputReader = GetComponent<PaddleInputReader>();
        if (inputReader == null) inputReader = gameObject.AddComponent<PaddleInputReader>();
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
    }

    private void OnEnable()
    {
        // Start from rest when (re)enabled, e.g. after Game Over -> Play Again, and forget every input state.
        ResetInputState();
        if (gameManager != null) gameManager.StateChanged += OnMatchStateChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.StateChanged -= OnMatchStateChanged;
    }

    /// <summary>A countdown always means a fresh match (also RESTART MATCH from the pause menu, which does not re-enable this component).</summary>
    private void OnMatchStateChanged(MatchState state)
    {
        if (state == MatchState.Countdown) ResetInputState();
    }

    private void ResetInputState()
    {
        intent = PaddleIntent.None;
        verticalVelocity = 0f;
        if (inputReader != null) inputReader.ResetState();
    }

    private void Update()
    {
        // Read input every frame so presses between physics steps aren't missed.
        intent = inputReader != null ? inputReader.Poll(minY, maxY) : PaddleIntent.None;
    }

    private void FixedUpdate()
    {
        Vector2 position = rb.position;
        float targetVelocity;
        bool speedingUp;

        switch (intent.Kind)
        {
            case PaddleIntentKind.Axis:
                // Keyboard: unchanged behaviour.
                targetVelocity = intent.Axis * moveSpeed;
                speedingUp = intent.Axis != 0f && (verticalVelocity == 0f || Mathf.Sign(intent.Axis) == Mathf.Sign(verticalVelocity));
                break;

            case PaddleIntentKind.TargetY:
                // Pointer / touch: chase the target with the same limits, braking so it stops at the target.
                targetVelocity = VelocityToward(intent.TargetY, position.y);
                speedingUp = targetVelocity != 0f
                             && (verticalVelocity == 0f || Mathf.Sign(targetVelocity) == Mathf.Sign(verticalVelocity))
                             && Mathf.Abs(targetVelocity) > Mathf.Abs(verticalVelocity);
                break;

            default:
                targetVelocity = 0f;
                speedingUp = false;
                break;
        }

        // Speed up with acceleration; slow down (release, braking or direction change) with deceleration.
        float rate = speedingUp ? acceleration : deceleration;
        verticalVelocity = Mathf.MoveTowards(verticalVelocity, targetVelocity, rate * Time.fixedDeltaTime);

        float targetY = position.y + verticalVelocity * Time.fixedDeltaTime;

        // Stop at the clamp limits instead of pushing against them.
        if (targetY <= minY || targetY >= maxY)
        {
            targetY = Mathf.Clamp(targetY, minY, maxY);
            verticalVelocity = 0f;
        }

        // X is kept fixed so the paddle can only move vertically.
        rb.MovePosition(new Vector2(position.x, targetY));
    }

    /// <summary>
    /// Desired velocity toward a target height: full moveSpeed while far away, then the highest speed from which the
    /// paddle can still brake (at 'deceleration') to a stop at the edge of the dead zone. Zero inside the dead zone.
    /// </summary>
    private float VelocityToward(float target, float currentY)
    {
        float error = target - currentY;
        float distance = Mathf.Abs(error);
        if (distance <= pointerDeadZone) return 0f;

        float speed = Mathf.Min(moveSpeed, Mathf.Sqrt(2f * deceleration * (distance - pointerDeadZone)));
        return Mathf.Sign(error) * speed;
    }
}
