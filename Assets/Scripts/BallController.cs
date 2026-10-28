
using UnityEngine;
using System.Collections.Generic;
using Fusion;

[RequireComponent(typeof(NetworkObject))]
public class BallController : NetworkBehaviour
{
    [Header("Ball Colliders")]
    [SerializeField] private SphereCollider physicalCollider;
    [SerializeField] private SphereCollider catchZone;
    [SerializeField] private float catchRadius = 0.8f;

    [Header("Ball Physics")]
    [SerializeField] private Rigidbody ballRigidbody;

    [Header("Hand Hits")]
    [SerializeField] private float hitStrength = 1.5f;
    [SerializeField] private float minimumHandSpeed = 0.5f;
    [SerializeField] private float minimumHitVelocity = 1.5f;
    [SerializeField] private float maximumHitVelocity = 6f;
    [SerializeField, Min(0f)] private float maximumAngularVelocity = 12f;
    [SerializeField, Min(0f)] private float linearDamping = 0.15f;
    [SerializeField] private float hitCooldown = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float awayDirectionBlend = 0.15f;

    [Header("Ball Visual")]
    [SerializeField] private Transform ballVisual;
    [SerializeField] private float squashStrength = 0.12f;
    [SerializeField] private float squashDuration = 0.12f;

    [Header("Ball Audio")]
    [SerializeField] private AudioSource ballAudioSource;
    [SerializeField] private AudioClip ballHitClip;
    [SerializeField] private AudioClip ballBounceClip;
    [SerializeField, Min(0f)] private float bounceCooldown = 0.12f;
    [SerializeField, Min(0f)] private float minimumBounceSpeed = 0.5f;

    private float lastBounceSoundTime = -100f;

    private readonly Dictionary<BallHandMarker, float> lastHitTimes =
        new Dictionary<BallHandMarker, float>();

    private Vector3 originalVisualScale;
    private Vector3 visualImpactScale;
    private float squashTimer;
    private Renderer[] ballRenderers;

    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Quaternion NetworkRotation { get; set; }
    [Networked] public NetworkBool NetworkGameStarted { get; set; }

    public float CatchRadius => catchRadius;
    public bool IsNetworked => Object != null && Object.IsValid;
    public bool HasGameplayAuthority => !IsNetworked || Object.HasStateAuthority;
    public bool IsGameStarted => IsNetworked && NetworkGameStarted;

    private void Awake()
    {
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();

        if (ballAudioSource == null)
            ballAudioSource = GetComponent<AudioSource>();

        ConfigureBallPhysics();

        if (physicalCollider == null || catchZone == null)
        {
            SphereCollider[] colliders = GetComponents<SphereCollider>();

            if (physicalCollider == null && colliders.Length > 0)
                physicalCollider = colliders[0];

            if (catchZone == null && colliders.Length > 1)
                catchZone = colliders[1];
        }

        ConfigureCatchZone();
        ballRenderers = GetComponentsInChildren<Renderer>(true);

        if (ballVisual != null)
        {
            originalVisualScale = ballVisual.localScale;
            visualImpactScale = originalVisualScale;
        }
    }

    private void ConfigureCatchZone()
    {
        if (catchZone == null)
            return;

        catchZone.isTrigger = true;
        catchZone.radius = catchRadius;
    }

    private void ConfigureBallPhysics()
    {
        if (ballRigidbody == null)
            return;

        ballRigidbody.linearDamping = linearDamping;
        ballRigidbody.maxAngularVelocity = maximumAngularVelocity;
        ballRigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
    }

    // ---------------- AUDIO ----------------

    private void PlayBallHitSound()
    {
        if (ballAudioSource != null && ballHitClip != null)
            ballAudioSource.PlayOneShot(ballHitClip);
    }

    private void PlayBallBounceSound()
    {
        if (ballAudioSource == null || ballBounceClip == null)
            return;

        if (Time.time - lastBounceSoundTime < bounceCooldown)
            return;

        lastBounceSoundTime = Time.time;
        ballAudioSource.PlayOneShot(ballBounceClip);
    }

    // ---------------- HAND HITS ----------------

    private void OnTriggerEnter(Collider other)
    {
        BallHandMarker hand = other.GetComponentInParent<BallHandMarker>();

        if (hand != null)
            TryHit(hand);
    }

