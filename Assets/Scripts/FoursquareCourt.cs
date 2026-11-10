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

    [Header("Square Ball Colors")]
    [Tooltip("Colors used when the matching square renderer has no readable color material.")]
    [SerializeField] private Color[] squareColors =
    {
        new Color(0.20f, 0.55f, 1.00f),
        new Color(1.00f, 0.28f, 0.24f),
        new Color(0.30f, 0.85f, 0.38f),
        new Color(1.00f, 0.78f, 0.18f)
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
        squareIndex = isEast
            ? (isNorth ? 2 : 1)
            : (isNorth ? 3 : 0);
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
        Transform servePoint = kingServePoint != null
            ? kingServePoint
            : GetSpawnPoint(SquareRole.King);
        if (servePoint == null)
        {
            Debug.LogError("FoursquareCourt has no King serve point or Square 4 spawn point.", this);
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        position = servePoint.position;
        rotation = servePoint.rotation;
        if (kingServePoint == null)
            position += Vector3.up * 1.5f;
        return true;
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
