using UnityEngine;
using System.Collections.Generic;

public class BallController : MonoBehaviour
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
    [SerializeField] private float maximumHitVelocity = 8f;
    [SerializeField] private float hitCooldown = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float awayDirectionBlend = 0.15f;

    [Header("Ball Visual")]
    [SerializeField] private Transform ballVisual;
    [SerializeField] private float squashStrength = 0.12f;
    [SerializeField] private float squashDuration = 0.12f;

    private readonly Dictionary<BallHandMarker, float> lastHitTimes = new Dictionary<BallHandMarker, float>();
    private Vector3 originalVisualScale;
    private Vector3 visualImpactScale;
    private float squashTimer;

    public float CatchRadius => catchRadius;

    private void Awake()
    {
        if (ballRigidbody == null)
        {
            ballRigidbody = GetComponent<Rigidbody>();
        }

        if (physicalCollider == null || catchZone == null)
        {
            SphereCollider[] colliders = GetComponents<SphereCollider>();
            if (physicalCollider == null && colliders.Length > 0)
            {
                physicalCollider = colliders[0];
            }

            if (catchZone == null && colliders.Length > 1)
            {
                catchZone = colliders[1];
            }
        }

        ConfigureCatchZone();

        if (ballVisual != null)
        {
            originalVisualScale = ballVisual.localScale;
            visualImpactScale = originalVisualScale;
        }
    }

    private void ConfigureCatchZone()
    {
        if (catchZone == null)
        {
            return;
        }

        catchZone.isTrigger = true;
        catchZone.radius = catchRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        BallHandMarker hand = other.GetComponentInParent<BallHandMarker>();
        if (hand != null)
        {
            TryHit(hand);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        BallHandMarker hand = other.GetComponentInParent<BallHandMarker>();
        if (hand != null)
        {
            TryHit(hand);
        }
    }

    private void TryHit(BallHandMarker hand)
    {
        if (lastHitTimes.TryGetValue(hand, out float lastHitTime) && Time.time < lastHitTime + hitCooldown)
        {
            return;
        }

        Vector3 handToBall = transform.position - hand.transform.position;
        if (handToBall.sqrMagnitude < Mathf.Epsilon)
        {
            return;
        }

        Vector3 awayFromHand = handToBall.normalized;
        Vector3 handVelocity = hand.Velocity;
        float inwardSpeed = Vector3.Dot(handVelocity, awayFromHand);
        if (inwardSpeed < minimumHandSpeed || handVelocity.sqrMagnitude < Mathf.Epsilon)
        {
            return;
        }

        Vector3 handDirection = handVelocity.normalized;
        Vector3 launchDirection = Vector3.Slerp(handDirection, awayFromHand, awayDirectionBlend).normalized;
        float launchSpeed = Mathf.Clamp(inwardSpeed * hitStrength, minimumHitVelocity, maximumHitVelocity);
        ballRigidbody.AddForce(launchDirection * launchSpeed, ForceMode.Impulse);
        ballRigidbody.linearVelocity = Vector3.ClampMagnitude(ballRigidbody.linearVelocity, maximumHitVelocity);
        lastHitTimes[hand] = Time.time;
        TriggerSquash(launchDirection);

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.SetBallCarrier(hand.gameObject, hand.PlayerRole);
            GameStateManager.Instance.SetBallInPlay(true);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount > 0)
        {
            TriggerSquash(collision.GetContact(0).normal);
        }
    }

    private void Update()
    {
        if (ballVisual == null || squashTimer <= 0f)
        {
            return;
        }

        squashTimer -= Time.deltaTime;
        float recovery = 1f - Mathf.Clamp01(squashTimer / Mathf.Max(0.01f, squashDuration));
        ballVisual.localScale = Vector3.Lerp(visualImpactScale, originalVisualScale, recovery);
    }

    private void TriggerSquash(Vector3 impactDirection)
    {
        if (ballVisual == null)
        {
            return;
        }

        Vector3 localDirection = ballVisual.InverseTransformDirection(impactDirection).normalized;
        float strength = Mathf.Clamp01(squashStrength);
        float perpendicularScale = 1f + strength * 0.5f;
        Vector3 scaleMultiplier = Vector3.one * perpendicularScale;
        int squashAxis = 0;
        if (Mathf.Abs(localDirection.y) > Mathf.Abs(localDirection.x))
        {
            squashAxis = 1;
        }

        if (Mathf.Abs(localDirection.z) > Mathf.Abs(localDirection[squashAxis]))
        {
            squashAxis = 2;
        }

        scaleMultiplier[squashAxis] = 1f - strength;
        visualImpactScale = Vector3.Scale(originalVisualScale, scaleMultiplier);
        ballVisual.localScale = visualImpactScale;
        squashTimer = Mathf.Max(0.01f, squashDuration);
    }

    private void OnValidate()
    {
        catchRadius = Mathf.Max(0.01f, catchRadius);
        hitStrength = Mathf.Max(0f, hitStrength);
        minimumHandSpeed = Mathf.Max(0f, minimumHandSpeed);
        minimumHitVelocity = Mathf.Max(0f, minimumHitVelocity);
        maximumHitVelocity = Mathf.Max(minimumHitVelocity, maximumHitVelocity);
        hitCooldown = Mathf.Max(0f, hitCooldown);
        squashStrength = Mathf.Clamp01(squashStrength);
        squashDuration = Mathf.Max(0.01f, squashDuration);
        ConfigureCatchZone();
    }

}