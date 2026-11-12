using System;
using System.Collections.Generic;
using UnityEngine;

public enum FoursquareGameState
{
    WaitingToStart,
    Playing,
    RoundOver,
    WaitingToServe
}

[DisallowMultipleComponent]
public sealed class FoursquareGameManager : MonoBehaviour
{
    public static FoursquareGameManager Instance { get; private set; }

    [Header("Gameplay References")]
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private FoursquareCourt court;
    [SerializeField] private FoursquareBall ball;
    [SerializeField] private NetworkBallController networkBall;

    private readonly List<FourSquarePlayer> rankingSnapshot = new List<FourSquarePlayer>();
    private FoursquareGameState currentState = FoursquareGameState.WaitingToStart;
    private SquareRole lastBouncedRole = SquareRole.Spectator;
    private SquareRole lastPlayerRole = SquareRole.King;
    private int bounceCount;
    private int lastTrackedSquareIndex = -1;
    private int lastEliminationFrame = -1;

    public event Action<FoursquareGameState> OnStateChanged;
    public event Action<SquareRole> OnPlayerEliminated;

    public FoursquareGameState CurrentState => currentState;
    public IReadOnlyList<FourSquarePlayer> Players =>
        gameStateManager != null ? gameStateManager.Players : Array.Empty<FourSquarePlayer>();
    public IReadOnlyList<FourSquarePlayer> CurrentRanking => rankingSnapshot;
    public SquareRole LastPlayerRole => lastPlayerRole;
    public SquareRole ServingRole => SquareRole.King;

