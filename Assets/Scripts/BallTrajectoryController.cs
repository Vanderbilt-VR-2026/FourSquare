using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Test-ball helper that launches a Rigidbody to a target sub-square selected by CourtTargetManager.
/// If a hit is reasonably on target, physics propels it deterministically to the selected sub-square.
/// If a hit is wildly mistimed or off, physics takes over and the ball flies off and is marked Out of Bounds.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallTrajectoryController : MonoBehaviour
{
    [SerializeField] private CourtTargetManager courtTargetManager;
    [SerializeField] private Rigidbody ballRigidbody;
    [SerializeField] private SphereCollider handHitZone;

    [Header("Ball Reset")]
    [SerializeField] private Vector3 initialDropPosition = new Vector3(0f, 2f, 0f);
    [SerializeField] private float autoResetYThreshold = -4f;

    [Header("Test Strike")]
    [SerializeField, Range(0, 3)] private int sourceSquare = 3;
    [SerializeField] private Vector3 horizontalStrikeDirection = Vector3.forward;
    [SerializeField, Min(0f)] private float swingSpeed = 6f;
    [SerializeField, Min(0.05f)] private float flightTimePerWorldUnit = 0.15f;
    [SerializeField, Min(0.05f)] private float minimumFlightTime = 0.4f;
    [SerializeField, Min(0.05f)] private float maximumFlightTime = 2f;

    [Header("Keyboard Test Input")]
    [SerializeField, Min(0f)] private float directionTurnSpeed = 90f;
    [SerializeField] private KeyCode keyboardHandStrikeKey = KeyCode.F;

    [Header("Hand Test Input")]
    [SerializeField, Min(0.01f)] private float handHitRadius = 0.3f;
    [SerializeField, Min(0f)] private float minimumHandSpeed = 0.5f;
    [SerializeField, Min(0f)] private float hitCooldown = 0.2f;

    private readonly Dictionary<BallHandMarker, float> lastHitTimes =
        new Dictionary<BallHandMarker, float>();

    private void Awake()
    {
        if (ballRigidbody == null)
        {
            ballRigidbody = GetComponent<Rigidbody>();
        }

        if (courtTargetManager == null)
        {
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();
        }

        ConfigureHandHitZone();
    }

    private void Start()
    {
        ResetBall();
    }

    private void Update()
    {
        // Auto-reset if ball falls off the court
        if (transform.position.y < autoResetYThreshold)
        {
            ResetBall();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            ResetBall();
        }

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) sourceSquare = 0;
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) sourceSquare = 1;
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) sourceSquare = 2;
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) sourceSquare = 3;

        float turnInput = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) turnInput -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) turnInput += 1f;
        float turn = turnInput * directionTurnSpeed * Time.deltaTime;
        if (Mathf.Abs(turn) > 0f)
        {
            horizontalStrikeDirection = Quaternion.AngleAxis(turn, Vector3.up) * horizontalStrikeDirection;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            StrikeTestBall();
        }

        if (WasPressed(keyboard, keyboardHandStrikeKey))
        {
            TryLaunch(transform.position, horizontalStrikeDirection, swingSpeed, sourceSquare);
        }
    }

    private static bool WasPressed(Keyboard keyboard, KeyCode key)
    {
        KeyControl control = key switch
        {
            KeyCode.F => keyboard.fKey,
            _ => null
        };
        return control != null && control.wasPressedThisFrame;
    }

    [ContextMenu("Reset Ball")]
    public void ResetBall()
    {
        if (ballRigidbody != null)
        {
            ballRigidbody.linearVelocity = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.isKinematic = false;
            ballRigidbody.constraints = RigidbodyConstraints.None;
            transform.position = initialDropPosition;
            ballRigidbody.WakeUp();
            Debug.Log($"[BallTrajectoryController] Ball reset to {initialDropPosition}.", this);
        }
    }

    [ContextMenu("Strike Test Ball")]
    public void StrikeTestBall()
    {
        if (courtTargetManager == null)
        {
            Debug.LogError("BallTrajectoryController requires a CourtTargetManager reference.", this);
            return;
        }

        int currentSquare = courtTargetManager.GetSquareFromPosition(transform.position);
        CourtTargetManager.TargetSelection selection = default;

        if (courtTargetManager.TryGetTarget(currentSquare, transform.position,
            horizontalStrikeDirection, swingSpeed, out selection))
        {
            LaunchAt(selection.position, transform.position);
            Debug.Log($"[BallTrajectoryController] Launched toward square {selection.targetSquare}, column {selection.column}, row {selection.row}.", this);
        }
        else
        {
            // Wild hit: launch off with physics and mark out of bounds
            Vector3 wildVelocity = horizontalStrikeDirection.normalized * swingSpeed + Vector3.up * 2f;
            LaunchOutOfBounds(wildVelocity);
            Debug.LogWarning($"[BallTrajectoryController] Strike marked Out of Bounds: {selection.failureReason}. Ball flying off with physics.", this);
        }
    }

    public bool TryLaunch(Vector3 strikeOrigin, Vector3 strikeDirection, float strikeSpeed, int launchingSquare)
    {
        if (courtTargetManager == null)
        {
            Debug.LogError("BallTrajectoryController requires a CourtTargetManager reference.", this);
            return false;
        }

        CourtTargetManager.TargetSelection selection = default;
        if (courtTargetManager.TryGetTarget(launchingSquare, strikeOrigin, strikeDirection,
            strikeSpeed, out selection))
        {
            LaunchAt(selection.position, strikeOrigin);
            return true;
        }

        Vector3 wildVelocity = strikeDirection.normalized * strikeSpeed + Vector3.up * 1.5f;
        LaunchOutOfBounds(wildVelocity);
        Debug.LogWarning($"[BallTrajectoryController] Strike marked Out of Bounds: {selection.failureReason}. Ball flying off with physics.", this);
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHitWithHand(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryHitWithHand(other);
    }

    private void TryHitWithHand(Collider other)
    {
        BallHandMarker hand = other.GetComponentInParent<BallHandMarker>();
        if (hand == null || hand.Velocity.magnitude < minimumHandSpeed)
        {
            return;
        }

        if (lastHitTimes.TryGetValue(hand, out float lastHitTime)
            && Time.time < lastHitTime + hitCooldown)
        {
            return;
        }

        Vector3 strikeDirection = Vector3.ProjectOnPlane(hand.Velocity, Vector3.up);
        float strikeSpeed = hand.Velocity.magnitude;

        if (courtTargetManager == null)
        {
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();
        }

        int currentSquare = courtTargetManager != null
            ? courtTargetManager.GetSquareFromPosition(transform.position)
            : sourceSquare;

        CourtTargetManager.TargetSelection selection = default;
        bool targetFound = courtTargetManager != null && courtTargetManager.TryGetTarget(
            currentSquare, transform.position, strikeDirection, strikeSpeed, out selection);

        if (targetFound)
        {
            // Reasonably good hit: send deterministically to selected sub-square
            LaunchAt(selection.position, transform.position);
            lastHitTimes[hand] = Time.time;
            Debug.Log($"[BallTrajectoryController] Guided hit to square {selection.targetSquare}, sub-square ({selection.column}, {selection.row}).", this);
        }
        else
        {
            // Wildly mistimed or off: physics takes over, ball flies off, marked out of bounds
            LaunchOutOfBounds(hand.Velocity);
            lastHitTimes[hand] = Time.time;
            string reason = !string.IsNullOrEmpty(selection.failureReason) ? selection.failureReason : "Strike was off target.";
            Debug.LogWarning($"[BallTrajectoryController] Strike marked Out of Bounds: {reason}. Ball flying off with physics.", this);
        }
    }

    /// <summary>
    /// Applies an unguided physical velocity to the ball so physics takes over when a hit is wildly mistimed or off.
    /// </summary>
    public void LaunchOutOfBounds(Vector3 strikeVelocity)
    {
        if (ballRigidbody == null) return;

        ballRigidbody.isKinematic = false;
        ballRigidbody.constraints = RigidbodyConstraints.None;
        ballRigidbody.WakeUp();

        Vector3 impulse = strikeVelocity;
        if (impulse.magnitude < 2f)
        {
            impulse = impulse.sqrMagnitude > 0.01f ? impulse.normalized * 3f : Vector3.forward * 3f;
        }
        // Ensure slight upward arc so it visibly flies away
        if (impulse.y < 0.6f)
        {
            impulse.y = 1.2f;
        }

        ballRigidbody.linearVelocity = impulse * 1.1f;
    }

    private void ConfigureHandHitZone()
    {
        if (handHitZone == null)
        {
            SphereCollider[] colliders = GetComponents<SphereCollider>();
            if (colliders.Length > 1)
            {
                handHitZone = colliders[1];
            }
        }

        if (handHitZone != null)
        {
            handHitZone.isTrigger = true;
            handHitZone.radius = handHitRadius;
        }
    }

    private void LaunchAt(Vector3 target, Vector3? originOverride = null)
    {
        if (ballRigidbody == null)
        {
            Debug.LogError("BallTrajectoryController requires a Rigidbody.", this);
            return;
        }

        Vector3 origin = originOverride ?? transform.position;
        Vector3 horizontalDelta = Vector3.ProjectOnPlane(target - origin, Vector3.up);
        float horizontalDistance = horizontalDelta.magnitude;
        float time = Mathf.Clamp(horizontalDistance * flightTimePerWorldUnit,
            minimumFlightTime, maximumFlightTime);
        float gravityMagnitude = Mathf.Abs(Physics.gravity.y);

        if (gravityMagnitude < 0.0001f || horizontalDistance < 0.0001f)
        {
            Debug.LogError("Cannot calculate a ballistic arc with zero gravity or horizontal distance.", this);
            return;
        }

        Vector3 velocityXZ = horizontalDelta / time;
        float velocityY = (target.y - origin.y + 0.5f * gravityMagnitude * time * time) / time;
        Vector3 launchVelocity = velocityXZ + Vector3.up * velocityY;

        ballRigidbody.isKinematic = false;
        ballRigidbody.constraints = RigidbodyConstraints.None;
        ballRigidbody.WakeUp();
        ballRigidbody.linearVelocity = launchVelocity;
        ballRigidbody.angularVelocity = Vector3.zero;

        Debug.Log($"[BallTrajectoryController] Ball launch: origin {origin}, target {target}, velocity {launchVelocity}.", this);
    }

    private void OnValidate()
    {
        handHitRadius = Mathf.Max(0.01f, handHitRadius);
        minimumHandSpeed = Mathf.Max(0f, minimumHandSpeed);
        hitCooldown = Mathf.Max(0f, hitCooldown);
        ConfigureHandHitZone();
    }
}
