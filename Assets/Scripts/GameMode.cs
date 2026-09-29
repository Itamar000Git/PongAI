/// <summary>
/// Which opponent the Game scene runs. Chosen on the main menu and kept in GameSession.
/// Classic: the trained PPO PongAgent (ONNX). Learning: the AdaptivePaddleController.
/// </summary>
public enum GameMode
{
    Classic,
    Learning
}
