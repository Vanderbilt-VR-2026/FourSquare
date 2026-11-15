using UnityEngine;
using Unity.XR.CoreUtils;
using System;

[DisallowMultipleComponent]
public sealed class FoursquareCourt : MonoBehaviour
{
    public const int SquareCount = 4;

    public static FoursquareCourt Instance { get; private set; }

    [Header("Court Geometry")]
    [SerializeField] private BoxCollider courtBounds;
    [Tooltip("Ordered Square 1 through Square 4. Square 4 is the King's square.")]
    [SerializeField] private Transform[] squareSpawnPoints = new Transform[SquareCount];
    [SerializeField] private Transform kingServePoint;
    [SerializeField, Min(0f)] private float kingServeForwardOffset = 0.75f;
    [SerializeField] private float kingServeHeadHeightOffset = -0.2f;
    [SerializeField, Min(0.1f)] private float kingServeFallbackHeightOffset = 1.1f;
    [SerializeField, Min(0f)] private float kingServeBoundaryInset = 0.35f;

    [Header("Square Ball Colors")]
    [Tooltip("Fallback colors ordered by the scene's Square 1 through Square 4 IDs.")]
    [SerializeField] private Color[] squareColors =
    {
        new Color(0.20f, 0.55f, 1.00f),
        new Color(1.00f, 0.78f, 0.18f),
        new Color(1.00f, 0.28f, 0.24f),
        new Color(0.30f, 0.85f, 0.38f)
    };
    [Tooltip("Optional floor renderers ordered Square 1 through Square 4.")]
    [SerializeField] private Renderer[] squareRenderers = new Renderer[SquareCount];
    [SerializeField] private bool useFloorMaterialColors = true;

    public event Action OnBallOutOfBounds;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second FoursquareCourt was found; destroying the duplicate.", this);
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool TryGetSquareIndex(Vector3 worldPosition, out int squareIndex)
    {
        squareIndex = -1;
        if (courtBounds == null)
        {
            Debug.LogError("FoursquareCourt requires a court bounds BoxCollider.", this);
            return false;
        }

        Vector3 localPosition = courtBounds.transform.InverseTransformPoint(worldPosition);
        Vector3 center = courtBounds.center;
        Vector3 halfSize = courtBounds.size * 0.5f;
        if (localPosition.x < center.x - halfSize.x || localPosition.x > center.x + halfSize.x ||
            localPosition.z < center.z - halfSize.z || localPosition.z > center.z + halfSize.z)
        {
            return false;
        }

        bool isEast = localPosition.x >= center.x;
        bool isNorth = localPosition.z >= center.z;
        // The scene lays out Square 1 northwest, 2 northeast, 3 southeast, and 4 southwest.
        squareIndex = isEast
            ? (isNorth ? 1 : 2)
            : (isNorth ? 0 : 3);
        return true;
    }

    public static SquareRole GetRoleForSquare(int squareIndex)
    {
        return squareIndex >= 0 && squareIndex < SquareCount
            ? (SquareRole)(squareIndex + 1)
            : SquareRole.Spectator;
    }

    public Transform GetSpawnPoint(SquareRole role)
    {
        int index = (int)role - (int)SquareRole.Peasant;
        if (squareSpawnPoints == null || index < 0 || index >= squareSpawnPoints.Length)
            return null;

        return squareSpawnPoints[index];
    }

    public bool TryGetSquareColor(int squareIndex, out Color color)
    {
        color = Color.white;
        if (squareIndex < 0 || squareIndex >= SquareCount)
            return false;

        if (useFloorMaterialColors &&
            squareRenderers != null &&
            squareIndex < squareRenderers.Length &&
            TryGetRendererColor(squareRenderers[squareIndex], out color))
        {
            return true;
        }

        if (squareColors != null && squareIndex < squareColors.Length)
        {
            color = squareColors[squareIndex];
            return true;
        }

        return false;
    }

