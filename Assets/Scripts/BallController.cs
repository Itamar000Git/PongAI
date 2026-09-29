using UnityEngine;

/// <summary>
/// Launches the ball in a random left/right direction on start and keeps its
/// speed constant so it never gradually slows down after collisions.
/// The speed ramps up during a rally and resets after each goal.
/// Paddle hits set the outgoing angle from where the ball struck the paddle.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class BallController : MonoBehaviour
{
    [Tooltip("Starting speed of every rally.")]
    [SerializeField] private float speed = 7f;
    [SerializeField] private float maxVerticalLaunch = 0.5f;

    [Header("Rally Speed-Up")]
    [SerializeField] private float speedIncreaseInterval = 5f;
    [SerializeField] private float speedIncreaseAmount = 0.5f;
    [SerializeField] private float maxSpeed = 11f;

    [Header("Paddle Bounce")]
    [SerializeField] private Collider2D[] paddles;
    [Tooltip("Outgoing angle for a hit at the very top/bottom of a paddle. Capped below 90 so the ball always keeps real horizontal speed.")]
    [SerializeField, Range(0f, 75f)] private float maxBounceAngle = 55f;

    private Rigidbody2D rb;
    private float currentSpeed;
    private float rallyTimer;

    public float CurrentSpeed => currentSpeed;

    /// <summary>
    /// Raised after every paddle bounce with the paddle that was hit, the normalized hit position
    /// (-1 bottom, 0 center, +1 top) and the outgoing velocity. Observers only (e.g. PlayerStyleTracker);
    /// it never changes the bounce itself.
    /// </summary>
    public event System.Action<Collider2D, float, Vector2> PaddleBounced;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        currentSpeed = speed;
    }

    private void Start()
    {
        Launch();
    }

    private void FixedUpdate()
    {
        Vector2 velocity = rb.linearVelocity;
        if (velocity.sqrMagnitude <= 0.0001f)
            return;

        // Only count time while the ball is actually in play (not during the post-goal pause).
        rallyTimer += Time.fixedDeltaTime;
        while (rallyTimer >= speedIncreaseInterval)
        {
            rallyTimer -= speedIncreaseInterval;
            currentSpeed = Mathf.Min(currentSpeed + speedIncreaseAmount, maxSpeed);
        }

        // Re-apply the current speed so collisions can change direction but not speed.
        rb.linearVelocity = velocity.normalized * currentSpeed;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsPaddle(collision.collider))
            BounceOffPaddle(collision.collider);
    }

    /// <summary>
    /// Launches the ball: always a full horizontal component, randomly left or right,
    /// with a slight random vertical component.
    /// </summary>
    public void Launch()
    {
        float x = Random.value < 0.5f ? -1f : 1f;
        float y = Random.Range(-maxVerticalLaunch, maxVerticalLaunch);

        rb.linearVelocity = new Vector2(x, y).normalized * currentSpeed;
    }

    /// <summary>
    /// Stops the ball, moves it back to the center of the field,
    /// and resets the rally speed to the starting speed.
    /// </summary>
    public void ResetToCenter()
    {
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.position = Vector2.zero;
        transform.position = Vector3.zero;

        currentSpeed = speed;
        rallyTimer = 0f;
    }

    private bool IsPaddle(Collider2D other)
    {
        if (paddles == null)
            return false;

        foreach (Collider2D paddle in paddles)
        {
            if (paddle == other)
                return true;
        }
        return false;
    }

    private void BounceOffPaddle(Collider2D paddle)
    {
        Bounds bounds = paddle.bounds;

        // -1 = bottom edge, 0 = center, +1 = top edge.
        float hit = Mathf.Clamp((rb.position.y - bounds.center.y) / bounds.extents.y, -1f, 1f);
        float angle = hit * maxBounceAngle * Mathf.Deg2Rad;

        // Always travel away from the paddle that was hit.
        float directionX = rb.position.x < bounds.center.x ? -1f : 1f;

        rb.linearVelocity = new Vector2(directionX * Mathf.Cos(angle), Mathf.Sin(angle)).normalized * currentSpeed;

        PaddleBounced?.Invoke(paddle, hit, rb.linearVelocity);
    }
}
