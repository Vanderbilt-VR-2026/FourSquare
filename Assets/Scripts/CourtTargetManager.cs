using System;
using UnityEngine;

/// <summary>
/// Calculates a virtual 3x3 target grid for each quadrant of the court.
/// Square indices are laid out in world X/Z order: 0=minX/minZ (Square 1), 1=maxX/minZ (Square 2),
/// 2=maxX/maxZ (Square 3), and 3=minX/maxZ (Square 4).
/// Also manages in-VR visual grid lines, target sub-square indicator, and real-time strike feedback.
/// </summary>
public class CourtTargetManager : MonoBehaviour
{
    public const int SquareCount = 4;
    public const int TargetsPerSquare = 9;

    [Header("Court Bounds")]
    [Tooltip("Optional explicit BoxCollider reference covering the court area (e.g. Court Bound). If null, auto-detected from children.")]
    [SerializeField] private BoxCollider courtBoundsCollider;

    [Header("Target Layout")]
    [SerializeField, Min(0f)] private float targetHeightAboveCourt = 0.02f;
    [SerializeField, Min(0f)] private float targetInset = 0.08f;

    [Header("Strike Validation")]
    [Tooltip("Maximum allowed angle difference between hit direction and square center.")]
    [SerializeField, Range(10f, 90f)] private float maximumAimError = 48f;
    [Tooltip("Hits below this speed are too weak to clear and are marked Out of Bounds.")]
    [SerializeField, Min(0f)] private float minimumSwingSpeed = 0.8f;
    [Tooltip("Hits above this speed are overpowered smashes that fly past the court Out of Bounds.")]
    [SerializeField, Min(0f)] private float maximumSwingSpeed = 4.8f;

    [Header("VR Visual Feedback")]
    [SerializeField] private bool showVisualGridInGame = false;
    [SerializeField] private bool showTargetIndicator = true;

    private readonly Vector3[] targetPositions = new Vector3[SquareCount * TargetsPerSquare];
    private Bounds courtBounds;
    private bool targetsAreReady;

    private GameObject targetIndicatorObject;
    private Renderer targetIndicatorRenderer;
    private TextMesh feedbackTextMesh;
    private float indicatorHideTime;

    public float MinimumSwingSpeed => minimumSwingSpeed;
    public float MaximumSwingSpeed => maximumSwingSpeed;
    public float MaximumAimError => maximumAimError;
    public BoxCollider CourtBoundsCollider => courtBoundsCollider;

    public Bounds CourtBounds
    {
        get
        {
            RecalculateTargetsIfNeeded();
            return courtBounds;
        }
    }

    /// <summary>
    /// Checks whether the given world position is horizontally inside the playable four-square court boundaries.
    /// Supports both axis-aligned bounds and rotated BoxColliders.
    /// </summary>
    public bool IsPositionInsideCourt(Vector3 worldPosition)
    {
        RecalculateTargetsIfNeeded();

        if (courtBoundsCollider != null)
        {
            Vector3 localPos = courtBoundsCollider.transform.InverseTransformPoint(worldPosition);
            Vector3 center = courtBoundsCollider.center;
            Vector3 halfSize = courtBoundsCollider.size * 0.5f;

            return localPos.x >= center.x - halfSize.x && localPos.x <= center.x + halfSize.x
                && localPos.z >= center.z - halfSize.z && localPos.z <= center.z + halfSize.z;
        }

        return worldPosition.x >= courtBounds.min.x && worldPosition.x <= courtBounds.max.x
            && worldPosition.z >= courtBounds.min.z && worldPosition.z <= courtBounds.max.z;
    }

    [Serializable]
    public struct TargetSelection
    {
        public int sourceSquare;
        public int targetSquare;
        public int column;
        public int row;
        public Vector3 position;
        public bool isOutOfBounds;
        public string failureReason;
    }

    private void Awake()
    {
        RecalculateTargets();
    }

    private void Start()
    {
        if (showVisualGridInGame)
        {
            CreateVisualGrid();
        }

        if (showTargetIndicator)
        {
            CreateTargetIndicator();
        }

        CreateFeedbackText();
    }

