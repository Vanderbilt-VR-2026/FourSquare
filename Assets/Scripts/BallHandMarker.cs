using UnityEngine;

public class BallHandMarker : MonoBehaviour
{
    [SerializeField] private float handColliderRadius = 0.08f;
    [SerializeField, Min(0.01f)] private float visualCubeSize = 0.12f;
    [SerializeField] private GameObject handVisualPrefab;
    [SerializeField] private Material visualMaterial;
    [SerializeField] private SquareRole playerRole = SquareRole.Spectator;

    public SquareRole PlayerRole => playerRole;
    public Vector3 Velocity { get; private set; }

    private Vector3 previousPosition;
    private Vector3 lastUpdatePosition;
    private float lastUpdateTime;
    private readonly Vector3[] recentVelocities = new Vector3[4];
    private int velocityIndex;
    private Renderer[] visualRenderers;
    private SphereCollider handCollider;
    private bool visualVisible = false;
    private bool hasValidVisual;

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
        if (handVisualPrefab != null)
        {
            Renderer[] previousRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer existingRenderer in previousRenderers)
            {
                if (existingRenderer.transform != transform)
                    existingRenderer.gameObject.SetActive(false);
            }

            GameObject handVisual = Instantiate(handVisualPrefab, transform, false);
            handVisual.name = handVisualPrefab.name;
            foreach (Behaviour behaviour in handVisual.GetComponentsInChildren<Behaviour>(true))
            {
                behaviour.enabled = false;
            }

            visualRenderers = handVisual.GetComponentsInChildren<Renderer>(true);
            hasValidVisual = visualRenderers.Length > 0 && IsSupportedUrpMaterial(visualMaterial);
            if (!hasValidVisual)
            {
                foreach (Renderer renderer in visualRenderers)
                {
                    if (renderer != null) renderer.enabled = false;
                }

                Debug.LogError(
                    "Hand visual requires a supported Universal Render Pipeline Lit or Unlit material.",
                    this);
                return;
            }

            foreach (Renderer renderer in visualRenderers)
            {
                if (renderer == null) continue;

                renderer.sharedMaterial = visualMaterial;
                renderer.enabled = false;
            }
            return;
        }

        bool isNetworkedPlayer = GetComponentInParent<FourSquareFusionPlayer>() != null;
        Renderer[] existingRenderers = GetComponentsInChildren<Renderer>(true);
        Renderer visualRenderer = null;
        foreach (Renderer childRenderer in existingRenderers)
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
            if (isNetworkedPlayer)
            {
                visualRenderers = System.Array.Empty<Renderer>();
                Debug.LogError(
                    "A networked player's hand has no cube mesh assigned; refusing to create a runtime fallback.",
                    this);
                return;
            }

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

        visualRenderers = new[] { visualRenderer };
        if (!IsSupportedUrpMaterial(visualMaterial))
        {
            Debug.LogError(
                "Hand marker visual requires a supported Universal Render Pipeline Lit or Unlit material.",
                this);
            visualRenderer.enabled = false;
            return;
        }

        hasValidVisual = true;
        visualRenderer.sharedMaterial = visualMaterial;
        visualRenderer.enabled = visualVisible && hasValidVisual;
        visualRenderer.gameObject.SetActive(true);
    }

    private static bool IsSupportedUrpMaterial(Material material)
    {
        if (material == null || material.shader == null || !material.shader.isSupported)
            return false;

        string shaderName = material.shader.name;
        return shaderName == "Universal Render Pipeline/Lit"
            || shaderName == "Universal Render Pipeline/Unlit";
    }

    public void SetTrackingActive(bool active)
    {
        if (visualRenderers != null)
        {
            foreach (Renderer renderer in visualRenderers)
            {
                if (renderer != null) renderer.enabled = active && visualVisible && hasValidVisual;
            }
        }
        if (handCollider != null) handCollider.enabled = active;
    }

    public void SetVisualVisible(bool visible)
    {
        visualVisible = visible;
        if (visualRenderers != null)
        {
            foreach (Renderer renderer in visualRenderers)
            {
                if (renderer != null) renderer.enabled = visualVisible && hasValidVisual;
            }
        }
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
