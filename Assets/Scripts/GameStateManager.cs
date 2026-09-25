using System;
using System.Collections.Generic;
using UnityEngine;

public class GameStateManager : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static GameStateManager Instance { get; private set; }

    // --- C# Events ---
    public event Action<FourSquarePlayer, SquareRole, SquareRole> OnPlayerRoleChanged; // (Player, OldRole, NewRole)
    public event Action<string, int> OnScoreUpdated;                                   // (PlayerId, NewScore)
    public event Action<SquareRole> OnBallBouncedInSquare;                             // (SquareRole hit)
    public event Action<SquareRole> OnPlayerEliminated;                                // (Eliminated SquareRole)

    [Header("Runtime State (Ephemerals)")]
    [SerializeField] private GameObject currentBallCarrier;
    [SerializeField] private SquareRole lastSquareHit;
    [SerializeField] private SquareRole lastTouchedByPlayerRole;
    [SerializeField] private bool isBallInPlay = false;

    [Header("Persistent Session State")]
    [SerializeField] private List<FourSquarePlayer> playerList = new List<FourSquarePlayer>();

    // Fast lookup for the 4 active roles
    private Dictionary<SquareRole, FourSquarePlayer> activePlayers = new Dictionary<SquareRole, FourSquarePlayer>();

    // Getters
    public GameObject CurrentBallCarrier => currentBallCarrier;
    public SquareRole LastSquareHit => lastSquareHit;
    public SquareRole LastTouchedByPlayerRole => lastTouchedByPlayerRole;
    public bool IsBallInPlay => isBallInPlay;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        InitializeDictionary();
    }

    private void InitializeDictionary()
    {
        activePlayers.Clear();
        foreach (var player in playerList)
        {
            activePlayers[player.currentRole] = player;
        }
    }

    // =========================================================================
    // RUNTIME / PHYSICS EVENT HANDLERS
    // =========================================================================

    public void RegisterBallBounce(SquareRole squareHit)
    {
        lastSquareHit = squareHit;
        OnBallBouncedInSquare?.Invoke(squareHit);
    }

    public void SetBallCarrier(GameObject carrier, SquareRole playerRole)
    {
        currentBallCarrier = carrier;
        lastTouchedByPlayerRole = playerRole;
    }

    public void SetBallInPlay(bool inPlay)
    {
        isBallInPlay = inPlay;
    }

    // =========================================================================
    // PERSISTENT DATA & SCORING HANDLERS
    // =========================================================================

    public void AddPoints(SquareRole role, int points)
    {
        if (activePlayers.TryGetValue(role, out FourSquarePlayer player))
        {
            player.score += points;
            OnScoreUpdated?.Invoke(player.playerId, player.score);
        }
    }

    /// <summary>
    /// Call when a player makes a fault or gets eliminated.
    /// The eliminated player drops down to Peasant, and lower-ranked players move up.
    /// </summary>
    public void EliminatePlayer(SquareRole eliminatedRole)
    {
        if (!activePlayers.ContainsKey(eliminatedRole)) return;

        FourSquarePlayer faultPlayer = activePlayers[eliminatedRole];
        faultPlayer.eliminationsCount++;

        OnPlayerEliminated?.Invoke(eliminatedRole);

        // Reset ball runtime state
        isBallInPlay = false;
        currentBallCarrier = null;

        // If Peasant eliminated, no rank movements happen—Peasant stays Peasant
        if (eliminatedRole == SquareRole.Peasant)
        {
            return;
        }

        RotateRoles(eliminatedRole);
    }

    private void RotateRoles(SquareRole faultRole)
    {
        FourSquarePlayer faultPlayer = activePlayers[faultRole];

        // 1. Shift players who were BELOW the eliminated player UP one rank
        // Example: If Queen (3) eliminated -> Jack (2) becomes Queen (3), Peasant (1) becomes Jack (2)
        for (int r = (int)faultRole - 1; r >= (int)SquareRole.Peasant; r--)
        {
            SquareRole currentRole = (SquareRole)r;
            SquareRole nextRole = (SquareRole)(r + 1);

            FourSquarePlayer promotingPlayer = activePlayers[currentRole];
            promotingPlayer.currentRole = nextRole;

            OnPlayerRoleChanged?.Invoke(promotingPlayer, currentRole, nextRole);
        }

        // 2. The eliminated player goes straight to Peasant
        SquareRole oldFaultRole = faultPlayer.currentRole;
        faultPlayer.currentRole = SquareRole.Peasant;
        OnPlayerRoleChanged?.Invoke(faultPlayer, oldFaultRole, SquareRole.Peasant);

        // Re-sync dictionary mapping
        InitializeDictionary();
    }
}