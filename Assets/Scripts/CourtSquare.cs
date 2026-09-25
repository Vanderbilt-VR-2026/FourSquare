using UnityEngine;

public class CourtSquare : MonoBehaviour
{
    [Header("Square Configuration")]
    [Tooltip("Enter 1, 2, 3, or 4 corresponding to this quadrant.")]
    public int squareID;

    private int bounceCount = 0;
    
    // Tracks which square the ball bounced in last across the entire court
    private static CourtSquare lastActiveSquare = null;

    private void OnCollisionEnter(Collision collision)
    {
        // Check if the object hitting the square is tagged as "Ball"
        if (!collision.gameObject.CompareTag("Ball")) return;

        // If the ball bounces in a DIFFERENT square, reset the old square's count
        if (lastActiveSquare != this)
        {
            if (lastActiveSquare != null)
            {
                lastActiveSquare.bounceCount = 0;
            }
            lastActiveSquare = this;
            bounceCount = 0;
        }

        // Increment the bounce count for this specific square
        bounceCount++;
        Debug.Log($"Square {squareID} recorded bounce #{bounceCount}");

        // Rule Check: 2 or more bounces means out
        if (bounceCount >= 2)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerOut($"Square {squareID} allowed 2+ bounces!");
            }
            
            // Reset count to prevent spamming logs
            bounceCount = 0;
        }
    }
}