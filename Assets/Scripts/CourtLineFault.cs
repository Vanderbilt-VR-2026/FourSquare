using UnityEngine;

public class CourtLineFault : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        HandleBallContact(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleBallContact(other);
    }

    private static void HandleBallContact(Collider other)
    {
        BallController ball = other.GetComponentInParent<BallController>();
        if (ball == null || !ball.HasGameplayAuthority) return;

        GameStateManager stateManager = GameStateManager.Instance;
        if (stateManager == null || !stateManager.IsBallInPlay) return;

        stateManager.HandleBallOut(stateManager.LastTouchedByPlayerRole);
    }
}