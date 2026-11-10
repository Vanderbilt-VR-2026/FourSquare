using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

[DisallowMultipleComponent]
public class FourSquareFusionSession : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Fusion Session")]
    [SerializeField] private string sessionName = "FourSquareVR";
    [SerializeField, Range(1, 4)] private int maximumPlayers = 4;
    [SerializeField] private bool connectOnStart = true;
#if UNITY_EDITOR
    [Header("Editor Testing")]
    [Tooltip("Checked: run this Editor client as desktop without taking over the PC XR headset. " +
             "Unchecked: keep XR enabled for VR testing in the Editor.")]
    [SerializeField] private bool disableXRInEditor;
#endif

    [Header("Network Prefabs")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform playerSpawnPoint;

    private NetworkRunner runner;
    private bool isStarting;
    private bool ownsRunner;
    private NetworkRunner playerSpawnedRunner;

    public static FourSquareFusionSession Instance { get; private set; }
    public static bool IsConfigured { get; private set; }
    public NetworkRunner Runner => runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        IsConfigured = false;
    }

    private void Awake()
    {
        Instance = this;
        IsConfigured = true;
    }

    private async void Start()
    {
        MultiplayerRoomManager roomManager = MultiplayerRoomManager.Instance;
        if (roomManager != null && roomManager.IsConnected)
        {
            AdoptRunner(roomManager.Runner);
            return;
        }

        if (connectOnStart)
            await ConnectAsync();
        else
            Debug.LogError(
                "No connected lobby Fusion runner was found. Start or join through the Start Scene before entering City.",
                this);
    }

    public void AdoptRunner(NetworkRunner existingRunner)
    {
        if (existingRunner == null || !existingRunner.IsRunning)
        {
            Debug.LogError("Cannot adopt a Fusion runner that is not running.", this);
            return;
        }

        if (runner != existingRunner)
        {
            if (runner != null)
                runner.RemoveCallbacks(this);

            runner = existingRunner;
            ownsRunner = false;
            runner.AddCallbacks(this);
        }

        SpawnLocalPlayer();
    }

    public async Task ConnectAsync()
    {
        if (isStarting || (runner != null && runner.IsRunning)) return;
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            Debug.LogError("Fusion session name cannot be empty.", this);
            return;
        }
        if (playerPrefab == null)
        {
            Debug.LogError("Assign a baked NetworkObject player prefab to the Fusion session.", this);
            return;
        }

        isStarting = true;
        try
        {
#if UNITY_EDITOR
            DisableEditorXRIfRequested();
#endif
            GameObject runnerObject = new GameObject("FourSquare Fusion Runner");
            DontDestroyOnLoad(runnerObject);
            runner = runnerObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
            runner.AddCallbacks(this);
            ownsRunner = true;
            INetworkSceneManager sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
            INetworkObjectProvider objectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>();

            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName.Trim(),
                PlayerCount = Mathf.Clamp(maximumPlayers, 1, 4),
                SceneManager = sceneManager,
                ObjectProvider = objectProvider
            });

            if (!result.Ok)
            {
                Debug.LogError($"Could not start Fusion session '{sessionName}': {result.ShutdownReason}", this);
                Destroy(runner.gameObject);
                runner = null;
                ownsRunner = false;
                return;
            }

            SpawnLocalPlayer();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (runner != null)
            {
                if (runner.IsRunning) await runner.Shutdown();
                Destroy(runner.gameObject);
                runner = null;
                ownsRunner = false;
            }
        }
        finally
        {
            isStarting = false;
        }
    }

#if UNITY_EDITOR
    private void DisableEditorXRIfRequested()
    {
        if (!disableXRInEditor) return;

        UnityEngine.XR.Management.XRManagerSettings manager =
            UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
        if (manager == null || manager.activeLoader == null) return;

        manager.StopSubsystems();
        manager.DeinitializeLoader();
        Debug.Log(
            "Stopped the Unity Editor XR loader for desktop multiplayer testing. " +
            "Quest builds are unaffected.",
            this);
    }
