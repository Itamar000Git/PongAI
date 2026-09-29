using UnityEngine;

/// <summary>
/// Keeps the ball's TrailRenderer clean: it only emits while the ball moves and is cleared when the
/// ball is teleported (reset to center after a goal), so no streak is drawn across the court.
/// Visual only; never touches the Rigidbody2D.
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
public class BallTrail : MonoBehaviour
{
    [Tooltip("A position jump larger than this in one frame counts as a reset/teleport.")]
    [SerializeField] private float teleportDistance = 1.0f;

    private TrailRenderer trail;
    private Rigidbody2D body;
    private Vector3 lastPosition;

    private void Awake()
    {
        trail = GetComponent<TrailRenderer>();
        body = GetComponent<Rigidbody2D>();
        lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        Vector3 position = transform.position;

        if ((position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance)
            trail.Clear();

        bool moving = body != null && body.linearVelocity.sqrMagnitude > 0.01f;
        if (trail.emitting != moving)
        {
            trail.emitting = moving;
            if (!moving) trail.Clear();
        }

        lastPosition = position;
    }
}