    private void Update()
    {
        if (targetIndicatorObject != null && targetIndicatorObject.activeSelf)
        {
            if (Time.time > indicatorHideTime)
            {
                targetIndicatorObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Identifies which court square (0..3) contains the specified world position.
    /// </summary>
    public int GetSquareFromPosition(Vector3 position)
    {
        RecalculateTargetsIfNeeded();
        float midX = courtBounds.center.x;
        float midZ = courtBounds.center.z;
        bool isLeft = position.x < midX;
        bool isBottom = position.z < midZ;

        if (isLeft && isBottom) return 0;   // Square 1
        if (!isLeft && isBottom) return 1;  // Square 2
        if (!isLeft && !isBottom) return 2; // Square 3
        return 3;                           // Square 4
    }

    public bool TryGetTarget(int sourceSquare, Vector3 strikeOrigin, Vector3 horizontalStrikeDirection,
        float swingSpeed, out TargetSelection selection)
    {
        selection = default;

        if (!TryGetCourtBounds(out courtBounds))
        {
            selection.isOutOfBounds = true;
            selection.failureReason = "The court has no Renderer or BoxCollider bounds.";
            UpdateFeedbackText("OUT OF BOUNDS: No court bounds", Color.red);
            return false;
        }

        // Auto-detect source square from origin if out of bounds or negative
        if (sourceSquare < 0 || sourceSquare >= SquareCount)
        {
            sourceSquare = GetSquareFromPosition(strikeOrigin);
        }
        selection.sourceSquare = sourceSquare;

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(horizontalStrikeDirection, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = "The strike direction has no horizontal component.";
            UpdateFeedbackText("OUT OF BOUNDS: No horizontal direction", Color.red);
            return false;
        }
        horizontalDirection.Normalize();

        // 1. Check underpowered hit (too weak to clear)
        if (swingSpeed < minimumSwingSpeed)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = $"Too weak ({swingSpeed:0.#} m/s) - failed to clear!";
            UpdateFeedbackText($"OUT OF BOUNDS!\nToo weak ({swingSpeed:0.#} m/s)", Color.red);
            return false;
        }

        // 2. Check overpowered hit (super fast smash flying out of bounds)
        if (swingSpeed > maximumSwingSpeed)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = $"Overpowered smash ({swingSpeed:0.#} m/s) - hit out of court!";
            UpdateFeedbackText($"OUT OF BOUNDS!\nOverpowered smash ({swingSpeed:0.#} m/s)", Color.red);
            return false;
        }

        // 3. Find the best target square among the other 3 squares and validate aim angle
        int bestTargetSquare = -1;
        float smallestAngle = float.MaxValue;

        for (int square = 0; square < SquareCount; square++)
        {
            if (square == sourceSquare) continue;

            Vector3 toSquare = Vector3.ProjectOnPlane(GetSquareCenter(square) - strikeOrigin, Vector3.up);
            if (toSquare.sqrMagnitude < 0.001f) continue;

            float angle = Vector3.Angle(horizontalDirection, toSquare);
            if (angle < smallestAngle)
            {
                smallestAngle = angle;
                bestTargetSquare = square;
            }
        }

        if (bestTargetSquare < 0 || smallestAngle > maximumAimError)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = $"Wild aim ({smallestAngle:0.#}°) - off target!";
            UpdateFeedbackText($"OUT OF BOUNDS!\nWild aim ({smallestAngle:0.#}°)", Color.red);
            return false;
        }

        // In-bounds hit: calculate intended landing point based on swing speed and direction
        float normalizedSpeed = Mathf.InverseLerp(minimumSwingSpeed, maximumSwingSpeed, swingSpeed);
        Vector3 targetCenter = GetSquareCenter(bestTargetSquare);
        float distToTargetCenter = Vector3.Distance(strikeOrigin, targetCenter);

        // Sub-square depth offset: -1.15m (shallow front) to +1.15m (deep back)
        float depthOffset = Mathf.Lerp(-1.15f, 1.15f, normalizedSpeed);
        float targetDistance = distToTargetCenter + depthOffset;
        Vector3 intendedPoint = strikeOrigin + horizontalDirection * targetDistance;

        // Select the sub-square in bestTargetSquare whose center is closest to intendedPoint
        int bestCol = 1;
        int bestRow = 1;
        float closestDistSq = float.MaxValue;

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                Vector3 subSquarePos = GetTargetPosition(bestTargetSquare, col, row);
                float distSq = Vector3.ProjectOnPlane(subSquarePos - intendedPoint, Vector3.up).sqrMagnitude;
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    bestCol = col;
                    bestRow = row;
                }
            }
        }

        selection.targetSquare = bestTargetSquare;
        selection.column = bestCol;
        selection.row = bestRow;
        selection.position = GetTargetPosition(bestTargetSquare, bestCol, bestRow);
        selection.isOutOfBounds = false;

        ShowTargetIndicator(selection.position, bestTargetSquare, bestCol, bestRow, swingSpeed);
        return true;
    }

    public Vector3 GetTargetPosition(int square, int column, int row)
    {
        RecalculateTargetsIfNeeded();
        if (square < 0 || square >= SquareCount || column < 0 || column > 2 || row < 0 || row > 2)
        {
            throw new ArgumentOutOfRangeException();
        }

        return targetPositions[square * TargetsPerSquare + row * 3 + column];
    }

    public void RecalculateTargets()
    {
        if (!TryGetCourtBounds(out courtBounds))
        {
            targetsAreReady = false;
            return;
        }

        float halfWidth = courtBounds.size.x * 0.5f;
        float halfDepth = courtBounds.size.z * 0.5f;
        float minX = courtBounds.center.x - halfWidth;
        float minZ = courtBounds.center.z - halfDepth;
        float maxX = courtBounds.center.x + halfWidth;
        float maxZ = courtBounds.center.z + halfDepth;
        // Floor level is at center.y (Y = 0 on court surface)
        float topY = courtBounds.center.y + targetHeightAboveCourt;

        for (int square = 0; square < SquareCount; square++)
        {
            int xHalf = square == 0 || square == 3 ? 0 : 1;
            int zHalf = square == 0 || square == 1 ? 0 : 1;
            float squareMinX = Mathf.Lerp(minX, maxX, xHalf * 0.5f);
            float squareMaxX = Mathf.Lerp(minX, maxX, (xHalf + 1) * 0.5f);
            float squareMinZ = Mathf.Lerp(minZ, maxZ, zHalf * 0.5f);
            float squareMaxZ = Mathf.Lerp(minZ, maxZ, (zHalf + 1) * 0.5f);

            for (int row = 0; row < 3; row++)
            {
                float z = Mathf.Lerp(squareMinZ + targetInset, squareMaxZ - targetInset, (row + 0.5f) / 3f);
                for (int column = 0; column < 3; column++)
                {
                    float x = Mathf.Lerp(squareMinX + targetInset, squareMaxX - targetInset, (column + 0.5f) / 3f);
                    targetPositions[square * TargetsPerSquare + row * 3 + column] =
                        new Vector3(x, topY, z);
                }
            }
        }

        targetsAreReady = true;
    }

    private void RecalculateTargetsIfNeeded()
    {
        if (!targetsAreReady)
        {
            RecalculateTargets();
        }
    }

    private bool TryGetCourtBounds(out Bounds bounds)
    {
        if (courtBoundsCollider == null)
        {
            courtBoundsCollider = GetComponentInChildren<BoxCollider>();
        }

        if (courtBoundsCollider != null)
        {
            bounds = courtBoundsCollider.bounds;
            return bounds.size.x > 0f && bounds.size.z > 0f;
        }
        Renderer courtRenderer = GetComponent<Renderer>();
        if (courtRenderer != null)
        {
            bounds = courtRenderer.bounds;
            return bounds.size.x > 0f && bounds.size.z > 0f;
        }

        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            bounds = boxCollider.bounds;
            return bounds.size.x > 0f && bounds.size.z > 0f;
        }

        BoxCollider childBoxCollider = GetComponentInChildren<BoxCollider>();
        if (childBoxCollider != null)
        {
            bounds = childBoxCollider.bounds;
            return bounds.size.x > 0f && bounds.size.z > 0f;
        }

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>();
        if (childRenderers.Length > 0)
        {
            bounds = childRenderers[0].bounds;
            for (int index = 1; index < childRenderers.Length; index++)
            {
                bounds.Encapsulate(childRenderers[index].bounds);
            }

            return bounds.size.x > 0f && bounds.size.z > 0f;
        }

        bounds = default;
        return false;
    }

    public Vector3 GetSquareCenter(int square)
    {
        RecalculateTargetsIfNeeded();
        float x = square == 0 || square == 3 ? courtBounds.center.x - courtBounds.size.x * 0.25f
            : courtBounds.center.x + courtBounds.size.x * 0.25f;
        float z = square == 0 || square == 1 ? courtBounds.center.z - courtBounds.size.z * 0.25f
            : courtBounds.center.z + courtBounds.size.z * 0.25f;
        return new Vector3(x, courtBounds.center.y + targetHeightAboveCourt, z);
    }

    public void ShowTargetIndicator(Vector3 targetPos, int square, int col, int row, float speed)
    {
        if (targetIndicatorObject != null)
        {
            targetIndicatorObject.transform.position = new Vector3(targetPos.x, targetPos.y + 0.015f, targetPos.z);
            targetIndicatorObject.SetActive(true);
            indicatorHideTime = Time.time + 3.0f;
        }

        string colName = col == 0 ? "Left" : (col == 1 ? "Mid" : "Right");
        string rowName = row == 0 ? "Shallow" : (row == 1 ? "Mid" : "Deep");
        int humanSquare = square + 1;
        UpdateFeedbackText($"IN BOUNDS: Square {humanSquare} [{colName}, {rowName}]\nSpeed: {speed:0.#} m/s", Color.green);
    }

    private void UpdateFeedbackText(string message, Color color)
    {
        if (feedbackTextMesh != null)
        {
            feedbackTextMesh.text = message;
            feedbackTextMesh.color = color;
        }
    }

    private void CreateVisualGrid()
    {
        RecalculateTargetsIfNeeded();
        GameObject gridParent = new GameObject("SubSquareGridLines");
        gridParent.transform.SetParent(transform, false);

        float halfWidth = courtBounds.size.x * 0.5f;
        float halfDepth = courtBounds.size.z * 0.5f;
        float minX = courtBounds.center.x - halfWidth;
        float minZ = courtBounds.center.z - halfDepth;
        float maxX = courtBounds.center.x + halfWidth;
        float maxZ = courtBounds.center.z + halfDepth;
        float lineY = courtBounds.center.y + 0.018f;

        for (int square = 0; square < SquareCount; square++)
        {
            int xHalf = square == 0 || square == 3 ? 0 : 1;
            int zHalf = square == 0 || square == 1 ? 0 : 1;
            float sqMinX = Mathf.Lerp(minX, maxX, xHalf * 0.5f);
            float sqMaxX = Mathf.Lerp(minX, maxX, (xHalf + 1) * 0.5f);
            float sqMinZ = Mathf.Lerp(minZ, maxZ, zHalf * 0.5f);
            float sqMaxZ = Mathf.Lerp(minZ, maxZ, (zHalf + 1) * 0.5f);

            // 2 vertical dividers inside each square
            for (int i = 1; i <= 2; i++)
            {
                float x = Mathf.Lerp(sqMinX, sqMaxX, i / 3f);
                CreateFloorLine(gridParent.transform,
                    new Vector3(x, lineY, sqMinZ),
                    new Vector3(x, lineY, sqMaxZ));
            }

            // 2 horizontal dividers inside each square
            for (int i = 1; i <= 2; i++)
            {
                float z = Mathf.Lerp(sqMinZ, sqMaxZ, i / 3f);
                CreateFloorLine(gridParent.transform,
                    new Vector3(sqMinX, lineY, z),
                    new Vector3(sqMaxX, lineY, z));
            }
        }
    }

    private static void CreateFloorLine(Transform parent, Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("GridLine");
        lineObj.transform.SetParent(parent, false);
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = 0.015f;
        lr.endWidth = 0.015f;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        mat.color = new Color(1f, 1f, 1f, 0.45f);
        lr.material = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private void CreateTargetIndicator()
    {
        targetIndicatorObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        targetIndicatorObject.name = "TargetSubSquareIndicator";
        targetIndicatorObject.transform.SetParent(transform, false);
        targetIndicatorObject.transform.localScale = new Vector3(0.5f, 0.005f, 0.5f);

        Collider col = targetIndicatorObject.GetComponent<Collider>();
        if (col != null) Destroy(col);

        targetIndicatorRenderer = targetIndicatorObject.GetComponent<Renderer>();
        if (targetIndicatorRenderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Material mat = new Material(shader != null ? shader : Shader.Find("Standard"));
            mat.color = new Color(0.2f, 1.0f, 0.4f, 0.85f);
            targetIndicatorRenderer.material = mat;
        }

        targetIndicatorObject.SetActive(false);
    }

    private void CreateFeedbackText()
    {
        RecalculateTargetsIfNeeded();
        GameObject textObj = new GameObject("CourtTargetFeedbackText");
        textObj.transform.SetParent(transform, false);
        // Position at court center, 2.3m up, facing Square 4 (towards player)
        textObj.transform.position = new Vector3(courtBounds.center.x, courtBounds.center.y + 2.3f, courtBounds.center.z);
        textObj.transform.rotation = Quaternion.Euler(0f, -45f, 0f);

        feedbackTextMesh = textObj.AddComponent<TextMesh>();
        feedbackTextMesh.text = "Strike the ball to begin";
        feedbackTextMesh.fontSize = 28;
        feedbackTextMesh.characterSize = 0.045f;
        feedbackTextMesh.anchor = TextAnchor.MiddleCenter;
        feedbackTextMesh.alignment = TextAlignment.Center;
        feedbackTextMesh.color = Color.white;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            RecalculateTargets();
        }

        if (!targetsAreReady) return;
        for (int square = 0; square < SquareCount; square++)
        {
            Gizmos.color = Color.HSVToRGB(square / 4f, 0.8f, 1f);
            for (int target = 0; target < TargetsPerSquare; target++)
            {
                Vector3 position = targetPositions[square * TargetsPerSquare + target];
                Gizmos.DrawSphere(position, Mathf.Max(0.04f, targetInset * 0.5f));
            }
        }
    }
}
