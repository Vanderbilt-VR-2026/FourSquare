using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Test-ball helper that launches a Rigidbody to a target selected by CourtTargetManager.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallTrajectoryController : MonoBehaviour
{
    [SerializeField] private CourtTargetManager courtTargetManager;
    [SerializeField] private Rigidbody ballRigidbody;
    [SerializeField] private SphereCollider handHitZone;

    [Header("Test Strike")]
    [SerializeField, Range(0, 3)] private int sourceSquare;
    [SerializeField] private Vector3 horizontalStrikeDirection = Vector3.forward;
    [SerializeField, Min(0f)] private float swingSpeed = 6f;
    [SerializeField, Min(0.1f)] private float flightTimePerWorldUnit = 0.12f;
    [SerializeField, Min(0.05f)] private float minimumFlightTime = 0.35f;
    [SerializeField, Min(0.05f)] private float maximumFlightTime = 2f;

    [Header("Keyboard Test Input")]
    [SerializeField, Min(0f)] private float directionTurnSpeed = 90f;
    [SerializeField] private KeyCode keyboardHandStrikeKey = KeyCode.F;

    [Header("Hand Test Input")]
    [SerializeField, Min(0.01f)] private float handHitRadius = 0.9f;
    [SerializeField, Min(0f)] private float minimumHandSpeed = 0.5f;
    [SerializeField, Min(0f)] private float hitCooldown = 0.2f;

    private readonly System.Collections.Generic.Dictionary<BallHandMarker, float> lastHitTimes =
        new System.Collections.Generic.Dictionary<BallHandMarker, float>();

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

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
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

    [ContextMenu("Strike Test Ball")]
    public void StrikeTestBall()
    {
        if (courtTargetManager == null)
        {
            Debug.LogError("BallTrajectoryController requires a CourtTargetManager reference.", this);
            return;
        }

        if (!courtTargetManager.TryGetTarget(sourceSquare, transform.position,
            horizontalStrikeDirection, swingSpeed, out CourtTargetManager.TargetSelection selection))
        {
            Debug.LogWarning($"Strike marked Out of Bounds: {selection.failureReason}", this);
            return;
        }

        LaunchAt(selection.position);
        Debug.Log($"Launched toward square {selection.targetSquare}, column {selection.column}, row {selection.row}.", this);
    }

    public bool TryLaunch(Vector3 strikeOrigin, Vector3 strikeDirection, float strikeSpeed, int launchingSquare)
    {
        if (courtTargetManager == null)
        {
            Debug.LogError("BallTrajectoryController requires a CourtTargetManager reference.", this);
            return false;
        }

        if (!courtTargetManager.TryGetTarget(launchingSquare, strikeOrigin, strikeDirection,
            strikeSpeed, out CourtTargetManager.TargetSelection selection))
        {
            Debug.LogWarning($"Strike marked Out of Bounds: {selection.failureReason}", this);
            return false;
        }

        LaunchAt(selection.position, strikeOrigin);
        return true;
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
        if (strikeDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float strikeSpeed = Mathf.Clamp(hand.Velocity.magnitude, minimumHandSpeed,
            courtTargetManager != null ? courtTargetManager.MaximumSwingSpeed : swingSpeed);
        if (TryLaunch(transform.position, strikeDirection, strikeSpeed, sourceSquare))
        {
            lastHitTimes[hand] = Time.time;
        }
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
        ballRigidbody.WakeUp();
        ballRigidbody.linearVelocity = Vector3.zero;
        ballRigidbody.angularVelocity = Vector3.zero;
        ballRigidbody.linearVelocity = launchVelocity;
        Debug.Log($"Test ball launch: origin {origin}, target {target}, velocity {launchVelocity}.",
            this);
    }

    private void OnValidate()
    {
        handHitRadius = Mathf.Max(0.01f, handHitRadius);
        minimumHandSpeed = Mathf.Max(0f, minimumHandSpeed);
        hitCooldown = Mathf.Max(0f, hitCooldown);
        ConfigureHandHitZone();
    }
}