    public bool TryGetKingServePose(out Vector3 position, out Quaternion rotation)
    {
        Transform kingSpawnPoint = GetSpawnPoint(SquareRole.King);
        Transform servePoint = kingServePoint != null ? kingServePoint : kingSpawnPoint;
        FourSquareFusionPlayer kingPlayer = null;
        FourSquareFusionPlayer[] players =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player != null && player.PlayerRole == SquareRole.King)
            {
                kingPlayer = player;
                break;
            }
        }

        if (servePoint == null && kingPlayer == null)
        {
            Debug.LogError("FoursquareCourt has no King serve point, Square 4 spawn point, or King player.", this);
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        Transform head = kingPlayer != null ? kingPlayer.HeadTransform : null;
        Transform forwardSource = head != null
            ? head
            : kingPlayer != null ? kingPlayer.transform : servePoint;
        Vector3 forward = Vector3.ProjectOnPlane(forwardSource.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        rotation = Quaternion.LookRotation(forward, Vector3.up);
        if (head != null)
        {
            position = head.position + forward * kingServeForwardOffset;
            position.y = head.position.y + kingServeHeadHeightOffset;
        }
        else if (kingPlayer != null)
        {
            position = kingPlayer.transform.position + forward * kingServeForwardOffset;
            position.y = (kingSpawnPoint != null ? kingSpawnPoint.position.y : kingPlayer.transform.position.y)
                + kingServeFallbackHeightOffset;
        }
        else if (kingServePoint != null)
        {
            position = kingServePoint.position;
            rotation = kingServePoint.rotation;
        }
        else
        {
            position = servePoint.position + forward * kingServeForwardOffset;
            position.y += kingServeFallbackHeightOffset;
        }

        ClampServePositionToKingSquare(ref position);
        return true;
    }

    private void ClampServePositionToKingSquare(ref Vector3 position)
    {
        Transform kingSpawnPoint = GetSpawnPoint(SquareRole.King);
        if (courtBounds == null || kingSpawnPoint == null)
            return;

        Vector3 localPosition = courtBounds.transform.InverseTransformPoint(position);
        Vector3 kingLocalPosition = courtBounds.transform.InverseTransformPoint(kingSpawnPoint.position);
        Vector3 center = courtBounds.center;
        Vector3 halfSize = courtBounds.size * 0.5f;
        Vector3 boundsScale = courtBounds.transform.lossyScale;
        float insetX = Mathf.Min(
            kingServeBoundaryInset / Mathf.Max(0.0001f, Mathf.Abs(boundsScale.x)),
            halfSize.x * 0.45f);
        float insetZ = Mathf.Min(
            kingServeBoundaryInset / Mathf.Max(0.0001f, Mathf.Abs(boundsScale.z)),
            halfSize.z * 0.45f);
        bool kingSquareIsEast = kingLocalPosition.x >= center.x;
        bool kingSquareIsNorth = kingLocalPosition.z >= center.z;

        localPosition.x = Mathf.Clamp(
            localPosition.x,
            kingSquareIsEast ? center.x + insetX : center.x - halfSize.x + insetX,
            kingSquareIsEast ? center.x + halfSize.x - insetX : center.x - insetX);
        localPosition.z = Mathf.Clamp(
            localPosition.z,
            kingSquareIsNorth ? center.z + insetZ : center.z - halfSize.z + insetZ,
            kingSquareIsNorth ? center.z + halfSize.z - insetZ : center.z - insetZ);
        position = courtBounds.transform.TransformPoint(localPosition);
    }

    public void TeleportAllPlayersToAssignedQuadrants()
    {
        PlayerJoinCourt.TeleportJoinedPlayersToAssignedRoles();

        FourSquareFusionPlayer[] players =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player == null || !player.IsLocallyOwned ||
                player.PlayerRole == SquareRole.Spectator)
            {
                continue;
            }

            XROrigin origin = player.GetComponent<XROrigin>();
            if (origin != null)
                PlayerJoinCourt.MoveJoinedPlayerToRole(origin, player.PlayerRole);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        FoursquareBall departingBall = other.GetComponentInParent<FoursquareBall>();
        if (departingBall == null || courtBounds == null)
            return;

        if (TryGetSquareIndex(departingBall.transform.position, out _))
            return;

        OnBallOutOfBounds?.Invoke();
    }

    private void ResolveReferences()
    {
        if (courtBounds == null)
            courtBounds = GetComponent<BoxCollider>();

        if (squareRenderers == null)
            squareRenderers = new Renderer[SquareCount];
        if (squareRenderers.Length != SquareCount)
            System.Array.Resize(ref squareRenderers, SquareCount);

        CourtSquare[] courtSquares =
            FindObjectsByType<CourtSquare>(FindObjectsSortMode.None);
        foreach (CourtSquare square in courtSquares)
        {
            int squareIndex = square.squareID - 1;
            if (squareIndex < 0 ||
                squareIndex >= SquareCount ||
                squareRenderers[squareIndex] != null)
            {
                continue;
            }

            squareRenderers[squareIndex] = square.GetComponentInChildren<Renderer>(true);
        }
    }

    private static bool TryGetRendererColor(Renderer floorRenderer, out Color color)
    {
        color = Color.white;
        if (floorRenderer == null)
            return false;

        Material material = floorRenderer.sharedMaterial;
        if (material == null)
            return false;

        if (material.HasProperty("_BaseColor"))
        {
            color = material.GetColor("_BaseColor");
            return true;
        }

        if (material.HasProperty("_Color"))
        {
            color = material.GetColor("_Color");
            return true;
        }

        return false;
    }

    private void OnValidate()
    {
        if (squareSpawnPoints == null || squareSpawnPoints.Length != SquareCount)
            System.Array.Resize(ref squareSpawnPoints, SquareCount);
        if (squareColors == null || squareColors.Length != SquareCount)
            System.Array.Resize(ref squareColors, SquareCount);
        if (squareRenderers == null || squareRenderers.Length != SquareCount)
            System.Array.Resize(ref squareRenderers, SquareCount);
    }
}
