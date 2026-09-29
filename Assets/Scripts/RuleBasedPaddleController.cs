using UnityEngine;

/// <summary>
/// Simple rule-based opponent: moves the paddle vertically toward the ball's Y position
/// at a limited speed, with a small dead zone so it doesn't jitter.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class RuleBasedPaddleController : MonoBehaviour
{
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float deadZone = 0.15f;
    [SerializeField] private float minY = -3.4f;
    [SerializeField] private float maxY = 3.4f;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        if (ball == null)
            return;

        Vector2 position = rb.position;
        float offset = ball.position.y - position.y;

        // Stay still while the ball is within the dead zone.
        if (Mathf.Abs(offset) <= deadZone)
            return;

        // Move toward the ball at a limited speed, without overshooting it.
        float step = moveSpeed * Time.fixedDeltaTime;
        float targetY = position.y + Mathf.Clamp(offset, -step, step);
        targetY = Mathf.Clamp(targetY, minY, maxY);

        // X is kept fixed so the paddle can only move vertically.
        rb.MovePosition(new Vector2(position.x, targetY));
    }
}
