using UnityEngine;

public class CourtSquare : MonoBehaviour
{
    [Header("Square Configuration")]
    [Tooltip("Enter 1, 2, 3, or 4 corresponding to this quadrant.")]
    public int squareID;

    private int bounceCount = 0;

    private static CourtSquare lastActiveSquare;

    private void OnCollisionEnter(Collision collision)
    {
        BallController ball = collision.collider.GetComponentInParent<BallController>();
        if (ball == null || !ball.HasGameplayAuthority) return;

        SquareRole squareRole = (SquareRole)squareID;
        if (!System.Enum.IsDefined(typeof(SquareRole), squareRole) || squareRole == SquareRole.Spectator)
        {
            Debug.LogError($"CourtSquare on {name} has an invalid squareID: {squareID}.");
            return;
        }

        GameStateManager stateManager = GameStateManager.Instance;
        if (stateManager != null && !stateManager.IsBallInPlay) return;

        if (lastActiveSquare != null && lastActiveSquare != this)
        {
            lastActiveSquare.bounceCount = 0;
        }

        lastActiveSquare = this;
        bounceCount++;

        Debug.Log(
            $"[RoleTrace] Court bounce: square={squareRole}, count={bounceCount}, " +
            $"ballSpeed={collision.relativeVelocity.magnitude:F2}, position={ball.transform.position}.",
            this);
        stateManager?.RegisterBallBounce(squareRole);

        if (bounceCount >= 2)
        {
            Debug.Log($"Ball is out in {squareRole} after {bounceCount} bounces.");
            stateManager?.HandleBallOut(squareRole);
            bounceCount = 0;
        }
    }
}