    private void OnTriggerStay(Collider other)
    {
        BallHandMarker hand = other.GetComponentInParent<BallHandMarker>();

        if (hand != null)
            TryHit(hand);
    }

    private void TryHit(BallHandMarker hand)
    {
        if (IsNetworked && !NetworkGameStarted)
            return;

        if (lastHitTimes.TryGetValue(hand, out float lastHitTime) &&
            Time.time < lastHitTime + hitCooldown)
            return;

        Vector3 handToBall =
            transform.position - hand.transform.position;

        if (handToBall.sqrMagnitude < Mathf.Epsilon)
            return;

        Vector3 awayFromHand = handToBall.normalized;
        Vector3 handVelocity = hand.Velocity;

        float inwardSpeed =
            Vector3.Dot(handVelocity, awayFromHand);

        if (inwardSpeed < minimumHandSpeed ||
            handVelocity.sqrMagnitude < Mathf.Epsilon)
            return;

        Vector3 handDirection = handVelocity.normalized;

        Vector3 launchDirection = Vector3.Slerp(
            handDirection,
            awayFromHand,
            awayDirectionBlend
        ).normalized;

        float launchSpeed = Mathf.Clamp(
            inwardSpeed * hitStrength,
            minimumHitVelocity,
            maximumHitVelocity
        );

        lastHitTimes[hand] = Time.time;

        if (IsNetworked)
        {
            RPC_RequestHit(
                launchDirection,
                launchSpeed,
                (int)hand.PlayerRole
            );
        }
        else
        {
            ApplyHit(
                launchDirection,
                launchSpeed,
                hand.PlayerRole,
                hand.gameObject
            );
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestHit(
        Vector3 launchDirection,
        float launchSpeed,
        int playerRole)
    {
        if (IsNetworked && !NetworkGameStarted)
            return;

        if (!System.Enum.IsDefined(typeof(SquareRole), playerRole))
            return;

        ApplyHit(
            launchDirection,
            launchSpeed,
            (SquareRole)playerRole,
            null
        );
    }

    // ---------------- GAME START ----------------

    public void RequestStartGame()
    {
        if (IsNetworked)
        {
            Debug.Log(
                $"[FourSquare] Start request sent to ball authority. " +
                $"Local state authority: {Object.HasStateAuthority}.",
                this
            );

            if (Object.HasStateAuthority)
            {
                BeginGame(false);
                return;
            }

            RPC_RequestStartGame();
            return;
        }

        Debug.Log(
            "[FourSquare] Starting local, non-networked game.",
            this
        );

        BeginGame(false);
    }

    public bool StartGameFromManager(bool startingRolesAssigned)
    {
        return BeginGame(startingRolesAssigned);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestStartGame()
    {
        Debug.Log(
            $"[FourSquare] State Authority received game-start RPC. " +
            $"Started: {NetworkGameStarted}.",
            this
        );

        if (NetworkGameStarted)
            return;

        BeginGame(false);
    }

    private bool BeginGame(bool startingRolesAssigned)
    {
        if (IsNetworked && !Object.HasStateAuthority)
        {
            Debug.LogWarning(
                "[FourSquare] Ignored game start because this client " +
                "is not State Authority.",
                this
            );
            return false;
        }

        if (IsNetworked && NetworkGameStarted)
        {
            Debug.Log(
                "[FourSquare] Ignored duplicate game-start request.",
                this
            );
            return false;
        }

        GameStateManager stateManager = GameStateManager.Instance;

        if (IsNetworked && !startingRolesAssigned &&
            (stateManager == null ||
             !stateManager.AssignStartingRoles()))
        {
            Debug.LogError(
                "Four Square start was rejected because starting " +
                "roles could not be assigned.",
                this
            );
            return false;
        }

        Vector3 startPosition =
            stateManager != null &&
            stateManager.BallSpawnPoint != null
            ? stateManager.BallSpawnPoint.position
            : transform.position;

        Quaternion startRotation =
            stateManager != null &&
            stateManager.BallSpawnPoint != null
            ? stateManager.BallSpawnPoint.rotation
            : transform.rotation;

        if (PlayerJoinCourt.TryGetKingBallSpawnPose(
            out Vector3 kingPosition,
            out Quaternion kingRotation))
        {
            startPosition = kingPosition;
            startRotation = kingRotation;
        }

        transform.SetPositionAndRotation(
            startPosition,
            startRotation
        );

        Physics.SyncTransforms();

        if (ballRigidbody != null)
        {
            ballRigidbody.linearVelocity = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.isKinematic = false;
            ballRigidbody.useGravity = true;
        }
        else
        {
            Debug.LogError(
                "The game ball has no Rigidbody, so it cannot " +
                "drop when the game starts.",
                this
            );
            return false;
        }

        if (IsNetworked)
        {
            NetworkGameStarted = true;
            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;
        }

        SetBallVisible(true);
        stateManager?.ApplyNetworkGameStarted();

        Debug.Log(
            $"[FourSquare] Game start completed. " +
            $"Ball released at {transform.position}.",
            this
        );

        return true;
    }

    // ---------------- APPLY HIT ----------------

    private void ApplyHit(
        Vector3 launchDirection,
        float launchSpeed,
        SquareRole playerRole,
        GameObject carrier)
    {
        if (IsNetworked && !Object.HasStateAuthority)
            return;

        if (launchDirection.sqrMagnitude < 0.0001f)
            return;

        launchSpeed = Mathf.Clamp(
            launchSpeed,
            minimumHitVelocity,
            maximumHitVelocity
        );

        ballRigidbody.AddForce(
            launchDirection.normalized * launchSpeed,
            ForceMode.Impulse
        );

        ballRigidbody.linearVelocity =
            Vector3.ClampMagnitude(
                ballRigidbody.linearVelocity,
                maximumHitVelocity
            );

        TriggerSquash(launchDirection);

        GameStateManager stateManager = GameStateManager.Instance;

        if (stateManager != null)
        {
            stateManager.SetBallCarrier(carrier, playerRole);
            stateManager.SetBallInPlay(true);
        }

        // Play locally on the authority.
        PlayBallHitSound();

        // Notify other clients in a networked session.
        if (IsNetworked)
            RPC_NotifyBallHit((int)playerRole);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyBallHit(int playerRole)
    {
        if (Object.HasStateAuthority)
            return;

        if (!System.Enum.IsDefined(typeof(SquareRole), playerRole))
            return;

        GameStateManager.Instance?.ApplyNetworkBallHit(
            (SquareRole)playerRole
        );

        PlayBallHitSound();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyBallOut(int responsibleRole)
    {
        if (Object.HasStateAuthority)
            return;

        if (!System.Enum.IsDefined(
            typeof(SquareRole),
            responsibleRole))
            return;

        GameStateManager.Instance?.ApplyNetworkBallOut(
            (SquareRole)responsibleRole
        );
    }

    // ---------------- BALL BOUNCE ----------------

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount == 0)
            return;

        TriggerSquash(collision.GetContact(0).normal);

        // Only process court bounces, not hand or wall collisions.
        // The court must have the "Ground" tag.
        if (!collision.collider.CompareTag("Ground"))
            return;

        if (IsNetworked && !Object.HasStateAuthority)
            return;

        if (collision.relativeVelocity.magnitude <
            minimumBounceSpeed)
            return;

        if (IsNetworked)
            RPC_NotifyBallBounce();
        else
            PlayBallBounceSound();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyBallBounce()
    {
        PlayBallBounceSound();
    }

    // ---------------- PHYSICS ----------------

    private void FixedUpdate()
    {
        if (IsNetworked)
            return;

        if (ballRigidbody != null)
        {
            ballRigidbody.linearVelocity =
                Vector3.ClampMagnitude(
                    ballRigidbody.linearVelocity,
                    maximumHitVelocity
                );
        }

        GameStateManager stateManager = GameStateManager.Instance;

        if (stateManager != null &&
            stateManager.IsBallInPlay &&
            PlayerJoinCourt.IsBallOutsideCourt(transform.position))
        {
            stateManager.HandleBallOutsideCourt();
        }
    }

    public override void Spawned()
    {
        if (ballRenderers == null || ballRenderers.Length == 0)
        {
            ballRenderers =
                GetComponentsInChildren<Renderer>(true);
        }

        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();

        ConfigureBallPhysics();

        if (Object.HasStateAuthority)
            NetworkGameStarted = false;

        if (ballRigidbody != null)
        {
            ballRigidbody.isKinematic = !NetworkGameStarted;
            ballRigidbody.useGravity = NetworkGameStarted;
        }

        if (Object.HasStateAuthority)
        {
            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;
        }
        else if (ballRigidbody != null)
        {
            ballRigidbody.isKinematic = true;
        }

        SetBallVisible(NetworkGameStarted);
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {
            if (ballRigidbody != null)
            {
                ballRigidbody.linearVelocity =
                    Vector3.ClampMagnitude(
                        ballRigidbody.linearVelocity,
                        maximumHitVelocity
                    );
            }

            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;

            GameStateManager stateManager = GameStateManager.Instance;

            if (stateManager != null &&
                stateManager.IsBallInPlay &&
                PlayerJoinCourt.IsBallOutsideCourt(transform.position))
            {
                stateManager.HandleBallOutsideCourt();
            }
        }
    }

    public override void Render()
    {
        if (IsNetworked && !Object.HasStateAuthority)
        {
            transform.SetPositionAndRotation(
                NetworkPosition,
                NetworkRotation
            );
        }

        if (NetworkGameStarted)
        {
            SetBallVisible(true);
            GameStateManager.Instance?.ApplyNetworkGameStarted();
        }
    }

    private void Update()
    {
        if (ballVisual == null || squashTimer <= 0f)
            return;

        squashTimer -= Time.deltaTime;

        float recovery =
            1f - Mathf.Clamp01(
                squashTimer / Mathf.Max(0.01f, squashDuration)
            );

        ballVisual.localScale =
            Vector3.Lerp(
                visualImpactScale,
                originalVisualScale,
                recovery
            );
    }

    private void TriggerSquash(Vector3 impactDirection)
    {
        if (ballVisual == null)
            return;

        Vector3 localDirection =
            ballVisual.InverseTransformDirection(
                impactDirection
            ).normalized;

        float strength = Mathf.Clamp01(squashStrength);
        float perpendicularScale = 1f + strength * 0.5f;

        Vector3 scaleMultiplier =
            Vector3.one * perpendicularScale;

        int squashAxis = 0;

        if (Mathf.Abs(localDirection.y) >
            Mathf.Abs(localDirection.x))
        {
            squashAxis = 1;
        }

        if (Mathf.Abs(localDirection.z) >
            Mathf.Abs(localDirection[squashAxis]))
        {
            squashAxis = 2;
        }

        scaleMultiplier[squashAxis] = 1f - strength;

        visualImpactScale =
            Vector3.Scale(
                originalVisualScale,
                scaleMultiplier
            );

        ballVisual.localScale = visualImpactScale;
        squashTimer = Mathf.Max(0.01f, squashDuration);
    }

    private void SetBallVisible(bool visible)
    {
        if (ballRenderers == null)
            return;

        foreach (Renderer ballRenderer in ballRenderers)
        {
            if (ballRenderer != null)
                ballRenderer.enabled = visible;
        }
    }

    private void OnValidate()
    {
        catchRadius = Mathf.Max(0.01f, catchRadius);
        hitStrength = Mathf.Max(0f, hitStrength);
        minimumHandSpeed = Mathf.Max(0f, minimumHandSpeed);
        minimumHitVelocity = Mathf.Max(0f, minimumHitVelocity);

        maximumHitVelocity =
            Mathf.Max(minimumHitVelocity, maximumHitVelocity);

        maximumAngularVelocity =
            Mathf.Max(0f, maximumAngularVelocity);

        linearDamping = Mathf.Max(0f, linearDamping);
        hitCooldown = Mathf.Max(0f, hitCooldown);
        squashStrength = Mathf.Clamp01(squashStrength);
        squashDuration = Mathf.Max(0.01f, squashDuration);

        bounceCooldown = Mathf.Max(0f, bounceCooldown);
        minimumBounceSpeed = Mathf.Max(0f, minimumBounceSpeed);

        ConfigureBallPhysics();
        ConfigureCatchZone();
    }
}
