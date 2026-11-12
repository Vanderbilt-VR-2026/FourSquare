using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Fusion;

/// <summary>
/// Authoritative networked ball controller for Photon Fusion Shared Mode.
/// State Authority (Shared Mode Master Client) simulates dynamic Rigidbody physics,
/// calculates parabolic court trajectory launches, runs out-of-bounds and ground-settled timers,
/// and handles resets.
/// Remote proxies run kinematic Rigidbody physics to follow NetworkTransform smoothly,
/// and forward local VR hand strikes and desktop test inputs to State Authority via RPC.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkBallController : NetworkBehaviour
{
    public static NetworkBallController Instance { get; private set; }

    [Header("Court & Physics References")]
    [SerializeField] private CourtTargetManager courtTargetManager;
    [SerializeField] private Rigidbody ballRigidbody;
    [SerializeField] private SphereCollider handHitZone;
    [SerializeField] private SphereCollider physicalCollider;
    [SerializeField] private NetworkTransform networkTransform;
    [SerializeField] private FoursquareBall foursquareBall;

    [Header("Ball Audio")]
    [SerializeField] private AudioSource ballAudioSource;
    [SerializeField] private AudioClip ballHitClip;
    [SerializeField] private AudioClip ballBounceClip;
    [SerializeField, Min(0f)] private float bounceCooldown = 0.12f;
    [SerializeField, Min(0f)] private float minimumBounceSpeed = 0.5f;

    [Header("Ball Reset")]
    [SerializeField] private Transform ballSpawnPoint;
    [SerializeField] private Vector3 fallbackDropPosition = new Vector3(-1.28f, 2.03f, 39.38f);
    [SerializeField] private float autoResetYThreshold = -4f;
    [SerializeField, Min(0.1f)] private float preServeBobHeight = 0.12f;
    [SerializeField, Min(0.1f)] private float preServeBobFrequency = 1.5f;

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
    private bool preServeActive;
    private Vector3 preServePosition;
    private float preServeStartTime;
    private float lastBounceSoundTime = -100f;

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
    [Tooltip("Minimum hand speed required for a deliberate strike; slow touches do not count as hits.")]
    [SerializeField, Min(0f)] private float minimumHandSpeed = 1f;
    [Tooltip("Minimum part of the hand velocity that must be moving toward the ball.")]
    [SerializeField, Min(0f)] private float minimumHandApproachSpeed = 0.2f;
    [SerializeField, Min(0f)] private float hitCooldown = 0.25f;

    private readonly Dictionary<BallHandMarker, float> lastHitTimes =
        new Dictionary<BallHandMarker, float>();
    private readonly Dictionary<SquareRole, float> lastAuthoritativeHitTimes =
        new Dictionary<SquareRole, float>();

    public bool HasGameplayAuthority =>
        Object == null || !Object.IsValid || Object.HasStateAuthority;
    public bool IsNetworked => Object != null && Object.IsValid;

    public bool StartGameFromManager(bool startingRolesAssigned)
    {
        if (IsNetworked && !HasGameplayAuthority)
            return false;

        ResetBall();
        GameStateManager.Instance?.ApplyNetworkGameStarted();
        FoursquareGameManager.Instance?.ApplyNetworkState((int)FoursquareGameState.WaitingToServe);
        if (IsNetworked)
            RPC_NotifyGamePhase((int)FoursquareGameState.WaitingToServe);
        return true;
    }

    private void Awake()
    {
        Instance = this;

        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();

        if (courtTargetManager == null)
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();

        if (networkTransform == null)
            networkTransform = GetComponent<NetworkTransform>();
        if (foursquareBall == null)
            foursquareBall = GetComponent<FoursquareBall>();

        if (ballAudioSource == null)
            ballAudioSource = GetComponent<AudioSource>();

        ConfigureColliders();
        FindSpawnPointReference();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void Spawned()
    {
        ConfigurePhysicsForAuthority();
        IgnorePlayerBodyCollisions();

        if (Object.HasStateAuthority)
        {
            ResetBall();
        }
    }

    public void IgnorePlayerBodyCollision(Collider bodyCollider)
    {
        if (physicalCollider == null || bodyCollider == null || bodyCollider == physicalCollider)
            return;

        Physics.IgnoreCollision(physicalCollider, bodyCollider, true);
    }

    private void IgnorePlayerBodyCollisions()
    {
        FourSquareFusionPlayer[] players =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player != null)
                player.IgnoreBallBodyCollision(this);
        }
    }

    private void ConfigurePhysicsForAuthority()
    {
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            ballRigidbody.isKinematic = false;
            ballRigidbody.useGravity = true;
            ballRigidbody.interpolation = RigidbodyInterpolation.None;
            ballRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            ballRigidbody.linearDamping = 0f;
            ballRigidbody.angularDamping = 0.05f;
        }
        else
        {
            // Proxy: disable local dynamic physics so it never fights NetworkTransform
            ballRigidbody.linearVelocity = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.isKinematic = true;
            ballRigidbody.useGravity = false;
        }
    }

    private void FindSpawnPointReference()
    {
        if (ballSpawnPoint != null)
            return;

        GameStateManager stateManager = GameStateManager.Instance;
        if (stateManager != null && stateManager.BallSpawnPoint != null)
        {
            ballSpawnPoint = stateManager.BallSpawnPoint;
            return;
        }

        GameObject foundSpawn = GameObject.Find("Ball Spawn Point");
        if (foundSpawn != null)
        {
            ballSpawnPoint = foundSpawn.transform;
        }
    }

    private void Update()
    {
        // 1. Authoritative simulation updates (Master Client only)
        if (HasGameplayAuthority)
        {
            if (preServeActive)
            {
                HandleKeyboardInput();
                return;
            }

            if (transform.position.y < autoResetYThreshold)
            {
                ResetBall();
                return;
            }

            UpdateAutomaticRespawn();
        }

        // 2. Keyboard test controls (works on State Authority and forwards via RPC on Proxies)
        HandleKeyboardInput();
    }

    private void FixedUpdate()
    {
        if (!HasGameplayAuthority || !preServeActive || ballRigidbody == null)
            return;

        float bob = Mathf.Abs(Mathf.Sin((Time.time - preServeStartTime) *
                                        preServeBobFrequency * Mathf.PI * 2f)) *
                    preServeBobHeight;
        ballRigidbody.MovePosition(preServePosition + Vector3.up * bob);
    }

    private void HandleKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.rKey.wasPressedThisFrame)
        {
            RequestReset();
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
            RequestStrikeTestBall();
        }

        if (WasPressed(keyboard, keyboardHandStrikeKey))
        {
            RequestHit(transform.position, horizontalStrikeDirection, swingSpeed, sourceSquare);
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

    public void RequestReset()
    {
        if (HasGameplayAuthority)
        {
            ResetBall();
        }
        else if (Object != null && Object.IsValid)
        {
            RPC_RequestReset();
        }
    }

    [ContextMenu("Reset Ball")]
    public void ResetBall()
    {
        if (!HasGameplayAuthority)
        {
            RequestReset();
            return;
        }

        outOfBoundsTimer = 0f;
        groundSettledTimer = 0f;
        hasGroundContact = false;
        preServeActive = false;

        Vector3 spawnPosition = GetSpawnPosition();
        Quaternion spawnRotation = GetSpawnRotation();

        if (ballRigidbody != null)
        {
            ballRigidbody.linearVelocity = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.isKinematic = true;
            ballRigidbody.useGravity = false;
            ballRigidbody.constraints = RigidbodyConstraints.None;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }
        else
        {
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }

        if (networkTransform != null)
        {
            networkTransform.Teleport(spawnPosition, spawnRotation);
        }

        preServePosition = spawnPosition;
        preServeStartTime = Time.time;
        preServeActive = true;
        Physics.SyncTransforms();
        FoursquareGameManager.Instance?.PrepareForServe();
        Debug.Log($"[NetworkBallController] Ball reset to {spawnPosition}.", this);
    }

    private Vector3 GetSpawnPosition()
    {
        FoursquareCourt court = FoursquareCourt.Instance;
        if (court != null && court.TryGetKingServePose(out Vector3 servePosition, out _))
            return servePosition;

        if (PlayerJoinCourt.TryGetKingBallSpawnPose(out Vector3 kingPosition, out _))
            return kingPosition;

        FindSpawnPointReference();
        if (ballSpawnPoint != null)
            return ballSpawnPoint.position;

        return fallbackDropPosition;
    }

    private Quaternion GetSpawnRotation()
    {
        FoursquareCourt court = FoursquareCourt.Instance;
        if (court != null && court.TryGetKingServePose(out _, out Quaternion serveRotation))
            return serveRotation;

        if (PlayerJoinCourt.TryGetKingBallSpawnPose(out _, out Quaternion kingRotation))
            return kingRotation;

        FindSpawnPointReference();
        if (ballSpawnPoint != null)
            return ballSpawnPoint.rotation;

        return Quaternion.identity;
    }

    public void RequestStrikeTestBall()
    {
        if (courtTargetManager == null)
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();

        int currentSquare = courtTargetManager != null
            ? courtTargetManager.GetSquareFromPosition(transform.position)
            : sourceSquare;

        RequestHit(transform.position, horizontalStrikeDirection, swingSpeed, currentSquare);
    }

    public void RequestHit(Vector3 strikeOrigin, Vector3 strikeDirection, float strikeSpeed, int launchingSquare)
    {
        SquareRole hitterRole = GetLocalPlayerRole(launchingSquare);
        if (HasGameplayAuthority)
        {
            TryLaunchAuthoritative(
                strikeOrigin, strikeDirection, strikeSpeed, launchingSquare, hitterRole,
                isPhysicalHandStrike: false);
        }
        else if (Object != null && Object.IsValid)
        {
            PlayBallHitSound();
            RPC_RequestHit(
                strikeOrigin, strikeDirection, strikeSpeed, launchingSquare, (int)hitterRole,
                isPhysicalHandStrike: false);
        }
    }

    private bool TryLaunchAuthoritative(
        Vector3 strikeOrigin,
        Vector3 strikeDirection,
        float strikeSpeed,
        int launchingSquare,
        SquareRole hitterRole,
        bool isPhysicalHandStrike)
    {
        if (courtTargetManager == null)
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();

        if (courtTargetManager == null)
        {
            Debug.LogError("NetworkBallController requires a CourtTargetManager reference.", this);
            return false;
        }

        CourtTargetManager.TargetSelection selection = default;
        bool targetFound = courtTargetManager.TryGetTarget(
            launchingSquare, strikeOrigin, strikeDirection, strikeSpeed, out selection);

        FoursquareGameManager gameManager = FoursquareGameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError(
                "Foursquare rules are not active: City is missing FoursquareGameManager. " +
                "Run FourSquare > Configure Fusion Gameplay in the Unity Editor.",
                this);
            return false;
        }

        bool isServe = gameManager.CurrentState == FoursquareGameState.WaitingToServe;
        if (isServe)
        {
            if (!isPhysicalHandStrike || hitterRole != gameManager.ServingRole)
                return false;
        }
        else if (gameManager.CurrentState != FoursquareGameState.Playing)
        {
            return false;
        }

        if (lastAuthoritativeHitTimes.TryGetValue(hitterRole, out float lastHitTime) &&
            Time.time < lastHitTime + hitCooldown)
        {
            Debug.Log(
                $"[NetworkBallController] Ignored duplicate hit from {hitterRole} " +
                $"within the {hitCooldown:0.##}s hit cooldown.",
                this);
            return false;
        }

        lastAuthoritativeHitTimes[hitterRole] = Time.time;

        if (foursquareBall == null)
            foursquareBall = GetComponent<FoursquareBall>();
        if (foursquareBall == null)
        {
            Debug.LogError(
                "FoursquareGameManager is active, but the network ball has no FoursquareBall rule tracker.",
                this);
            return false;
        }
        if (isServe)
        {
            if (!gameManager.TryBeginServe(hitterRole))
                return false;
        }
        else if (!foursquareBall.RegisterPlayerHit(
                     hitterRole, targetFound ? selection.targetSquare : -1))
        {
            return false;
        }

        if (targetFound)
        {
            LaunchAt(selection.position, strikeOrigin, strikeSpeed);
            PlayBallHitSound();
            Debug.Log($"[NetworkBallController] Launched toward square {selection.targetSquare}, column {selection.column}, row {selection.row}.", this);
            return true;
        }

        Vector3 wildVelocity = strikeDirection.normalized * strikeSpeed + Vector3.up * 2f;
        LaunchOutOfBounds(wildVelocity);
        PlayBallHitSound();
        Debug.LogWarning($"[NetworkBallController] Strike marked Out of Bounds: {selection.failureReason}. Ball flying off with physics.", this);
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
            return;

        FourSquareFusionPlayer handOwner =
            hand.GetComponentInParent<FourSquareFusionPlayer>();
        if (IsNetworked && (handOwner == null || !handOwner.IsLocallyOwned))
            return;

        if (lastHitTimes.TryGetValue(hand, out float lastHitTime)
            && Time.time < lastHitTime + hitCooldown)
        {
            return;
        }

        Vector3 handVel = hand.Velocity;
        float handSpeed = handVel.magnitude;
        Vector3 toBall = transform.position - hand.transform.position;
        float approachSpeed = toBall.sqrMagnitude > 0.0001f
            ? Vector3.Dot(handVel, toBall.normalized)
            : 0f;
        if (handSpeed < minimumHandSpeed || approachSpeed < minimumHandApproachSpeed)
            return;

        Vector3 handVelHorizontal = Vector3.ProjectOnPlane(handVel, Vector3.up);
        Vector3 handToBall = Vector3.ProjectOnPlane(toBall, Vector3.up);
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

        float strikeSpeed = handSpeed;

        if (courtTargetManager == null)
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();

        int currentSquare = courtTargetManager != null
            ? courtTargetManager.GetSquareFromPosition(transform.position)
            : sourceSquare;

        lastHitTimes[hand] = Time.time;
        Debug.Log(
            $"[NetworkBallController] Intentional hand strike detected: role={hand.PlayerRole}, " +
            $"speed={handSpeed:F2} m/s, approach={approachSpeed:F2} m/s, square={currentSquare}.",
            this);

        if (HasGameplayAuthority)
        {
            TryLaunchAuthoritative(
                transform.position, strikeDirection, strikeSpeed, currentSquare, hand.PlayerRole,
                isPhysicalHandStrike: true);
        }
        else if (Object != null && Object.IsValid)
        {
            PlayBallHitSound();
            RPC_RequestHit(
                transform.position, strikeDirection, strikeSpeed, currentSquare, (int)hand.PlayerRole,
                isPhysicalHandStrike: true);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestHit(
        Vector3 strikeOrigin,
        Vector3 strikeDirection,
        float strikeSpeed,
        int launchingSquare,
        int hitterRole,
        bool isPhysicalHandStrike,
        RpcInfo info = default)
    {
        if (!HasGameplayAuthority)
            return;

        if (!System.Enum.IsDefined(typeof(SquareRole), hitterRole) ||
            hitterRole == (int)SquareRole.Spectator ||
            !IsPlayerAuthorizedForRole(info.Source, (SquareRole)hitterRole))
        {
            Debug.LogWarning($"Ignoring ball-hit request with invalid player role {hitterRole}.", this);
            return;
        }

        TryLaunchAuthoritative(
            strikeOrigin, strikeDirection, strikeSpeed, launchingSquare, (SquareRole)hitterRole,
            isPhysicalHandStrike);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyGamePhase(int stateValue)
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            return;

        FoursquareGameManager.Instance?.ApplyNetworkState(stateValue);
        if (stateValue == (int)FoursquareGameState.Playing ||
            stateValue == (int)FoursquareGameState.WaitingToServe)
            GameStateManager.Instance?.ApplyNetworkGameStarted();
    }

    private static bool IsPlayerAuthorizedForRole(PlayerRef source, SquareRole role)
    {
        FourSquareFusionPlayer[] players =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player != null && player.PlayerId == source.ToString())
                return player.PlayerRole == role;
        }

        return false;
    }

    private static SquareRole GetLocalPlayerRole(int fallbackSquare)
    {
        FourSquareFusionPlayer[] players =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player != null && player.IsLocallyOwned &&
                player.PlayerRole != SquareRole.Spectator)
            {
                return player.PlayerRole;
            }
        }

        return FoursquareCourt.GetRoleForSquare(fallbackSquare);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestReset()
    {
        if (!HasGameplayAuthority)
            return;

        ResetBall();
    }

    public void LaunchOutOfBounds(Vector3 strikeVelocity)
    {
        if (ballRigidbody == null) return;

        preServeActive = false;
        ballRigidbody.useGravity = true;
        ballRigidbody.isKinematic = false;
        ballRigidbody.constraints = RigidbodyConstraints.None;
        ballRigidbody.WakeUp();

        Vector3 impulse = strikeVelocity;

        if (impulse.magnitude > 4.5f)
        {
            Vector3 smashDir = impulse.normalized;
            if (smashDir.y < 0.2f) smashDir.y = 0.3f;
            ballRigidbody.linearVelocity = smashDir.normalized * (impulse.magnitude * 1.3f);
        }
        else if (impulse.magnitude < 0.8f)
        {
            Vector3 weakDir = impulse.sqrMagnitude > 0.01f ? impulse.normalized : Vector3.forward;
            ballRigidbody.linearVelocity = weakDir * 0.9f + Vector3.up * 0.7f;
        }
        else
        {
            Vector3 wildDir = impulse.normalized;
            if (wildDir.y < 0.3f) wildDir.y = 0.45f;
            ballRigidbody.linearVelocity = wildDir.normalized * (impulse.magnitude * 1.15f);
        }
    }

    private void LaunchAt(Vector3 target, Vector3? originOverride = null, float strikeSpeed = 2.5f)
    {
        if (ballRigidbody == null)
        {
            Debug.LogError("NetworkBallController requires a Rigidbody.", this);
            return;
        }

        preServeActive = false;
        ballRigidbody.useGravity = true;
        ballRigidbody.isKinematic = false;
        ballRigidbody.constraints = RigidbodyConstraints.None;

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

        float effectiveApex = Mathf.Lerp(0.85f, 1.45f, normSpeed);
        float apexY = Mathf.Max(origin.y, target.y) + effectiveApex;

        float riseDist = Mathf.Max(0.05f, apexY - origin.y);
        float velocityY = Mathf.Sqrt(2f * gravityMagnitude * riseDist);
        float timeRise = velocityY / gravityMagnitude;

        float fallDist = Mathf.Max(0.05f, apexY - target.y);
        float timeFall = Mathf.Sqrt(2f * fallDist / gravityMagnitude);

        float kinematicTime = timeRise + timeFall;
        float speedFlightFactor = Mathf.Lerp(1.05f, 0.72f, normSpeed);
        float totalFlightTime = Mathf.Clamp(kinematicTime * speedFlightFactor, minimumFlightTime, maximumFlightTime);

        Vector3 velocityXZ = horizontalDelta / totalFlightTime;
        float launchVy = (target.y - origin.y + 0.5f * gravityMagnitude * totalFlightTime * totalFlightTime) / totalFlightTime;
        Vector3 launchVelocity = velocityXZ + Vector3.up * launchVy;

        ballRigidbody.WakeUp();
        ballRigidbody.linearVelocity = launchVelocity;
        ballRigidbody.angularVelocity = Vector3.zero;

        Debug.Log($"[NetworkBallController] Launch: origin {origin}, target {target}, speed {strikeSpeed:0.#} m/s, apex {apexY:0.##}m, time {totalFlightTime:0.##}s, vel {launchVelocity}.", this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        EvaluateGroundCollision(collision);
        TryPlayBounceSound(collision);
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

    private void TryPlayBounceSound(Collision collision)
    {
        if (ballAudioSource == null || ballBounceClip == null)
            return;

        if (Time.time - lastBounceSoundTime < bounceCooldown)
            return;

        if (collision.relativeVelocity.magnitude < minimumBounceSpeed)
            return;

        lastBounceSoundTime = Time.time;
        ballAudioSource.PlayOneShot(ballBounceClip);
    }

    private void PlayBallHitSound()
    {
        if (ballAudioSource != null && ballHitClip != null)
        {
            ballAudioSource.PlayOneShot(ballHitClip);
        }
    }

    public bool IsOutsidePlayableCourt()
    {
        if (courtTargetManager == null)
            courtTargetManager = FindFirstObjectByType<CourtTargetManager>();

        if (courtTargetManager != null)
            return !courtTargetManager.IsPositionInsideCourt(transform.position);

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
            return true;

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
        if (ballRigidbody == null)
            return;

        // Condition 1: Ball outside playable four-square court (3-second respawn timer)
        bool isOutsideCourt = IsOutsidePlayableCourt();
        if (isOutsideCourt)
        {
            outOfBoundsTimer += Time.deltaTime;
            if (outOfBoundsTimer >= outOfBoundsRespawnDelay)
            {
                Debug.Log($"[NetworkBallController] Ball outside playable court for {outOfBoundsTimer:0.#}s. Respawning.", this);
                ResetBall();
                return;
            }
        }
        else
        {
            outOfBoundsTimer = 0f;
        }

        // Condition 2: Ball settled stationary on the ground (1-second respawn timer)
        bool isOnGround = CheckIsOnGround();
        bool isNearlyStopped = ballRigidbody.IsSleeping() ||
            (ballRigidbody.linearVelocity.sqrMagnitude < (restLinearSpeedThreshold * restLinearSpeedThreshold)
             && ballRigidbody.angularVelocity.sqrMagnitude < (restAngularSpeedThreshold * restAngularSpeedThreshold));

        if (isOnGround && isNearlyStopped)
        {
            groundSettledTimer += Time.deltaTime;
            if (groundSettledTimer >= groundSettledDuration)
            {
                Debug.Log($"[NetworkBallController] Ball settled on ground for {groundSettledTimer:0.#}s. Respawning.", this);
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

    private void OnValidate()
    {
        handHitRadius = Mathf.Max(0.01f, handHitRadius);
        minimumHandSpeed = Mathf.Max(0f, minimumHandSpeed);
        minimumHandApproachSpeed = Mathf.Max(0f, minimumHandApproachSpeed);
        hitCooldown = Mathf.Max(0f, hitCooldown);
        preServeBobHeight = Mathf.Max(0.1f, preServeBobHeight);
        preServeBobFrequency = Mathf.Max(0.1f, preServeBobFrequency);
        arcApexHeight = Mathf.Max(0.3f, arcApexHeight);
        minimumFlightTime = Mathf.Max(0.2f, minimumFlightTime);
        maximumFlightTime = Mathf.Max(minimumFlightTime, maximumFlightTime);
        ConfigureColliders();
    }
}
