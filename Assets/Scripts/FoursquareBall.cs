using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class FoursquareBall : MonoBehaviour
{
    [Header("Gameplay References")]
    [SerializeField] private NetworkBallController networkBall;
    [SerializeField] private FoursquareCourt court;

    [Header("Ball Color")]
    [SerializeField] private Renderer[] ballRenderers;
    [SerializeField] private Color defaultColor = Color.white;

    public event Action<SquareRole> OnGroundBounce;
    public event Action<SquareRole> OnPlayerHit;
    public event Action<SquareRole> OnRuleViolation;
    public event Action<int> OnBallEnteredSquare;
    public event Action OnOutOfBounds;
    public event Func<SquareRole, int, bool> PlayerHitRequested;

    private MaterialPropertyBlock propertyBlock;
    private int currentSquareIndex = -1;
    private int lastObservedSquareIndex = -1;

    private void Awake()
    {
        ResolveReferences();
        if (ballRenderers == null || ballRenderers.Length == 0)
            ballRenderers = GetComponentsInChildren<Renderer>(true);
        propertyBlock = new MaterialPropertyBlock();
        ApplyColor(defaultColor);
    }

    private void LateUpdate()
    {
        if (court != null &&
            court.TryGetSquareIndex(transform.position, out int squareIndex))
        {
            SetSquareColor(squareIndex);
        }
        else
        {
            SetDefaultColor();
        }
    }

    private void FixedUpdate()
    {
        if (!HasGameplayAuthority() || court == null)
            return;

        int squareIndex = court.TryGetSquareIndex(transform.position, out int currentSquareIndex)
            ? currentSquareIndex
            : -1;
        if (squareIndex == lastObservedSquareIndex)
            return;

        lastObservedSquareIndex = squareIndex;
        OnBallEnteredSquare?.Invoke(squareIndex);
    }

    public bool RegisterPlayerHit(SquareRole role, int targetSquareIndex)
    {
        ResolveReferences();
        if (!HasGameplayAuthority())
            return false;

        if (PlayerHitRequested == null)
        {
            Debug.LogError("FoursquareBall has no FoursquareGameManager listening for player-hit events.", this);
            return false;
        }

        if (!PlayerHitRequested.Invoke(role, targetSquareIndex))
        {
            OnRuleViolation?.Invoke(role);
            return false;
        }

        OnPlayerHit?.Invoke(role);
        return true;
    }

    public void ReportOutOfBounds()
    {
        ResolveReferences();
        if (!HasGameplayAuthority())
            return;

        OnOutOfBounds?.Invoke();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!HasGameplayAuthority())
            return;

        CourtSquare square = collision.collider.GetComponentInParent<CourtSquare>();
        if (square == null)
            return;

        bool landedOnFloor = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                landedOnFloor = true;
                break;
            }
        }

        if (!landedOnFloor)
            return;

        int squareIndex = square.squareID - 1;
        SquareRole role = FoursquareCourt.GetRoleForSquare(squareIndex);
        if (role == SquareRole.Spectator)
        {
            Debug.LogError($"CourtSquare '{square.name}' has invalid square ID {square.squareID}.", square);
            return;
        }

        Debug.Log($"[FourSquare] Ball bounced in {role} square ({square.name}).", this);
        if (lastObservedSquareIndex != squareIndex)
        {
            lastObservedSquareIndex = squareIndex;
            OnBallEnteredSquare?.Invoke(squareIndex);
        }
        SetSquareColor(squareIndex);
        OnGroundBounce?.Invoke(role);
    }

    private void SetSquareColor(int squareIndex)
    {
        if (squareIndex == currentSquareIndex)
            return;

        if (court == null || !court.TryGetSquareColor(squareIndex, out Color color))
        {
            SetDefaultColor();
            return;
        }

        currentSquareIndex = squareIndex;
        ApplyColor(color);
    }

    private void SetDefaultColor()
    {
        if (currentSquareIndex == -1)
            return;

        currentSquareIndex = -1;
        ApplyColor(defaultColor);
    }

    private void ApplyColor(Color color)
    {
        if (ballRenderers == null || propertyBlock == null)
            return;

        foreach (Renderer ballRenderer in ballRenderers)
        {
            if (ballRenderer == null)
                continue;

            Material sharedMaterial = ballRenderer.sharedMaterial;
            if (sharedMaterial == null)
                continue;

            string colorProperty = sharedMaterial.HasProperty("_BaseColor")
                ? "_BaseColor"
                : sharedMaterial.HasProperty("_Color")
                    ? "_Color"
                    : null;
            if (colorProperty == null)
                continue;

            propertyBlock.Clear();
            ballRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(colorProperty, color);
            ballRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    private bool HasGameplayAuthority()
    {
        return networkBall == null || !networkBall.IsNetworked || networkBall.HasGameplayAuthority;
    }

    private void ResolveReferences()
    {
        if (networkBall == null)
            networkBall = GetComponent<NetworkBallController>();
        if (court == null)
            court = FindFirstObjectByType<FoursquareCourt>();
    }
}