#endif

    public async Task DisconnectAsync()
    {
        if (runner == null) return;

        NetworkRunner runnerToShutdown = runner;
        if (!ownsRunner)
        {
            MultiplayerRoomManager roomManager = MultiplayerRoomManager.Instance;
            if (roomManager != null && roomManager.Runner == runnerToShutdown)
            {
                await roomManager.LeaveRoom();
                return;
            }

            Debug.LogWarning(
                "This scene is using a runner owned by another session manager and cannot disconnect it.",
                this);
            return;
        }

        if (runnerToShutdown.IsRunning)
        {
            await runnerToShutdown.Shutdown();
        }

        if (runner == runnerToShutdown) runner = null;
        if (runnerToShutdown != null)
        {
            Destroy(runnerToShutdown.gameObject);
        }
        ownsRunner = false;
    }

    private void SpawnLocalPlayer()
    {
        if (runner == null || !runner.IsRunning || playerSpawnedRunner == runner)
            return;

        if (playerPrefab == null)
        {
            Debug.LogError("Assign a baked NetworkObject player prefab to the Fusion session.", this);
            return;
        }

        GetLocalPlayerSpawn(out Vector3 playerPosition, out Quaternion playerRotation);
        NetworkObject spawnedPlayer =
            runner.Spawn(playerPrefab, playerPosition, playerRotation, runner.LocalPlayer);
        if (spawnedPlayer == null)
        {
            Debug.LogError(
                $"Fusion session '{runner.SessionInfo.Name}' did not return a player object from Spawn. " +
                "Check that the VR Player prefab is in Fusion's Network Project Config prefab table.",
                this);
            return;
        }

        playerSpawnedRunner = runner;
        Debug.Log(
            $"Spawned local VR Player for {runner.LocalPlayer} at {playerPosition}. " +
            $"XR camera active: {spawnedPlayer.GetComponent<FourSquareFusionPlayer>()?.IsLocalCameraReady}.",
            spawnedPlayer);

        EnsureNetworkBallSpawned();
    }

    private void EnsureNetworkBallSpawned()
    {
        DisableLocalTestBall();

        if (runner == null || !runner.IsRunning || !runner.IsSharedModeMasterClient)
            return;

        if (NetworkBallController.Instance != null || FindAnyObjectByType<NetworkBallController>() != null)
            return;

        GameObject prefabToSpawn = null;
        Transform spawnPoint = null;

        GameStateManager stateManager = GameStateManager.Instance;
        if (stateManager != null)
        {
            prefabToSpawn = stateManager.BallPrefab;
            spawnPoint = stateManager.BallSpawnPoint;
        }

        if (prefabToSpawn == null)
        {
            Debug.LogError("No ball prefab found in GameStateManager to spawn network ball.", this);
            return;
        }

        NetworkObject networkBallPrefab = prefabToSpawn.GetComponent<NetworkObject>();
        if (networkBallPrefab == null)
        {
            Debug.LogError("The ball prefab must contain a NetworkObject component.", prefabToSpawn);
            return;
        }

        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : transform.position;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        NetworkObject spawnedBall = runner.Spawn(networkBallPrefab, spawnPosition, spawnRotation);
        if (spawnedBall != null)
        {
            Debug.Log($"[FourSquareFusionSession] Master Client successfully spawned network ball at {spawnPosition}.", spawnedBall);
        }
    }

    private static void DisableLocalTestBall()
    {
        BallTrajectoryController[] testBalls = FindObjectsByType<BallTrajectoryController>(FindObjectsSortMode.None);
        foreach (BallTrajectoryController testBall in testBalls)
        {
            if (testBall != null && testBall.gameObject.name == "Test Ball")
            {
                testBall.gameObject.SetActive(false);
                Debug.Log("[FourSquareFusionSession] Disabled local Test Ball in City to avoid duplicate physical balls.", testBall);
            }
        }
    }

    private void GetLocalPlayerSpawn(out Vector3 position, out Quaternion rotation)
    {
        Unity.XR.CoreUtils.XROrigin[] existingRigs =
            FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsSortMode.None);
        position = playerSpawnPoint != null ? playerSpawnPoint.position : transform.position;
        rotation = playerSpawnPoint != null ? playerSpawnPoint.rotation : transform.rotation;

        if (playerSpawnPoint == null)
        {
            Debug.LogWarning(
                "No player spawn point is assigned. The VR Player will spawn at the Fusion session object's position. " +
                "Assign Player Spawn Point in the City scene.",
                this);
        }

        foreach (Unity.XR.CoreUtils.XROrigin rig in existingRigs)
        {
            rig.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsConfigured = false;
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
            if (ownsRunner && runner.IsRunning)
                _ = runner.Shutdown();
        }

        runner = null;
        playerSpawnedRunner = null;
        ownsRunner = false;
    }

    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
    {
        if (callbackRunner != runner) return;

        GameStateManager.Instance?.UnregisterNetworkPlayer(player);
        PlayerJoinCourt.OnNetworkPlayerLeft(player);
        PlayerJoinCourt.RefreshJoinZoneAuras();
    }

    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
    {
        if (callbackRunner != runner) return;

        runner = null;
        playerSpawnedRunner = null;
        OnSessionShutdown();
    }

    public void OnSessionShutdown()
    {
        GameStateManager.Instance?.ClearNetworkPlayers();
        PlayerJoinCourt.OnNetworkSessionShutdown();
        PlayerJoinCourt.RefreshJoinZoneAuras();
    }

    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
    {
        if (callbackRunner == runner && player == callbackRunner.LocalPlayer)
            SpawnLocalPlayer();
    }
    public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner callbackRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(
        NetworkRunner callbackRunner,
        PlayerRef player,
        ReliableKey key,
        ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(
        NetworkRunner callbackRunner,
        PlayerRef player,
        ReliableKey key,
        float progress) { }
    public void OnSceneLoadDone(NetworkRunner callbackRunner)
    {
        if (callbackRunner == runner)
        {
            SpawnLocalPlayer();
            EnsureNetworkBallSpawned();
        }
    }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
}
