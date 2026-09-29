using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Switches the game between Training Mode (rule-based opponent on the left paddle)
/// and Play Mode (human W/S control on the left paddle).
/// In Play Mode the right paddle is controlled by exactly one opponent, chosen by GameSession's GameMode:
/// Classic = the trained PongAgent (ONNX), Learning = the AdaptivePaddleController.
/// Runs early so the unused controller is off before anything else starts.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PongModeController : MonoBehaviour
{
    [Tooltip("On: left paddle is a rule-based training opponent. Off: left paddle is human-controlled.")]
    [SerializeField] private bool trainingMode = true;

    [Header("Left Paddle")]
    [SerializeField] private PlayerPaddleController playerController;
    [SerializeField] private RuleBasedPaddleController trainingOpponent;

    [Header("Right Paddle")]
    [SerializeField] private PongAgent agent;
    [SerializeField] private DecisionRequester agentDecisionRequester;
    [SerializeField] private RuleBasedPaddleController agentPaddleRuleController;
    [Tooltip("AGAINST LEARNING AI opponent. Only enabled in GameMode.Learning.")]
    [SerializeField] private AdaptivePaddleController learningOpponent;

    [Header("Learning Mode Analysis")]
    [Tooltip("Observes the human's returns. Only enabled in GameMode.Learning.")]
    [SerializeField] private PlayerStyleTracker playerStyleTracker;
    [Tooltip("Online Q-learning layer. Only enabled in GameMode.Learning.")]
    [SerializeField] private OnlineQLearning qLearning;

    public bool TrainingMode => trainingMode;

    /// <summary>True when the right paddle is the Learning AI (never while training).</summary>
    public bool LearningMode => !trainingMode && GameSession.CurrentModeOrDefault == GameMode.Learning;

    public GameMode ActiveGameMode => LearningMode ? GameMode.Learning : GameMode.Classic;

    private void Awake()
    {
        ApplyMode();
    }

    public void SetTrainingMode(bool enabled)
    {
        trainingMode = enabled;
        ApplyMode();
    }

    private void ApplyMode()
    {
        if (playerController != null)
            playerController.enabled = !trainingMode;
        if (trainingOpponent != null)
            trainingOpponent.enabled = trainingMode;

        bool learning = LearningMode;

        // Classic (and training): the right paddle is the agent, exactly as before.
        // Learning: the agent is switched off and the adaptive controller drives the paddle instead.
        if (agent != null)
            agent.enabled = !learning;
        if (agentDecisionRequester != null)
            agentDecisionRequester.enabled = !learning;
        if (agentPaddleRuleController != null)
            agentPaddleRuleController.enabled = false;
        if (learningOpponent != null)
            learningOpponent.enabled = learning;
        if (playerStyleTracker != null)
            playerStyleTracker.enabled = learning;
        if (qLearning != null)
            qLearning.enabled = learning;
    }
}
