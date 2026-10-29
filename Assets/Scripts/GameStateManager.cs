using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class GameStateManager : MonoBehaviour
{
    private const float PlayerTeleportTimeout = 5f;
    private const float PlayerTeleportPollInterval = 0.1f;

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

    [Header("Game Start")]
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private Transform ballSpawnPoint;
    [SerializeField, Min(0f)] private float ballSpawnDelay = 0.5f;
    [SerializeField] private bool hasGameStarted;
    private bool gameStartEffectsApplied;
    private bool isStartingGame;

    [Header("Persistent Session State")]
    [SerializeField] private List<FourSquarePlayer> playerList = new List<FourSquarePlayer>();

    // Fast lookup for the 4 active roles
    private Dictionary<SquareRole, FourSquarePlayer> activePlayers = new Dictionary<SquareRole, FourSquarePlayer>();
    private readonly Dictionary<string, FourSquareFusionPlayer> networkPlayers = new Dictionary<string, FourSquareFusionPlayer>();

    // Getters
    public GameObject CurrentBallCarrier => currentBallCarrier;
    public SquareRole LastSquareHit => lastSquareHit;
    public SquareRole LastTouchedByPlayerRole => lastTouchedByPlayerRole;
    public bool IsBallInPlay => isBallInPlay;
    public bool HasGameStarted => hasGameStarted;
    public Transform BallSpawnPoint => ballSpawnPoint;
    public IReadOnlyList<FourSquarePlayer> Players => playerList;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (ballSpawnPoint == null)
        {
            ballSpawnPoint = transform.Find("Ball Spawn Point");
        }
    }

    private void Start()
    {
        if (FourSquareFusionSession.IsConfigured)
        {
            playerList.Clear();
            foreach (FourSquareFusionPlayer networkPlayer in networkPlayers.Values)
            {
                AddNetworkPlayerToPlayerList(networkPlayer);
            }
        }

        InitializeDictionary();
    }

    private BallController SpawnBall()
    {
        if (ballPrefab == null)
        {
            Debug.LogWarning("GameStateManager needs a ball prefab to spawn the game ball.", this);
            return null;
        }

        Vector3 position = ballSpawnPoint != null ? ballSpawnPoint.position : transform.position;
        Quaternion rotation = ballSpawnPoint != null ? ballSpawnPoint.rotation : Quaternion.identity;
        GameObject ballObject = Instantiate(ballPrefab, position, rotation);
        BallController ball = ballObject.GetComponent<BallController>();
        if (ball == null)
        {
            Debug.LogError("The configured ball prefab has no BallController.", ballObject);
        }
        return ball;
    }

    public BallController SpawnNetworkBall(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning)
        {
            Debug.LogError("A running NetworkRunner is required to spawn the networked ball.", this);
            return null;
        }
        if (!runner.IsSharedModeMasterClient)
        {
            Debug.LogError("Only the Shared Mode Master Client can spawn the networked game ball.", this);
            return null;
        }
        if (ballPrefab == null)
        {
            Debug.LogError("GameStateManager needs a ball prefab to spawn the networked game ball.", this);
            return null;
        }
        NetworkObject networkBallPrefab = ballPrefab.GetComponent<NetworkObject>();
        if (networkBallPrefab == null)
        {
            Debug.LogError("The ball prefab must contain a baked NetworkObject before it can be spawned by Fusion.", ballPrefab);
            return null;
        }

        Vector3 position = ballSpawnPoint != null ? ballSpawnPoint.position : transform.position;
        Quaternion rotation = ballSpawnPoint != null ? ballSpawnPoint.rotation : Quaternion.identity;
        NetworkObject networkBall = runner.Spawn(networkBallPrefab, position, rotation);
        BallController ball = networkBall != null ? networkBall.GetComponent<BallController>() : null;
        if (ball == null)
        {
            Debug.LogError("Fusion spawned the ball prefab without a BallController.", networkBall);
        }
        return ball;
    }

    public void RegisterNetworkPlayer(FourSquareFusionPlayer networkPlayer)
    {
        if (networkPlayer == null) return;
        if (!networkPlayer.HasPlayerInputAuthority)
        {
            Debug.LogError(
                "Ignoring a Fusion player registration without a real PlayerRef. " +
                "Check that only the VR Player prefab has NetworkObject/FourSquareFusionPlayer components.",
                networkPlayer);
            return;
        }
        if (string.IsNullOrEmpty(networkPlayer.PlayerId)) return;
        bool isNewPlayer = !networkPlayers.ContainsKey(networkPlayer.PlayerId);
        if (isNewPlayer)
        {
            networkPlayers.Add(networkPlayer.PlayerId, networkPlayer);
        }
        else
        {
            networkPlayers[networkPlayer.PlayerId] = networkPlayer;
        }

        AddNetworkPlayerToPlayerList(networkPlayer);
        InitializeDictionary();

        if (isNewPlayer)
        {
            Debug.Log(
                $"[FourSquare] Player joined: {networkPlayer.DisplayName} (ID {networkPlayer.PlayerId}) | " +
                $"Players connected: {networkPlayers.Count}/4 | Current ranking: {networkPlayer.PlayerRole}.",
                networkPlayer);
        }
    }

    public void RegisterPlayer(string playerId, SquareRole role)
    {
        if (string.IsNullOrWhiteSpace(playerId)
            || role == SquareRole.Spectator
            || !System.Enum.IsDefined(typeof(SquareRole), role))
        {
            return;
        }

        FourSquarePlayer player = playerList.Find(existing => existing.playerId == playerId);
        if (player == null)
        {
            playerList.Add(new FourSquarePlayer(playerId, "Player " + playerId, role));
        }
        else
        {
            player.currentRole = role;
        }

        InitializeDictionary();
    }

    public void UnregisterNetworkPlayer(PlayerRef playerRef)
    {
        string playerId = playerRef.ToString();
        bool removed = networkPlayers.Remove(playerId);
        playerList.RemoveAll(player => player != null && player.playerId == playerId);
        InitializeDictionary();

        if (removed)
        {
            Debug.Log(
                $"[FourSquare] Player left: {playerId} | Players connected: " +
                $"{networkPlayers.Count}/4.",
                this);
        }
    }

    public void ClearNetworkPlayers()
    {
        networkPlayers.Clear();
        playerList.Clear();
        InitializeDictionary();
    }

    private void AddNetworkPlayerToPlayerList(FourSquareFusionPlayer networkPlayer)
    {
        foreach (FourSquarePlayer player in playerList)
        {
            if (player.playerId == networkPlayer.PlayerId)
            {
                player.currentRole = networkPlayer.PlayerRole;
                return;
            }
        }

        playerList.Add(new FourSquarePlayer(networkPlayer.PlayerId, "Player " + networkPlayer.PlayerId,
            networkPlayer.PlayerRole));
    }

    public void UpdateNetworkPlayerRole(FourSquareFusionPlayer networkPlayer)
    {
        if (networkPlayer == null || string.IsNullOrEmpty(networkPlayer.PlayerId)) return;

        foreach (FourSquarePlayer player in playerList)
        {
            if (player.playerId != networkPlayer.PlayerId || player.currentRole == networkPlayer.PlayerRole) continue;

            SquareRole oldRole = player.currentRole;
            player.currentRole = networkPlayer.PlayerRole;
            InitializeDictionary();
            OnPlayerRoleChanged?.Invoke(player, oldRole, player.currentRole);
            return;
        }
    }

    public void ApplyNetworkBallHit(SquareRole playerRole)
    {
        currentBallCarrier = null;
        lastTouchedByPlayerRole = playerRole;
        lastSquareHit = SquareRole.Spectator;
        isBallInPlay = true;
    }

    public void StartGame()
    {
        Debug.Log("[FourSquare] Start Game button was pressed.", this);
        if (hasGameStarted)
        {
            Debug.LogWarning("[FourSquare] Start request ignored because the game is already running.", this);
            return;
        }

        if (!PlayerJoinCourt.CanStartGame)
        {
            Debug.LogWarning(
                $"Four Square cannot start yet: the minimum player count has not been met. " +
                $"Connected players: {PlayerJoinCourt.ConnectedPlayerCount}.", this);
            return;
        }

        if (FourSquareFusionSession.IsConfigured)
        {
            FourSquareFusionPlayer[] players =
                UnityEngine.Object.FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
            foreach (FourSquareFusionPlayer player in players)
            {
                if (player != null && player.IsLocallyOwned)
                {
                    player.RequestGameStart();
                    return;
                }
            }

            Debug.LogError("Cannot request a networked game start because no locally owned player exists.", this);
            return;
        }

        StartGameAsAuthority(null);
    }

    public void StartGameAsAuthority(NetworkRunner runner)
    {
        if (hasGameStarted || isStartingGame) return;
        if (runner != null && !runner.IsSharedModeMasterClient) return;

        if (!PlayerJoinCourt.CanStartGame)
        {
            Debug.LogWarning(
                $"Four Square cannot start yet. Players in court: {PlayerJoinCourt.ConnectedPlayerCount}.", this);
            return;
        }

        isStartingGame = true;
        StartCoroutine(StartGameAfterDelay(runner));
    }

    private IEnumerator StartGameAfterDelay(NetworkRunner runner)
    {
        if (runner != null && !AssignStartingRoles())
        {
            isStartingGame = false;
            yield break;
        }

        if (ballSpawnDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(ballSpawnDelay);
        }

        PlayerJoinCourt.TeleportJoinedPlayersToAssignedRoles();
        if (runner != null)
        {
            float waitTime = 0f;
            FourSquareFusionPlayer[] assignedPlayers =
                UnityEngine.Object.FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
            while (!PlayerJoinCourt.ArePlayersAtAssignedSquares(assignedPlayers, out int playersNotAtSquare))
            {
                if (hasGameStarted || !runner.IsRunning || !runner.IsSharedModeMasterClient)
                {
                    isStartingGame = false;
                    yield break;
                }

                if (waitTime >= PlayerTeleportTimeout)
                {
                    Debug.LogError(
                        $"Game start cancelled because {playersNotAtSquare} assigned player(s) did not " +
                        $"reach their court squares within {PlayerTeleportTimeout:F0} seconds. No ball was spawned.",
                        this);
                    isStartingGame = false;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(PlayerTeleportPollInterval);
                waitTime += PlayerTeleportPollInterval;
                assignedPlayers =
                    UnityEngine.Object.FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
            }
        }

        if (hasGameStarted || (runner != null && (!runner.IsRunning || !runner.IsSharedModeMasterClient)))
        {
            isStartingGame = false;
            yield break;
        }

        if (!PlayerJoinCourt.CanStartGame)
        {
            Debug.LogWarning("Game start cancelled because no eligible players remain in the court.", this);
            isStartingGame = false;
            yield break;
        }

        Debug.Log("[FourSquare] Preparing the game ball for the Started transition.", this);
        BallController ball = runner != null
            ? SpawnNetworkBall(runner)
            : SpawnBall();
        if (ball == null)
        {
            isStartingGame = false;
            yield break;
        }

        Debug.Log($"[FourSquare] Starting game. Players in court: {PlayerJoinCourt.ConnectedPlayerCount}; " +
            $"ball networked: {ball.IsNetworked}; State Authority: {ball.HasGameplayAuthority}.", ball);
        if (ball.StartGameFromManager(runner != null))
        {
            isStartingGame = false;
            yield break;
        }

        Debug.LogError("Game start failed while initializing the ball; removing the inactive ball.", ball);
        if (ball.IsNetworked && ball.Object != null && ball.Object.IsValid)
        {
            runner.Despawn(ball.Object);
        }
        else if (ball != null)
        {
            Destroy(ball.gameObject);
        }
        isStartingGame = false;
    }

    public bool TryAssignLateJoinRole(FourSquareFusionPlayer joiningPlayer, out SquareRole assignedRole)
    {
        assignedRole = SquareRole.Spectator;
        if (!hasGameStarted || joiningPlayer == null) return false;

        FourSquareFusionPlayer[] connectedPlayers =
            UnityEngine.Object.FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        for (int roleValue = (int)SquareRole.King; roleValue >= (int)SquareRole.Peasant; roleValue--)
        {
            SquareRole availableRole = (SquareRole)roleValue;
            bool occupied = false;
            foreach (FourSquareFusionPlayer player in connectedPlayers)
            {
                if (player != null && player != joiningPlayer && player.PlayerRole == availableRole)
                {
                    occupied = true;
                    break;
                }
            }

            if (occupied) continue;

            assignedRole = availableRole;
            joiningPlayer.RequestRole(assignedRole);
            Debug.Log($"[FourSquare] Assigned late-joining {joiningPlayer.DisplayName} to {assignedRole}.", joiningPlayer);
            return true;
        }

        Debug.LogWarning($"No court square is available for late-joining {joiningPlayer.DisplayName}.", joiningPlayer);
        return false;
    }

    public bool AssignStartingRoles()
    {
        FourSquareFusionPlayer[] connectedPlayers =
            UnityEngine.Object.FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        Array.Sort(connectedPlayers, (left, right) => left.JoinOrder.CompareTo(right.JoinOrder));

        if (connectedPlayers.Length == 0)
        {
            Debug.LogError("Cannot start Four Square because no networked players are registered.", this);
            return false;
        }

        networkPlayers.Clear();
        playerList.Clear();
        int maximumPlayers = Mathf.Min(4, connectedPlayers.Length);

        for (int i = 0; i < maximumPlayers; i++)
        {
            FourSquareFusionPlayer networkPlayer = connectedPlayers[i];
            SquareRole role = (SquareRole)((int)SquareRole.King - i);
            networkPlayer.RequestRole(role);
            networkPlayers[networkPlayer.PlayerId] = networkPlayer;
            playerList.Add(new FourSquarePlayer(networkPlayer.PlayerId, "Player " + networkPlayer.PlayerId, role));
        }

        InitializeDictionary();
        Debug.Log($"[FourSquare] Starting game with {maximumPlayers} player(s). Starting rankings:", this);
        for (int i = 0; i < maximumPlayers; i++)
        {
            FourSquareFusionPlayer networkPlayer = connectedPlayers[i];
            SquareRole role = (SquareRole)((int)SquareRole.King - i);
            Debug.Log($"[FourSquare] Rank {i + 1}: {networkPlayer.DisplayName} (ID {networkPlayer.PlayerId}) - {role}.",
                networkPlayer);
        }

        return true;
    }

    public void ApplyNetworkGameStarted()
    {
        if (gameStartEffectsApplied) return;

        hasGameStarted = true;
        gameStartEffectsApplied = true;
        isBallInPlay = true;
        currentBallCarrier = null;
        lastTouchedByPlayerRole = SquareRole.King;
        lastSquareHit = SquareRole.Spectator;
        PlayerJoinCourt.OnGameStarted();
        PlayerJoinCourt.RefreshJoinZoneAuras();

        Debug.Log($"[FourSquare] Game started. Players in ranking: {playerList.Count}.", this);
        FourSquareFusionPlayer[] rankedPlayers =
            UnityEngine.Object.FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        Array.Sort(rankedPlayers, (left, right) =>
        {
            int roleComparison = right.NetworkRole.CompareTo(left.NetworkRole);
            return roleComparison != 0 ? roleComparison : left.JoinOrder.CompareTo(right.JoinOrder);
        });
        int rank = 1;
        foreach (FourSquareFusionPlayer networkPlayer in rankedPlayers)
        {
            if (networkPlayer.PlayerRole == SquareRole.Spectator) continue;

            Debug.Log($"[FourSquare] Current rank {rank++}: {networkPlayer.DisplayName} " +
                $"(ID {networkPlayer.PlayerId}) - {networkPlayer.PlayerRole}.", networkPlayer);
        }
    }

    public void ApplyNetworkBallOut(SquareRole responsibleRole)
    {
        ApplyBallOut(responsibleRole);
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
        lastTouchedByPlayerRole = playerRole == SquareRole.Spectator && playerList.Count == 1 && playerList[0] != null
            ? playerList[0].currentRole
            : playerRole;
        lastSquareHit = SquareRole.Spectator;
    }

    public void SetBallInPlay(bool inPlay)
    {
        isBallInPlay = inPlay;
    }

    public void HandleBallOut(SquareRole responsibleRole)
    {
        BallController ball = UnityEngine.Object.FindAnyObjectByType<BallController>();
        if (ball != null && ball.IsNetworked && !ball.HasStateAuthority) return;

        ApplyBallOut(responsibleRole);

        if (ball != null && ball.IsNetworked)
        {
            ball.RPC_NotifyBallOut((int)responsibleRole);
        }
    }

    private void ApplyBallOut(SquareRole responsibleRole)
    {
        isBallInPlay = false;
        currentBallCarrier = null;

        if (responsibleRole == SquareRole.Spectator || !System.Enum.IsDefined(typeof(SquareRole), responsibleRole))
        {
            Debug.LogWarning("Cannot eliminate a player for a ball fault without a valid player role.", this);
            return;
        }

        EliminatePlayer(responsibleRole);
    }

    public void HandleBallOutsideCourt()
    {
        SquareRole responsibleRole = lastSquareHit != SquareRole.Spectator
            ? lastSquareHit
            : lastTouchedByPlayerRole;
        HandleBallOut(responsibleRole);
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
    public bool EliminatePlayer(SquareRole eliminatedRole)
    {
        if (!activePlayers.TryGetValue(eliminatedRole, out FourSquarePlayer faultPlayer))
        {
            Debug.LogWarning(
                $"[RoleTrace] Elimination ignored: no player currently has role={eliminatedRole}.",
                this);
            return false;
        }

        Debug.LogWarning(
            $"[RoleTrace] Elimination received: playerId='{faultPlayer.playerId}', " +
            $"role={eliminatedRole}, gameStarted={hasGameStarted}.",
            this);
        faultPlayer.eliminationsCount++;

        OnPlayerEliminated?.Invoke(eliminatedRole);

        // Reset ball runtime state
        isBallInPlay = false;
        currentBallCarrier = null;

        // If Peasant eliminated, no rank movements happen—Peasant stays Peasant
        if (eliminatedRole == SquareRole.Peasant)
        {
            return true;
        }

        RotateRoles(eliminatedRole);
        return true;
    }

    private void RotateRoles(SquareRole faultRole)
    {
        FourSquarePlayer faultPlayer = activePlayers[faultRole];

        for (int r = (int)faultRole - 1; r >= (int)SquareRole.Peasant; r--)
        {
            SquareRole currentRole = (SquareRole)r;
            if (!activePlayers.TryGetValue(currentRole, out FourSquarePlayer promotingPlayer)) continue;

            SquareRole nextRole = (SquareRole)(r + 1);
            promotingPlayer.currentRole = nextRole;
            SetNetworkPlayerRole(promotingPlayer, nextRole);

            OnPlayerRoleChanged?.Invoke(promotingPlayer, currentRole, nextRole);
        }

        // 2. The eliminated player goes straight to Peasant
        SquareRole oldFaultRole = faultPlayer.currentRole;
        faultPlayer.currentRole = SquareRole.Peasant;
        Debug.LogWarning(
            $"[RoleTrace] Demoting playerId='{faultPlayer.playerId}' from {oldFaultRole} to Peasant.",
            this);
        SetNetworkPlayerRole(faultPlayer, SquareRole.Peasant);
        OnPlayerRoleChanged?.Invoke(faultPlayer, oldFaultRole, SquareRole.Peasant);

        // Re-sync dictionary mapping
        InitializeDictionary();
    }

    private void SetNetworkPlayerRole(FourSquarePlayer player, SquareRole role)
    {
        if (player != null && networkPlayers.TryGetValue(player.playerId, out FourSquareFusionPlayer networkPlayer))
        {
            networkPlayer.RequestRole(role);
        }
    }
}