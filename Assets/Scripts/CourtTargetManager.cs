using System;
using UnityEngine;

/// <summary>
/// Calculates a virtual 3x3 target grid for each quadrant of the court.
/// Square indices are laid out in world X/Z order: 0=minX/minZ, 1=maxX/minZ,
/// 2=maxX/maxZ, and 3=minX/maxZ.
/// </summary>
public class CourtTargetManager : MonoBehaviour
{
    public const int SquareCount = 4;
    public const int TargetsPerSquare = 9;

    [Header("Target Layout")]
    [SerializeField, Min(0f)] private float targetHeightAboveCourt = 0.02f;
    [SerializeField, Min(0f)] private float targetInset = 0.05f;

    [Header("Strike Validation")]
    [SerializeField, Range(0f, 90f)] private float maximumAimError = 20f;
    [SerializeField, Min(0f)] private float minimumSwingSpeed = 1f;
    [SerializeField, Min(0f)] private float maximumSwingSpeed = 12f;

    private readonly Vector3[] targetPositions = new Vector3[SquareCount * TargetsPerSquare];
    private Bounds courtBounds;
    private bool targetsAreReady;

    public float MinimumSwingSpeed => minimumSwingSpeed;
    public float MaximumSwingSpeed => maximumSwingSpeed;

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

    public bool TryGetTarget(int sourceSquare, Vector3 strikeOrigin, Vector3 horizontalStrikeDirection,
        float swingSpeed, out TargetSelection selection)
    {
        selection = default;
        selection.sourceSquare = sourceSquare;

        if (!TryGetCourtBounds(out courtBounds))
        {
            selection.isOutOfBounds = true;
            selection.failureReason = "The court has no Renderer or BoxCollider bounds.";
            return false;
        }

        if (sourceSquare < 0 || sourceSquare >= SquareCount)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = "The source square index must be between 0 and 3.";
            return false;
        }

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(horizontalStrikeDirection, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = "The strike direction has no horizontal component.";
            return false;
        }

        if (swingSpeed < minimumSwingSpeed || swingSpeed > maximumSwingSpeed)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = $"Swing speed must be between {minimumSwingSpeed:0.##} and {maximumSwingSpeed:0.##}.";
            return false;
        }

        Vector3 sourceCenter = GetSquareCenter(sourceSquare);
        int targetSquare = FindTargetSquare(sourceSquare, sourceCenter, horizontalDirection.normalized);
        Vector3 targetCenter = GetSquareCenter(targetSquare);
        float aimError = Vector3.Angle(horizontalDirection, targetCenter - sourceCenter);
        if (aimError > maximumAimError)
        {
            selection.isOutOfBounds = true;
            selection.failureReason = $"Aim error is {aimError:0.#} degrees, above the {maximumAimError:0.#}-degree limit.";
            return false;
        }

        int column = GetColumn(horizontalDirection);
        int row = GetDepthRow(swingSpeed);
        selection.targetSquare = targetSquare;
        selection.column = column;
        selection.row = row;
        selection.position = GetTargetPosition(targetSquare, column, row);
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
        float topY = courtBounds.max.y + targetHeightAboveCourt;

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
                float rowProgress = (row + 0.5f) / 3f;
                if (zHalf == 0)
                {
                    rowProgress = 1f - rowProgress;
                }

                float z = Mathf.Lerp(squareMinZ, squareMaxZ, rowProgress);
                for (int column = 0; column < 3; column++)
                {
                    float x = Mathf.Lerp(squareMinX, squareMaxX, (column + 0.5f) / 3f);
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

    private Vector3 GetSquareCenter(int square)
    {
        float x = square == 0 || square == 3 ? courtBounds.center.x - courtBounds.size.x * 0.25f
            : courtBounds.center.x + courtBounds.size.x * 0.25f;
        float z = square == 0 || square == 1 ? courtBounds.center.z - courtBounds.size.z * 0.25f
            : courtBounds.center.z + courtBounds.size.z * 0.25f;
        return new Vector3(x, courtBounds.center.y, z);
    }

    private int FindTargetSquare(int sourceSquare, Vector3 sourceCenter, Vector3 direction)
    {
        int bestSquare = sourceSquare;
        float bestDot = float.NegativeInfinity;
        for (int square = 0; square < SquareCount; square++)
        {
            if (square == sourceSquare) continue;
            Vector3 toSquare = Vector3.ProjectOnPlane(GetSquareCenter(square) - sourceCenter, Vector3.up).normalized;
            float dot = Vector3.Dot(direction, toSquare);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestSquare = square;
            }
        }

        return bestSquare;
    }

    private int GetColumn(Vector3 direction)
    {
        if (direction.x < -0.25f) return 0;
        if (direction.x > 0.25f) return 2;
        return 1;
    }

    private int GetDepthRow(float swingSpeed)
    {
        float normalizedSpeed = Mathf.InverseLerp(minimumSwingSpeed, maximumSwingSpeed, swingSpeed);
        return Mathf.Clamp(Mathf.FloorToInt(normalizedSpeed * 3f), 0, 2);
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
                Gizmos.DrawSphere(position, Mathf.Max(0.025f, targetInset * 0.5f));
            }
        }
    }
}