    private bool HasGameplayAuthority =>
        networkBall == null || !networkBall.IsNetworked || networkBall.HasGameplayAuthority;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second FoursquareGameManager was found; destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (gameStateManager != null)
            gameStateManager.OnGameStarted += HandleGameStarted;
        if (court != null)
            court.OnBallOutOfBounds += ReportOutOfBounds;
        if (ball != null)
        {
            ball.OnGroundBounce += RegisterBallBounce;
            ball.OnBallEnteredSquare += RegisterBallEnteredSquare;
            ball.OnOutOfBounds += ReportOutOfBounds;
            ball.PlayerHitRequested += RegisterPlayerHit;
        }
    }

    private void Start()
    {
        ResolveReferences();
        if (gameStateManager == null)
        {
            Debug.LogError(
                "FoursquareGameManager has no GameStateManager reference. Configure City with " +
                "FourSquare > Configure Fusion Gameplay.",
                this);
            return;
        }

        if (court == null || ball == null || networkBall == null)
        {
            Debug.LogError(
                $"Foursquare runtime wiring is incomplete (court={court != null}, " +
                $"rule ball={ball != null}, network ball={networkBall != null}). " +
                "Run FourSquare > Configure Fusion Gameplay in the Unity Editor.",
                this);
        }

        if (gameStateManager.HasGameStarted)
        {
            gameStateManager.EnsureSoloPlayerKing();
            CaptureRankingSnapshot();
            ResetRuleState();
            SetState(FoursquareGameState.WaitingToServe);
        }
    }

    private void OnDisable()
    {
        if (gameStateManager != null)
            gameStateManager.OnGameStarted -= HandleGameStarted;
        if (court != null)
            court.OnBallOutOfBounds -= ReportOutOfBounds;
        if (ball != null)
        {
            ball.OnGroundBounce -= RegisterBallBounce;
            ball.OnBallEnteredSquare -= RegisterBallEnteredSquare;
            ball.OnOutOfBounds -= ReportOutOfBounds;
            ball.PlayerHitRequested -= RegisterPlayerHit;
        }

    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void StartGame()
    {
        ResolveReferences();
        if (gameStateManager == null)
        {
            Debug.LogError("FoursquareGameManager requires a GameStateManager.", this);
            return;
        }

        gameStateManager.StartGame();
    }

    public void RegisterBallBounce(SquareRole role)
    {
        if (currentState != FoursquareGameState.Playing || !HasGameplayAuthority)
            return;

        if (role < SquareRole.Peasant || role > SquareRole.King)
        {
            Debug.LogWarning($"Ignoring a bounce in invalid square role {role}.", this);
            return;
        }

        lastTrackedSquareIndex = (int)role - 1;
        if (lastBouncedRole == role)
        {
            bounceCount++;
        }
        else
        {
            lastBouncedRole = role;
            bounceCount = 1;
        }

        if (bounceCount > 1)
        {
            int squareIndex = (int)role - 1;
            string squareName = GetSquareName(squareIndex);
            if (TryGetPlayerInSquare(squareIndex, role, out FourSquarePlayer player, out SquareRole playerRole))
            {
                string playerName = string.IsNullOrWhiteSpace(player.playerName)
                    ? string.IsNullOrWhiteSpace(player.playerId) ? "Unknown Player" : player.playerId
                    : player.playerName;
                Debug.Log(
                    $"[FourSquare] Player {playerName}/{player.playerId} in {squareName} " +
                    "got eliminated due to a double bounce!");
                EliminateForFault(playerRole, "The ball bounced twice in the same square.");
            }
            else
            {
                HandleEmptySquareDoubleBounce(squareName);
            }
        }
    }

    private void RegisterBallEnteredSquare(int squareIndex)
    {
        if (currentState != FoursquareGameState.Playing || !HasGameplayAuthority)
            return;

        if (squareIndex < 0 || squareIndex >= FoursquareCourt.SquareCount)
        {
            lastTrackedSquareIndex = -1;
            return;
        }

        if (lastTrackedSquareIndex >= 0 && lastTrackedSquareIndex != squareIndex)
        {
            lastBouncedRole = SquareRole.Spectator;
            bounceCount = 0;
        }

        lastTrackedSquareIndex = squareIndex;
    }

    public bool RegisterPlayerHit(SquareRole hitterRole, int targetSquareIndex)
    {
        if (currentState != FoursquareGameState.Playing || !HasGameplayAuthority)
            return false;

        Debug.Log(
            $"[FourSquare] Hit attempt: hitter={hitterRole}, lastBounce={lastBouncedRole}, " +
            $"bounceCount={bounceCount}, targetSquare={targetSquareIndex}.",
            this);

        if (!IsActiveRole(hitterRole))
        {
            Debug.LogWarning($"Ignoring a hit from unoccupied square role {hitterRole}.", this);
            return false;
        }

        if (lastBouncedRole != hitterRole || bounceCount != 1)
        {
            EliminateForFault(hitterRole, "The player hit the ball before it bounced once in their square.");
            return false;
        }

        if (targetSquareIndex < 0 || targetSquareIndex >= FoursquareCourt.SquareCount)
        {
            EliminateForFault(hitterRole, "The player hit the ball out of bounds.");
            return false;
        }

        SquareRole targetRole = (SquareRole)(targetSquareIndex + 1);
        if (targetRole == hitterRole)
        {
            EliminateForFault(hitterRole, "The player hit the ball into their own square.");
            return false;
        }

        lastPlayerRole = hitterRole;
        lastBouncedRole = SquareRole.Spectator;
        bounceCount = 0;
        lastTrackedSquareIndex = -1;
        return true;
    }

    public bool TryBeginServe(SquareRole serverRole)
    {
        if (currentState != FoursquareGameState.WaitingToServe ||
            !HasGameplayAuthority ||
            serverRole != ServingRole ||
            !IsActiveRole(serverRole))
        {
            return false;
        }

        ResetRuleState();
        lastPlayerRole = serverRole;
        SetState(FoursquareGameState.Playing);
        BroadcastState(FoursquareGameState.Playing);
        return true;
    }

    public void PrepareForServe()
    {
        if (!HasGameplayAuthority ||
            gameStateManager == null ||
            !gameStateManager.HasGameStarted)
        {
            return;
        }

        ResetRuleState();
        SetState(FoursquareGameState.WaitingToServe);
        BroadcastState(FoursquareGameState.WaitingToServe);
    }

    public void ReportOutOfBounds()
    {
        if (currentState != FoursquareGameState.Playing || !HasGameplayAuthority)
            return;

        SquareRole responsibleRole = IsActiveRole(lastPlayerRole)
            ? lastPlayerRole
            : lastBouncedRole;
        EliminateForFault(responsibleRole, "The ball left the court.");
    }

    public void ApplyNetworkState(int stateValue)
    {
        if (!Enum.IsDefined(typeof(FoursquareGameState), stateValue))
        {
            Debug.LogError($"Received invalid Foursquare game state value {stateValue}.", this);
            return;
        }

        SetState((FoursquareGameState)stateValue);
        if (currentState == FoursquareGameState.Playing ||
            currentState == FoursquareGameState.WaitingToServe)
            ResetRuleState();
    }

    public void ResetRuleState()
    {
        lastBouncedRole = SquareRole.Spectator;
        lastPlayerRole = SquareRole.King;
        bounceCount = 0;
        lastTrackedSquareIndex = -1;
    }

    private void HandleGameStarted()
    {
        gameStateManager?.EnsureSoloPlayerKing();
        CaptureRankingSnapshot();
        ResetRuleState();
        SetState(FoursquareGameState.WaitingToServe);
        court?.TeleportAllPlayersToAssignedQuadrants();
    }

    private void EliminateForFault(SquareRole role, string reason)
    {
        if (Time.frameCount == lastEliminationFrame ||
            !HasGameplayAuthority ||
            !IsActiveRole(role) ||
            gameStateManager == null)
        {
            if (!IsActiveRole(role) || gameStateManager == null)
                Debug.LogWarning($"Cannot apply Four Square fault: {reason} Responsible role={role}.", this);
            return;
        }

        lastEliminationFrame = Time.frameCount;
        Debug.LogWarning($"[FourSquare] RULE VIOLATION: {reason} Eliminating role {role}.", this);
        if (!gameStateManager.EliminatePlayer(role))
            return;

        CaptureRankingSnapshot();
        OnPlayerEliminated?.Invoke(role);
        SetState(FoursquareGameState.RoundOver);
        BroadcastState(FoursquareGameState.RoundOver);
        court?.TeleportAllPlayersToAssignedQuadrants();
        networkBall?.ResetBall();
        ResetRuleState();
        if (networkBall != null)
        {
            Debug.Log(
                $"[FourSquare] Elimination complete. Remaining players were repositioned; " +
                $"the ball is reset for the King serve at {networkBall.transform.position}.",
                networkBall);
        }
    }

    private void HandleEmptySquareDoubleBounce(string squareName)
    {
        if (Time.frameCount == lastEliminationFrame || !HasGameplayAuthority)
            return;

        lastEliminationFrame = Time.frameCount;
        Debug.Log($"[FourSquare] {squareName} rank got eliminated (empty square)!");
        CaptureRankingSnapshot();
        SetState(FoursquareGameState.RoundOver);
        BroadcastState(FoursquareGameState.RoundOver);
        court?.TeleportAllPlayersToAssignedQuadrants();
        networkBall?.ResetBall();
        ResetRuleState();
    }

    private bool TryGetPlayerInSquare(
        int squareIndex,
        SquareRole squareRole,
        out FourSquarePlayer player,
        out SquareRole playerRole)
    {
        player = null;
        playerRole = SquareRole.Spectator;
        if (gameStateManager == null)
            return false;

        CaptureRankingSnapshot();
        foreach (FourSquarePlayer rankedPlayer in rankingSnapshot)
        {
            if (rankedPlayer.currentRole != squareRole)
                continue;

            player = rankedPlayer;
            playerRole = rankedPlayer.currentRole;
            return true;
        }

        if (court == null)
            return false;

        FourSquareFusionPlayer[] networkPlayers =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer networkPlayer in networkPlayers)
        {
            if (networkPlayer == null || networkPlayer.PlayerRole == SquareRole.Spectator)
                continue;

            Vector3 position = networkPlayer.Object != null && networkPlayer.Object.IsValid
                ? networkPlayer.NetworkRootPosition
                : networkPlayer.transform.position;
            if (!court.TryGetSquareIndex(position, out int occupiedSquareIndex) ||
                occupiedSquareIndex != squareIndex)
            {
                continue;
            }

            foreach (FourSquarePlayer rankedPlayer in rankingSnapshot)
            {
                if (rankedPlayer.playerId != networkPlayer.PlayerId)
                    continue;

                player = rankedPlayer;
                playerRole = rankedPlayer.currentRole;
                return true;
            }
        }

        return false;
    }

    private string GetSquareName(int squareIndex)
    {
        CourtSquare[] squares = FindObjectsByType<CourtSquare>(FindObjectsSortMode.None);
        foreach (CourtSquare square in squares)
        {
            if (square != null && square.squareID == squareIndex + 1)
                return square.name;
        }

        return $"Square {squareIndex + 1}";
    }

    private bool IsActiveRole(SquareRole role)
    {
        if (role < SquareRole.Peasant || role > SquareRole.King ||
            gameStateManager == null)
        {
            return false;
        }

        CaptureRankingSnapshot();
        foreach (FourSquarePlayer player in rankingSnapshot)
        {
            if (player != null && player.currentRole == role)
                return true;
        }

        return false;
    }

    private void CaptureRankingSnapshot()
    {
        rankingSnapshot.Clear();
        if (gameStateManager == null)
            return;

        foreach (FourSquarePlayer player in gameStateManager.Players)
        {
            if (player == null ||
                player.currentRole < SquareRole.Peasant ||
                player.currentRole > SquareRole.King)
            {
                continue;
            }

            rankingSnapshot.Add(new FourSquarePlayer(
                player.playerId,
                player.playerName,
                player.currentRole)
            {
                score = player.score,
                eliminationsCount = player.eliminationsCount
            });
        }

        rankingSnapshot.Sort(
            (left, right) => right.currentRole.CompareTo(left.currentRole));
    }

    private void SetState(FoursquareGameState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;
        OnStateChanged?.Invoke(currentState);
    }

    private void BroadcastState(FoursquareGameState state)
    {
        if (networkBall != null && networkBall.IsNetworked && networkBall.HasGameplayAuthority)
            networkBall.RPC_NotifyGamePhase((int)state);
    }

    private void ResolveReferences()
    {
        if (gameStateManager == null)
            gameStateManager = GameStateManager.Instance;
        if (court == null)
            court = FindFirstObjectByType<FoursquareCourt>();
        if (ball == null)
            ball = FindFirstObjectByType<FoursquareBall>();
        if (networkBall == null)
            networkBall = FindFirstObjectByType<NetworkBallController>();
    }
}
