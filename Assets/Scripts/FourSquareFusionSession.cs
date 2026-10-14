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
        if (connectOnStart) await ConnectAsync();
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
                return;
            }

            GetLocalPlayerSpawn(out Vector3 playerPosition, out Quaternion playerRotation);
            NetworkObject spawnedPlayer =
                runner.Spawn(playerPrefab, playerPosition, playerRotation, runner.LocalPlayer);
            if (spawnedPlayer == null)
            {
                Debug.LogError(
                    $"Fusion started session '{sessionName}', but did not return a player object from Spawn. " +
                    "Check that the VR Player prefab is in Fusion's Network Project Config prefab table.",
                    this);
                throw new InvalidOperationException("Fusion failed to spawn the local VR Player object.");
            }

            Debug.Log(
                $"Spawned local VR Player for {runner.LocalPlayer} at {playerPosition}. " +
                $"XR camera active: {spawnedPlayer.GetComponent<FourSquareFusionPlayer>()?.IsLocalCameraReady}.",
                spawnedPlayer);

        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (runner != null)
            {
                if (runner.IsRunning) await runner.Shutdown();
                Destroy(runner.gameObject);
                runner = null;
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
        if (runnerToShutdown.IsRunning)
        {
            await runnerToShutdown.Shutdown();
        }

        if (runner == runnerToShutdown) runner = null;
        if (runnerToShutdown != null)
        {
            Destroy(runnerToShutdown.gameObject);
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
        if (runner != null && runner.IsRunning)
        {
            _ = runner.Shutdown();
        }
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
        GameStateManager.Instance?.ClearNetworkPlayers();
        PlayerJoinCourt.OnNetworkSessionShutdown();
        PlayerJoinCourt.RefreshJoinZoneAuras();
    }

    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player) { }
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
    public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
}
