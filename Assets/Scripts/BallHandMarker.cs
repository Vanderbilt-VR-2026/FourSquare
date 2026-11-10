using UnityEngine;

public class BallHandMarker : MonoBehaviour
{
    [SerializeField] private float handColliderRadius = 0.08f;
    [SerializeField, Min(0.01f)] private float visualCubeSize = 0.12f;
    [SerializeField] private Material visualMaterial;
    [SerializeField] private SquareRole playerRole = SquareRole.Spectator;

    public SquareRole PlayerRole => playerRole;
    public Vector3 Velocity { get; private set; }

    private Vector3 previousPosition;
    private Vector3 lastUpdatePosition;
    private float lastUpdateTime;
    private readonly Vector3[] recentVelocities = new Vector3[4];
    private int velocityIndex;
    private Renderer visualRenderer;
    private SphereCollider handCollider;

    public void SetPlayerRole(SquareRole role)
    {
        playerRole = role;
    }

    private void Awake()
    {
        CreateHandVisual();

        handCollider = GetComponent<SphereCollider>();
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
        lastUpdatePosition = transform.position;
        lastUpdateTime = Time.time;
    }

    private void CreateHandVisual()
    {
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer childRenderer in childRenderers)
        {
            if (childRenderer.transform == transform) continue;

            visualRenderer = childRenderer;
            visualRenderer.transform.localPosition = Vector3.zero;
            visualRenderer.transform.localRotation = Quaternion.identity;
            visualRenderer.transform.localScale = Vector3.one * visualCubeSize;
            break;
        }

        if (visualRenderer == null)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Hand Cube";
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * visualCubeSize;

            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null) Destroy(visualCollider);

            visualRenderer = visual.GetComponent<Renderer>();
        }

        if (visualMaterial == null)
        {
            Debug.LogError("Assign a build-included material to the hand marker visual.", this);
            visualRenderer.enabled = false;
            return;
        }

        visualRenderer.enabled = true;
        visualRenderer.sharedMaterial = visualMaterial;
    }

    public void SetTrackingActive(bool active)
    {
        if (visualRenderer != null) visualRenderer.enabled = active && visualMaterial != null;
        if (handCollider != null) handCollider.enabled = active;
    }

    private void Update()
    {
        float dt = Time.time - lastUpdateTime;
        if (dt > 0.0001f)
        {
            Vector3 instantVelocity = (transform.position - lastUpdatePosition) / dt;
            recentVelocities[velocityIndex] = instantVelocity;
            velocityIndex = (velocityIndex + 1) % recentVelocities.Length;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < recentVelocities.Length; i++)
            {
                sum += recentVelocities[i];
            }
            Velocity = sum / recentVelocities.Length;
            lastUpdatePosition = transform.position;
            lastUpdateTime = Time.time;
        }
    }

    private void FixedUpdate()
    {
        float fixedDeltaTime = Time.fixedDeltaTime;
        if (fixedDeltaTime > 0f)
        {
            Vector3 fixedVel = (transform.position - previousPosition) / fixedDeltaTime;
            // Fallback if Update didn't run or if fixed velocity is higher during sudden impulse
            if (fixedVel.sqrMagnitude > Velocity.sqrMagnitude)
            {
                Velocity = fixedVel;
            }
        }

        previousPosition = transform.position;
    }
}
