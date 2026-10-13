using UnityEngine;

public class BallHandMarker : MonoBehaviour
{
    [SerializeField] private float handColliderRadius = 0.08f;
    [SerializeField] private SquareRole playerRole = SquareRole.Spectator;

    public SquareRole PlayerRole => playerRole;
    public Vector3 Velocity { get; private set; }

    private Vector3 previousPosition;

    private void Awake()
    {
        SphereCollider handCollider = GetComponent<SphereCollider>();
        if (handCollider == null)
        {
            handCollider = gameObject.AddComponent<SphereCollider>();
        }

        handCollider.isTrigger = true;
        handCollider.radius = handColliderRadius;

        Rigidbody handBody = GetComponent<Rigidbody>();
        if (handBody == null)
        {
            handBody = gameObject.AddComponent<Rigidbody>();
        }

        handBody.isKinematic = true;
        handBody.useGravity = false;
        previousPosition = transform.position;
    }

    private void FixedUpdate()
    {
        float fixedDeltaTime = Time.fixedDeltaTime;
        if (fixedDeltaTime > 0f)
        {
            Velocity = (transform.position - previousPosition) / fixedDeltaTime;
        }

        previousPosition = transform.position;
    }
}