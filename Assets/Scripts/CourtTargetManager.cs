using System;
using UnityEngine;

/// <summary>
/// Calculates a virtual 3x3 target grid for each quadrant of the court.
/// Square indices are laid out in world X/Z order: 0=minX/minZ (Square 1), 1=maxX/minZ (Square 2),
/// 2=maxX/maxZ (Square 3), and 3=minX/maxZ (Square 4).
/// </summary>
public class CourtTargetManager : MonoBehaviour
{
    public const int SquareCount = 4;
    public const int TargetsPerSquare = 9;

    [Header("Target Layout")]
    [SerializeField, Min(0f)] private float targetHeightAboveCourt = 0.02f;
    [SerializeField, Min(0f)] private float targetInset = 0.05f;

    [Header("Strike Validation")]
    [SerializeField, Range(10f, 90f)] private float maximumAimError = 40f;
    [SerializeField, Min(0f)] private float minimumSwingSpeed = 0.8f;
    [SerializeField, Min(0f)] private float maximumSwingSpeed = 12f;

    private readonly Vector3[] targetPositions = new Vector3[SquareCount * TargetsPerSquare];
    private Bounds courtBounds;
    private bool targetsAreReady;

    public float MinimumSwingSpeed => minimumSwingSpeed;
    public float MaximumSwingSpeed => maximumSwingSpeed;
    public float MaximumAimError => maximumAimError;

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
            return false;
        }
        horizontalDirection.Normalize();

        if (swingSpeed < minimumSwingSpeed)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = $"Swing speed ({swingSpeed:0.##}) is below minimum ({minimumSwingSpeed:0.##}).";
            return false;
        }

        // Find the best target square among the other 3 squares
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
            selection.failureReason = $"Aim error ({smallestAngle:0.#}°) exceeds maximum ({maximumAimError:0.#}°).";
            return false;
        }

        // Calculate intended landing point based on swing speed and direction
        float normalizedSpeed = Mathf.InverseLerp(minimumSwingSpeed, maximumSwingSpeed, swingSpeed);
        Vector3 targetCenter = GetSquareCenter(bestTargetSquare);
        float distToTargetCenter = Vector3.Distance(strikeOrigin, targetCenter);
        // Vary depth by +/- 0.9m around square center based on swing speed
        float targetDistance = Mathf.Lerp(distToTargetCenter - 0.9f, distToTargetCenter + 0.9f, normalizedSpeed);
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
