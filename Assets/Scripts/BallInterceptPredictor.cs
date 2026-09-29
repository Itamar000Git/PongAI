using UnityEngine;

/// <summary>
/// Pure geometry: where will a ball moving in a straight line reach a given X,
/// bouncing off the top and bottom walls on the way? No scene access, no state,
/// so it can be reused by any controller (and later by the style tracker / Q-learning).
/// </summary>
public static class BallInterceptPredictor
{
    public struct Prediction
    {
        /// <summary>False if the ball is not moving toward targetX (Y then holds the ball's current Y).</summary>
        public bool Valid;
        /// <summary>Predicted ball-center Y when it reaches targetX.</summary>
        public float InterceptY;
        /// <summary>Seconds until the ball reaches targetX at its current speed.</summary>
        public float TimeToIntercept;
        /// <summary>Number of top/bottom wall bounces before it gets there.</summary>
        public int WallBounces;
    }

    /// <param name="position">Ball center.</param>
    /// <param name="velocity">Ball velocity (units per second).</param>
    /// <param name="targetX">X of the line to intercept (e.g. the paddle's front face minus the ball radius).</param>
    /// <param name="minY">Lowest Y the ball center can reach (bottom wall + ball radius).</param>
    /// <param name="maxY">Highest Y the ball center can reach (top wall - ball radius).</param>
    public static Prediction Predict(Vector2 position, Vector2 velocity, float targetX, float minY, float maxY)
    {
        var result = new Prediction { InterceptY = position.y };

        float dx = targetX - position.x;
        if (Mathf.Abs(velocity.x) < 0.0001f || Mathf.Sign(dx) != Mathf.Sign(velocity.x))
            return result;

        float time = dx / velocity.x;
        float height = maxY - minY;
        result.Valid = true;
        result.TimeToIntercept = time;

        if (height <= 0.0001f)
        {
            result.InterceptY = Mathf.Clamp(position.y + velocity.y * time, minY, maxY);
            return result;
        }

        // "Unfold" the court: a wall reflection is the same as continuing straight into a mirrored copy
        // of the court. Travel freely, then fold the result back into [minY, maxY].
        float unfolded = position.y - minY + velocity.y * time;
        float period = 2f * height;
        float folded = Mathf.Repeat(unfolded, period);
        result.InterceptY = minY + (folded <= height ? folded : period - folded);
        result.WallBounces = Mathf.Abs(Mathf.FloorToInt(unfolded / height));
        return result;
    }
}
