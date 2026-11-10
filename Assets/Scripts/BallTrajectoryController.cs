using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Test-ball helper that launches a Rigidbody to a target sub-square selected by CourtTargetManager.
/// If a hit is reasonably on target, physics propels it in a high, natural parabolic arc to the selected sub-square.
/// If a hit is wildly mistimed or off (too weak, overpowered smash, or bad aim), physics takes over,
/// the ball flies off freely, and it is marked Out of Bounds.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallTrajectoryController : MonoBehaviour
{
    [SerializeField] private CourtTargetManager courtTargetManager;
    [SerializeField] private Rigidbody ballRigidbody;
    [SerializeField] private SphereCollider handHitZone;
    [SerializeField] private SphereCollider physicalCollider;

    [Header("Ball Reset")]
    [SerializeField] private Vector3 initialDropPosition = new Vector3(0f, 2f, 0f);
    [SerializeField] private float autoResetYThreshold = -4f;

    [Header("Automatic Respawn")]
    [Tooltip("Delay in seconds after leaving the playable court area before the ball automatically respawns.")]
    [SerializeField, Min(0.5f)] private float outOfBoundsRespawnDelay = 3.0f;

    [Tooltip("Continuous duration in seconds the ball must remain stationary on the ground before respawning.")]
    [SerializeField, Min(0.2f)] private float groundSettledDuration = 1.0f;

    [Tooltip("Linear speed threshold (m/s) below which the ball is considered stopped.")]
    [SerializeField, Min(0.01f)] private float restLinearSpeedThreshold = 0.15f;

    [Tooltip("Angular speed threshold (rad/s) below which the ball is considered stopped.")]
    [SerializeField, Min(0.01f)] private float restAngularSpeedThreshold = 0.6f;

    [Tooltip("Vertical clearance above the bottom of the ball to check for ground contact.")]
    [SerializeField, Min(0.01f)] private float groundCheckTolerance = 0.08f;

    private float outOfBoundsTimer;
    private float groundSettledTimer;
    private bool hasGroundContact;

    [Header("Trajectory Arc & Bounce")]
    [Tooltip("Peak height of the arc above launch point.")]
    [SerializeField, Min(0.3f)] private float arcApexHeight = 1.25f;
    [SerializeField, Min(0.2f)] private float minimumFlightTime = 0.65f;
    [SerializeField, Min(0.5f)] private float maximumFlightTime = 2.5f;

    [Header("Test Strike")]
    [SerializeField, Range(0, 3)] private int sourceSquare = 3;
    [SerializeField] private Vector3 horizontalStrikeDirection = Vector3.forward;
    [SerializeField, Min(0f)] private float swingSpeed = 2.5f;

    [Header("Keyboard Test Input")]
    [SerializeField, Min(0f)] private float directionTurnSpeed = 90f;
    [SerializeField] private KeyCode keyboardHandStrikeKey = KeyCode.F;

    [Header("Hand Test Input")]
    [SerializeField, Min(0.01f)] private float handHitRadius = 0.32f;
    [SerializeField, Min(0f)] private float minimumHandSpeed = 0.25f;
    [SerializeField, Min(0f)] private float hitCooldown = 0.25f;

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

        ConfigureColliders();
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

        // Automatic respawn checks:
        // 1. Respawns after 3 seconds if outside the playable four-square court area
        // 2. Respawns after settling stationary on the ground for 1 second
        UpdateAutomaticRespawn();

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
        outOfBoundsTimer = 0f;
        groundSettledTimer = 0f;
        hasGroundContact = false;

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
            LaunchAt(selection.position, transform.position, swingSpeed);
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
            LaunchAt(selection.position, strikeOrigin, strikeSpeed);
            return true;
        }

        Vector3 wildVelocity = strikeDirection.normalized * strikeSpeed + Vector3.up * 2f;
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
        if (hand == null)
        {
            return;
        }

        if (lastHitTimes.TryGetValue(hand, out float lastHitTime)
            && Time.time < lastHitTime + hitCooldown)
        {
            return;
        }

        Vector3 handVel = hand.Velocity;
        Vector3 handVelHorizontal = Vector3.ProjectOnPlane(handVel, Vector3.up);
        Vector3 handToBall = Vector3.ProjectOnPlane(transform.position - hand.transform.position, Vector3.up);

        float handSpeed = handVel.magnitude;
        // Ignore only idle resting hands / negligible micro-jitters
        if (handSpeed < minimumHandSpeed && handVelHorizontal.magnitude < 0.15f)
        {
            return;
        }

        // Determine robust strike direction: blend hand movement with hand-to-ball vector
        Vector3 strikeDirection;
        if (handVelHorizontal.sqrMagnitude > 0.04f)
        {
            strikeDirection = handToBall.sqrMagnitude > 0.001f
                ? (handVelHorizontal.normalized * 0.7f + handToBall.normalized * 0.3f).normalized
                : handVelHorizontal.normalized;
        }
        else
        {
            strikeDirection = handToBall.sqrMagnitude > 0.001f ? handToBall.normalized : Vector3.forward;
        }

        // Use true measured hand speed (do not force a minimum clamp so soft hits can be underpowered)
        float strikeSpeed = handSpeed;

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
            // In-bounds hit: launch in parabolic arc scaled to hit speed
            LaunchAt(selection.position, transform.position, strikeSpeed);
            lastHitTimes[hand] = Time.time;
            Debug.Log($"[BallTrajectoryController] Guided hit to square {selection.targetSquare}, sub-square ({selection.column}, {selection.row}) at {strikeSpeed:0.#} m/s.", this);
        }
        else
        {
            // Wildly mistimed, overpowered smash, too weak, or bad aim: physics takes over, ball flies off, marked out of bounds
            LaunchOutOfBounds(handVel);
            lastHitTimes[hand] = Time.time;
            string reason = !string.IsNullOrEmpty(selection.failureReason) ? selection.failureReason : "Strike was off target.";
            Debug.LogWarning($"[BallTrajectoryController] Strike marked Out of Bounds: {reason}. Ball flying off with physics.", this);
        }
    }

    /// <summary>
    /// Applies an unguided physical velocity to the ball so physics takes over when a hit is out of bounds.
    /// Handles overpowered smashes, underpowered dribbles, and off-court angles.
    /// </summary>
    public void LaunchOutOfBounds(Vector3 strikeVelocity)
    {
        if (ballRigidbody == null) return;

        ballRigidbody.isKinematic = false;
        ballRigidbody.constraints = RigidbodyConstraints.None;
        ballRigidbody.WakeUp();

        Vector3 impulse = strikeVelocity;

        if (impulse.magnitude > 4.5f)
        {
            // Overpowered smash: flies deep and out past the boundaries
            Vector3 smashDir = impulse.normalized;
            if (smashDir.y < 0.2f) smashDir.y = 0.3f;
            ballRigidbody.linearVelocity = smashDir.normalized * (impulse.magnitude * 1.3f);
        }
        else if (impulse.magnitude < 0.8f)
        {
            // Underpowered / weak tap: dribbles softly and drops short
            Vector3 weakDir = impulse.sqrMagnitude > 0.01f ? impulse.normalized : Vector3.forward;
            ballRigidbody.linearVelocity = weakDir * 0.9f + Vector3.up * 0.7f;
        }
        else
        {
            // Off-target / wild angle: flies freely in the direction of the hit
            Vector3 wildDir = impulse.normalized;
            if (wildDir.y < 0.3f) wildDir.y = 0.45f;
            ballRigidbody.linearVelocity = wildDir.normalized * (impulse.magnitude * 1.15f);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        EvaluateGroundCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        EvaluateGroundCollision(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        hasGroundContact = false;
    }

    private void EvaluateGroundCollision(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.normal.y > 0.5f)
            {
                hasGroundContact = true;
                return;
            }
        }
    }

    public bool IsOutsidePlayableCourt()
    {
        if (courtTargetManager == null)
        {
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();
        }

        if (courtTargetManager != null)
        {
            return !courtTargetManager.IsPositionInsideCourt(transform.position);
        }

        return false;
    }

    private float GetBallRadius()
    {
        if (physicalCollider != null)
        {
            float maxScale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
            return physicalCollider.radius * maxScale;
        }

        return 0.225f;
    }

    private bool CheckIsOnGround()
    {
        if (hasGroundContact)
        {
            return true;
        }

        float radius = GetBallRadius();
        float rayDistance = radius + groundCheckTolerance;

        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateAutomaticRespawn()
    {
        if (ballRigidbody == null) return;

        // Condition 2: Ball goes out of the playable court area (3-second respawn timer)
        bool isOutsideCourt = IsOutsidePlayableCourt();
        if (isOutsideCourt)
        {
            outOfBoundsTimer += Time.deltaTime;
            if (outOfBoundsTimer >= outOfBoundsRespawnDelay)
            {
                Debug.Log($"[BallTrajectoryController] Ball outside playable court for {outOfBoundsTimer:0.#}s. Respawning.", this);
                ResetBall();
                return;
            }
        }
        else
        {
            outOfBoundsTimer = 0f;
        }

        // Condition 1: Ball comes to rest on the ground
        bool isOnGround = CheckIsOnGround();
        bool isNearlyStopped = ballRigidbody.IsSleeping() ||
            (ballRigidbody.linearVelocity.sqrMagnitude < (restLinearSpeedThreshold * restLinearSpeedThreshold)
             && ballRigidbody.angularVelocity.sqrMagnitude < (restAngularSpeedThreshold * restAngularSpeedThreshold));

        if (isOnGround && isNearlyStopped)
        {
            groundSettledTimer += Time.deltaTime;
            if (groundSettledTimer >= groundSettledDuration)
            {
                Debug.Log($"[BallTrajectoryController] Ball settled on ground for {groundSettledTimer:0.#}s. Respawning.", this);
                ResetBall();
                return;
            }
        }
        else
        {
            groundSettledTimer = 0f;
        }
    }

    private void ConfigureColliders()
    {
        SphereCollider[] colliders = GetComponents<SphereCollider>();
        foreach (SphereCollider col in colliders)
        {
            if (col.isTrigger)
            {
                if (handHitZone == null) handHitZone = col;
            }
            else
            {
                if (physicalCollider == null) physicalCollider = col;
            }
        }

        if (handHitZone != null)
        {
            handHitZone.isTrigger = true;
            handHitZone.radius = handHitRadius;
        }
    }

    /// <summary>
    /// Launches the ball toward the floor target using a parabolic arc scaled to the swing speed.
    /// Soft hits have a low, gentle arc; hard hits drive faster and deeper.
    /// </summary>
    private void LaunchAt(Vector3 target, Vector3? originOverride = null, float strikeSpeed = 2.5f)
    {
        if (ballRigidbody == null)
        {
            Debug.LogError("BallTrajectoryController requires a Rigidbody.", this);
            return;
        }

        Vector3 origin = originOverride ?? transform.position;
        Vector3 horizontalDelta = Vector3.ProjectOnPlane(target - origin, Vector3.up);
        float horizontalDistance = horizontalDelta.magnitude;
        float gravityMagnitude = Mathf.Abs(Physics.gravity.y);

        if (gravityMagnitude < 0.0001f || horizontalDistance < 0.0001f)
        {
            Debug.LogError("Cannot calculate a ballistic arc with zero gravity or horizontal distance.", this);
            return;
        }

        float minSpeed = courtTargetManager != null ? courtTargetManager.MinimumSwingSpeed : 0.8f;
        float maxSpeed = courtTargetManager != null ? courtTargetManager.MaximumSwingSpeed : 4.8f;
        float normSpeed = Mathf.InverseLerp(minSpeed, maxSpeed, strikeSpeed);

        // Apex height scales with speed: soft hits arc low (0.85m), hard hits arc with energy (1.45m)
        float effectiveApex = Mathf.Lerp(0.85f, 1.45f, normSpeed);
        float apexY = Mathf.Max(origin.y, target.y) + effectiveApex;

        // Kinematic rise and fall phases
        float riseDist = Mathf.Max(0.05f, apexY - origin.y);
        float velocityY = Mathf.Sqrt(2f * gravityMagnitude * riseDist);
        float timeRise = velocityY / gravityMagnitude;

        float fallDist = Mathf.Max(0.05f, apexY - target.y);
        float timeFall = Mathf.Sqrt(2f * fallDist / gravityMagnitude);

        // Hard hits fly quicker (0.72x time) with punch; soft hits float gently (1.05x time)
        float kinematicTime = timeRise + timeFall;
        float speedFlightFactor = Mathf.Lerp(1.05f, 0.72f, normSpeed);
        float totalFlightTime = Mathf.Clamp(kinematicTime * speedFlightFactor, minimumFlightTime, maximumFlightTime);

        // Calculate horizontal and vertical velocity to hit target accurately in totalFlightTime
        Vector3 velocityXZ = horizontalDelta / totalFlightTime;
        float launchVy = (target.y - origin.y + 0.5f * gravityMagnitude * totalFlightTime * totalFlightTime) / totalFlightTime;
        Vector3 launchVelocity = velocityXZ + Vector3.up * launchVy;

        ballRigidbody.isKinematic = false;
        ballRigidbody.constraints = RigidbodyConstraints.None;
        ballRigidbody.WakeUp();
        ballRigidbody.linearVelocity = launchVelocity;
        ballRigidbody.angularVelocity = Vector3.zero;

        Debug.Log($"[BallTrajectoryController] Launch: origin {origin}, target {target}, speed {strikeSpeed:0.#} m/s, apex {apexY:0.##}m, time {totalFlightTime:0.##}s, vel {launchVelocity}.", this);
    }

    private void OnValidate()
    {
        handHitRadius = Mathf.Max(0.01f, handHitRadius);
        minimumHandSpeed = Mathf.Max(0f, minimumHandSpeed);
        hitCooldown = Mathf.Max(0f, hitCooldown);
        arcApexHeight = Mathf.Max(0.3f, arcApexHeight);
        minimumFlightTime = Mathf.Max(0.2f, minimumFlightTime);
        maximumFlightTime = Mathf.Max(minimumFlightTime, maximumFlightTime);
        ConfigureColliders();
    }
}
