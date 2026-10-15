using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#endif

public class MultiplayerRoomManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Lobby Session")]
    [SerializeField, Range(1, 4)] private int maximumPlayers = 4;
    [SerializeField, Range(1, 4)] private int requiredPlayersCount = 2;

    [Header("Scene Transition")]
    [SerializeField] private string cityScenePath = "Assets/Scenes/City.unity";
#if UNITY_EDITOR
    [Header("Editor Testing")]
    [Tooltip("Run this Editor client as desktop without taking over an active headset.")]
    [SerializeField] private bool disableXRInEditor;
#endif

    private NetworkRunner runner;
    private bool isLoadingCity;
    private bool isStartingRoom;

    public static MultiplayerRoomManager Instance { get; private set; }
    public NetworkRunner Runner => runner;
    public int MaximumPlayers => Mathf.Clamp(maximumPlayers, 1, 4);
    public int RequiredPlayersCount => Mathf.Clamp(requiredPlayersCount, 1, MaximumPlayers);
    public string CurrentRoomCode { get; private set; }
    public bool IsConnected => runner != null && runner.IsRunning;
    public int PlayerCount => IsConnected ? runner.ActivePlayers.Count() : 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second MultiplayerRoomManager was found; destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
#if UNITY_EDITOR
        DisableSimulatorForActiveOpenXRHeadset();
#endif
    }

#if UNITY_EDITOR
    private void DisableSimulatorForActiveOpenXRHeadset()
    {
        UnityEngine.XR.Management.XRManagerSettings manager =
            UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
        if (!UnityEngine.XR.XRSettings.isDeviceActive ||
            manager?.activeLoader == null ||
            !manager.activeLoader.GetType().Name.Contains("OpenXR"))
        {
            return;
        }

        bool disabledSimulator = false;
        XRInteractionSimulator interactionSimulator =
            FindAnyObjectByType<XRInteractionSimulator>();
        if (interactionSimulator != null && interactionSimulator.enabled)
        {
            interactionSimulator.enabled = false;
            disabledSimulator = true;
        }

        XRDeviceSimulator deviceSimulator =
            FindAnyObjectByType<XRDeviceSimulator>();
        if (deviceSimulator != null && deviceSimulator.enabled)
        {
            deviceSimulator.enabled = false;
            disabledSimulator = true;
        }

        if (disabledSimulator)
        {
            Debug.LogWarning(
                "Disabled the XR simulator because a real OpenXR headset is active. " +
                "Use the simulator only for desktop Editor testing without a headset.",
                this);
        }
    }
#endif

    public Task<bool> CreateRoom(string roomCode)
    {
        return StartRoom(roomCode, createRoom: true);
    }

    public Task<bool> JoinRoom(string roomCode)
    {
        return StartRoom(roomCode, createRoom: false);
    }

    private async Task<bool> StartRoom(string roomCode, bool createRoom)
    {
        if (runner != null)
        {
            Debug.LogWarning("Already connected to a room.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            Debug.LogError("Fusion room code cannot be empty.", this);
            return false;
        }

        roomCode = roomCode.Trim().ToUpperInvariant();
        GameObject runnerObject = new GameObject("FourSquare NetworkRunner");
        isStartingRoom = true;

        try
        {
#if UNITY_EDITOR
            DisableEditorXRIfRequested();
#endif
            runner = runnerObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
            runner.AddCallbacks(this);

            INetworkSceneManager sceneManager =
                runnerObject.AddComponent<NetworkSceneManagerDefault>();
            INetworkObjectProvider objectProvider =
                runnerObject.AddComponent<NetworkObjectProviderDefault>();

            Scene activeScene = SceneManager.GetActiveScene();
            NetworkSceneInfo initialScene = new NetworkSceneInfo();
            if (activeScene.buildIndex >= 0)
            {
                initialScene.AddSceneRef(
                    SceneRef.FromIndex(activeScene.buildIndex),
                    LoadSceneMode.Single);
            }

            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = roomCode,
                PlayerCount = MaximumPlayers,
                EnableClientSessionCreation = createRoom,
                Scene = initialScene,
                SceneManager = sceneManager,
                ObjectProvider = objectProvider
            });

            if (!result.Ok)
            {
                Debug.LogError(
                    $"Could not {(createRoom ? "create" : "join")} Fusion Shared Mode session " +
                    $"'{roomCode}': {result.ShutdownReason}",
                    this);
                await CleanupFailedConnection(runnerObject);
                return false;
            }

            CurrentRoomCode = roomCode;
            isLoadingCity = false;
            isStartingRoom = false;
            if (transform.parent != null)
                transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
            DontDestroyOnLoad(runnerObject);
#if UNITY_EDITOR
            KeepDeviceSimulatorAcrossSceneLoads();
#endif

            Debug.Log(
                $"Connected to Fusion Shared Mode session '{roomCode}' as " +
                $"{(runner.IsSharedModeMasterClient ? "Master Client" : "Client")}.",
                this);

            TryStartCityTransition();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            await CleanupFailedConnection(runnerObject);
            return false;
        }
    }

