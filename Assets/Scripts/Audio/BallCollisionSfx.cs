using UnityEngine;

/// <summary>
/// Plays the paddle / wall hit sounds for the ball. Sound only: it reads collisions and never
/// touches the ball's physics (that stays in BallController).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BallCollisionSfx : MonoBehaviour
{
    [SerializeField] private Collider2D[] paddles;
    [SerializeField] private Collider2D[] walls;
    [Tooltip("Ignores a re-contact with the same collider within this time, so one hit = one sound.")]
    [SerializeField, Min(0f)] private float sameColliderCooldown = 0.08f;

    private Collider2D lastCollider;
    private float lastHitTime = float.NegativeInfinity;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null) return;

        Collider2D other = collision.collider;
        if (other == lastCollider && Time.time - lastHitTime < sameColliderCooldown) return;

        if (Contains(paddles, other))
            audio.PlayBallHit();
        else if (Contains(walls, other))
            audio.PlayWallHit();
        else
            return;

        lastCollider = other;
        lastHitTime = Time.time;
    }

    private static bool Contains(Collider2D[] colliders, Collider2D target)
    {
        if (colliders == null) return false;

        foreach (Collider2D c in colliders)
        {
            if (c == target) return true;
        }
        return false;
    }
}
