using UnityEngine;

/// <summary>
/// Trigger zone behind a paddle. Notifies the GameManager when the ball enters it.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class GoalZone : MonoBehaviour
{
    [SerializeField] private GoalSide side;
    [SerializeField] private GameManager gameManager;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<BallController>() != null)
            gameManager.GoalScored(side);
    }
}