#if UNITY_EDITOR
    private void KeepDeviceSimulatorAcrossSceneLoads()
    {
        XRInteractionSimulator interactionSimulator =
            FindAnyObjectByType<XRInteractionSimulator>();
        if (interactionSimulator != null && interactionSimulator.isActiveAndEnabled)
        {
            DontDestroyOnLoad(interactionSimulator.transform.root.gameObject);
            return;
        }

        XRDeviceSimulator simulator =
            FindAnyObjectByType<XRDeviceSimulator>();
        if (simulator == null || !simulator.isActiveAndEnabled)
        {
            Debug.LogWarning(
                "No active XR Device Simulator was found in the Editor. Add its prefab to the Start Scene " +
                "and assign the simulator and controller action assets before testing simulated hands.",
                this);
            return;
        }

        DontDestroyOnLoad(simulator.transform.root.gameObject);
    }
#endif

#if UNITY_EDITOR
    private void DisableEditorXRIfRequested()
    {
        if (!disableXRInEditor)
            return;

        UnityEngine.XR.Management.XRManagerSettings manager =
            UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
        if (manager == null || manager.activeLoader == null)
            return;

        manager.StopSubsystems();
        manager.DeinitializeLoader();
        Debug.Log(
            "Stopped the Unity Editor XR loader for desktop multiplayer testing. " +
            "Quest builds are unaffected.",
            this);
    }
#endif

    private async Task CleanupFailedConnection(GameObject runnerObject)
    {
        NetworkRunner failedRunner = runner;
        runner = null;
        CurrentRoomCode = null;

        if (failedRunner != null && failedRunner.IsRunning)
        {
            try
            {
                await failedRunner.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Runner cleanup failed: {exception.Message}", this);
            }
        }

        if (runnerObject != null)
            Destroy(runnerObject);
        isStartingRoom = false;
    }

    private void TryStartCityTransition()
    {
        if (!IsConnected ||
            !runner.IsSharedModeMasterClient ||
            isStartingRoom ||
            isLoadingCity ||
            PlayerCount < RequiredPlayersCount)
        {
            return;
        }

        int cityBuildIndex = SceneUtility.GetBuildIndexByScenePath(cityScenePath);
        if (cityBuildIndex < 0)
        {
            Debug.LogError(
                $"City scene '{cityScenePath}' is not enabled in Build Settings.",
                this);
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex == cityBuildIndex)
            return;

        isLoadingCity = true;
        Debug.Log(
            $"Lobby threshold reached ({PlayerCount}/{RequiredPlayersCount}); " +
            $"loading City for the Fusion session.",
            this);
        runner.LoadScene(
            SceneRef.FromIndex(cityBuildIndex),
            new LoadSceneParameters(LoadSceneMode.Single),
            setActiveOnLoad: true);
    }

    public async Task LeaveRoom()
    {
        NetworkRunner runnerToShutdown = runner;
        if (runnerToShutdown == null)
            return;

        runner = null;
        CurrentRoomCode = null;
        isLoadingCity = false;

        try
        {
            if (runnerToShutdown.IsRunning)
                await runnerToShutdown.Shutdown();
        }
        finally
        {
            if (runnerToShutdown != null)
                Destroy(runnerToShutdown.gameObject);

        }

        Debug.Log("Left Fusion session.");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (runner != null)
        {
            runner.RemoveCallbacks(this);
            if (runner.IsRunning)
                _ = runner.Shutdown();
        }
    }

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
    {
        if (callbackRunner != runner)
            return;

        Debug.Log($"Fusion player {player} joined; session count is {PlayerCount}.", this);
        TryStartCityTransition();
    }

    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player) { }

    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
    {
        if (callbackRunner != runner)
            return;

        runner = null;
        CurrentRoomCode = null;
        isLoadingCity = false;
        FourSquareFusionSession.Instance?.OnSessionShutdown();
        Debug.LogWarning($"Fusion session shut down: {shutdownReason}.", this);
    }

    public void OnSceneLoadDone(NetworkRunner callbackRunner)
    {
        if (callbackRunner != runner)
            return;

        FourSquareFusionSession sceneSession =
            FindAnyObjectByType<FourSquareFusionSession>();
        if (sceneSession != null)
            sceneSession.AdoptRunner(callbackRunner);
    }

    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner callbackRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
}
