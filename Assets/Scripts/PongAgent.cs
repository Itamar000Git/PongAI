using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ML-Agents controller for the right paddle.
/// Observes the ball and both paddles, and chooses one discrete action per step:
/// 0 = stay, 1 = move up, 2 = move down.
/// Movement is applied through Rigidbody2D in FixedUpdate and clamped to the play area.
/// Rewards: hitting the ball, scoring, and conceding. Each goal ends the episode.
/// </summary>
public class PongAgent : Agent
{
    private const int ActionStay = 0;
    private const int ActionUp = 1;
    private const int ActionDown = 2;

    [Header("References")]
    [SerializeField] private Rigidbody2D paddleRb;
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private Transform opponentPaddle;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float minY = -3.4f;
    [SerializeField] private float maxY = 3.4f;

    [Header("Observation Normalization")]
    [Tooltip("Half the court width (goal lines are at about x = ±9.5).")]
    [SerializeField] private float courtHalfWidth = 9.5f;
    [Tooltip("Half the court height (walls are at about y = ±4.5).")]
    [SerializeField] private float courtHalfHeight = 4.5f;
    [Tooltip("Highest ball speed (BallController maxSpeed).")]
    [SerializeField] private float maxBallSpeed = 11f;

    [Header("Rewards")]
    [SerializeField] private float hitReward = 0.2f;
    [SerializeField] private float scoreReward = 1f;
    [SerializeField] private float concedeReward = -1f;

    private int moveDirection;

    public override void Initialize()
    {
        if (paddleRb == null)
            paddleRb = GetComponent<Rigidbody2D>();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector2 ballPosition = ball.position;
        Vector2 ballVelocity = ball.linearVelocity;

        // 6 observations, each normalized to roughly [-1, 1].
        sensor.AddObservation(Normalize(ballPosition.x, courtHalfWidth));
        sensor.AddObservation(Normalize(ballPosition.y, courtHalfHeight));
        sensor.AddObservation(Normalize(ballVelocity.x, maxBallSpeed));
        sensor.AddObservation(Normalize(ballVelocity.y, maxBallSpeed));
        sensor.AddObservation(Normalize(paddleRb.position.y, courtHalfHeight));
        sensor.AddObservation(Normalize(opponentPaddle.position.y, courtHalfHeight));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        switch (actions.DiscreteActions[0])
        {
            case ActionUp:
                moveDirection = 1;
                break;
            case ActionDown:
                moveDirection = -1;
                break;
            default:
                moveDirection = 0;
                break;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Simple ball-following fallback for testing without a trained model.
        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
        float offset = ball.position.y - paddleRb.position.y;

        if (offset > 0.15f)
            discreteActions[0] = ActionUp;
        else if (offset < -0.15f)
            discreteActions[0] = ActionDown;
        else
            discreteActions[0] = ActionStay;
    }

    /// <summary>
    /// Called by GameManager after a goal is counted. Rewards the outcome and ends the episode.
    /// Ball reset/relaunch stays in GameManager.
    /// </summary>
    public void OnGoalScored(bool agentScored)
    {
        AddReward(agentScored ? scoreReward : concedeReward);
        EndEpisode();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Fires once when contact begins (not while touching), and only for the ball.
        if (collision.rigidbody == ball)
            AddReward(hitReward);
    }

    private void FixedUpdate()
    {
        if (moveDirection == 0)
            return;

        Vector2 position = paddleRb.position;
        float targetY = Mathf.Clamp(position.y + moveDirection * moveSpeed * Time.fixedDeltaTime, minY, maxY);

        // X is kept fixed so the paddle can only move vertically.
        paddleRb.MovePosition(new Vector2(position.x, targetY));
    }

    private static float Normalize(float value, float range)
    {
        return Mathf.Clamp(value / range, -1f, 1f);
    }
}
