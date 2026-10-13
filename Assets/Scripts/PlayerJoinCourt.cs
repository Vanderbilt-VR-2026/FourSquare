using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using Unity.XR.CoreUtils;

public class PlayerJoinCourt : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Player")]
    [SerializeField] private NetworkObject playerPrefab;

    [Header("Court Spawn Points")]
    [Tooltip("Assign King, Queen, Jack, and Peasant spawn points in that order.")]
    [SerializeField] private Transform[] squareSpawnPoints = new Transform[4];

    private NetworkRunner runner;
    private bool callbacksRegistered;
    private Coroutine cameraActivationRoutine;
    private Camera activeLocalCamera;
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    private IEnumerator Start()
    {
        while (runner == null || !runner.IsRunning)
        {
            NetworkRunner sceneRunner = GetComponent<NetworkRunner>();
            if (sceneRunner != null && sceneRunner.IsRunning)
            {
                runner = sceneRunner;
            }
            else
            {
                NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                foreach (NetworkRunner candidate in runners)
                {
                    if (candidate != null && candidate.IsRunning)
                    {
                        runner = candidate;
                        break;
                    }
                }
            }

            if (runner == null || !runner.IsRunning)
            {
                yield return null;
            }
        }

        RegisterCallbacks();

        if (HasSpawnAuthority(runner))
        {
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                SpawnPlayerIfNeeded(runner, player);
            }
        }

        StartLocalCameraActivation(runner);
    }

    private void OnDisable()
    {
        if (cameraActivationRoutine != null)
        {
            StopCoroutine(cameraActivationRoutine);
            cameraActivationRoutine = null;
        }

        if (runner != null && callbacksRegistered)
        {
            runner.RemoveCallbacks(this);
            callbacksRegistered = false;
        }
    }

    private void LateUpdate()
    {
        if (activeLocalCamera == null || runner == null || !runner.IsRunning)
        {
            return;
        }

        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Camera camera in cameras)
        {
            bool isLocalCamera = camera == activeLocalCamera;
            camera.enabled = isLocalCamera;

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = isLocalCamera;
            }
        }
    }

    private void RegisterCallbacks()
    {
        if (runner == null || callbacksRegistered)
        {
            return;
        }

        runner.AddCallbacks(this);
        callbacksRegistered = true;
    }

    public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
    {
        if (networkRunner != runner)
        {
            return;
        }

        SpawnPlayerIfNeeded(networkRunner, player);

        if (player == networkRunner.LocalPlayer)
        {
            StartLocalCameraActivation(networkRunner);
        }
    }

    private void SpawnPlayerIfNeeded(NetworkRunner networkRunner, PlayerRef player)
    {
        if (!HasSpawnAuthority(networkRunner) ||
            playerPrefab == null ||
            spawnedPlayers.ContainsKey(player))
        {
            return;
        }

        SquareRole role = GetRole(spawnedPlayers.Count);
        Transform spawnPoint = GetSpawnPoint(role);
        Vector3 position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        NetworkObject playerObject = networkRunner.Spawn(playerPrefab, position, rotation, player);
        EliminationPlayer eliminationPlayer = playerObject.GetComponent<EliminationPlayer>();
        if (eliminationPlayer != null)
        {
            eliminationPlayer.InitializeNetworkPlayer(role, player.ToString());
        }

        spawnedPlayers.Add(player, playerObject);
    }

    public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
    {
        if (networkRunner != runner || !HasSpawnAuthority(networkRunner))
        {
            return;
        }

        if (spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            networkRunner.Despawn(playerObject);
            spawnedPlayers.Remove(player);
        }
    }

    private static bool HasSpawnAuthority(NetworkRunner networkRunner)
    {
        return networkRunner.IsServer || networkRunner.IsSharedModeMasterClient;
    }

    private SquareRole GetRole(int playerIndex)
    {
        switch (playerIndex)
        {
            case 0:
                return SquareRole.King;
            case 1:
                return SquareRole.Queen;
            case 2:
                return SquareRole.Jack;
            case 3:
                return SquareRole.Peasant;
            default:
                return SquareRole.Spectator;
        }
    }

    private Transform GetSpawnPoint(SquareRole role)
    {
        int index = 4 - (int)role;
        if (index < 0 || index >= squareSpawnPoints.Length)
        {
            return null;
        }

        return squareSpawnPoints[index];
    }

    public void OnInput(NetworkRunner networkRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner networkRunner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner networkRunner)
    {
        if (networkRunner == runner && networkRunner.LocalPlayer.IsValid)
        {
            StartLocalCameraActivation(networkRunner);
        }
    }
    public void OnSceneLoadStart(NetworkRunner networkRunner) { }
    public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }

    private void StartLocalCameraActivation(NetworkRunner networkRunner)
    {
        if (networkRunner != null && cameraActivationRoutine == null)
        {
            cameraActivationRoutine = StartCoroutine(ActivateLocalCameraWhenAvailable(networkRunner));
        }
    }

    private IEnumerator ActivateLocalCameraWhenAvailable(NetworkRunner networkRunner)
    {
        while (networkRunner != null)
        {
            if (!networkRunner.IsRunning)
            {
                yield return null;
                continue;
            }

            XROrigin[] origins = FindObjectsByType<XROrigin>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (XROrigin origin in origins)
            {
                NetworkObject networkObject = origin.GetComponent<NetworkObject>();
                if (networkObject == null || networkObject.InputAuthority != networkRunner.LocalPlayer)
                {
                    continue;
                }

                Camera localCamera = origin.GetComponentInChildren<Camera>(true);
                if (localCamera == null)
                {
                    continue;
                }

                if (!origin.gameObject.activeSelf)
                {
                    origin.gameObject.SetActive(true);
                }

                activeLocalCamera = localCamera;
                Camera[] cameras = FindObjectsByType<Camera>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (Camera camera in cameras)
                {
                    bool isLocalCamera = camera == localCamera;
                    camera.enabled = isLocalCamera;

                    AudioListener listener = camera.GetComponent<AudioListener>();
                    if (listener != null)
                    {
                        listener.enabled = isLocalCamera;
                    }
                }

                cameraActivationRoutine = null;
                yield break;
            }

            // Do not leave the scene's desktop camera rendering after the local
            // network player has joined. The local XR camera will replace it
            // as soon as the spawned rig is available.
            if (networkRunner.LocalPlayer.IsValid)
            {
                Camera[] cameras = FindObjectsByType<Camera>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (Camera camera in cameras)
                {
                    if (camera.GetComponentInParent<XROrigin>() == null)
                    {
                        camera.enabled = false;

                        AudioListener listener = camera.GetComponent<AudioListener>();
                        if (listener != null)
                        {
                            listener.enabled = false;
                        }
                    }
                }
            }

            yield return null;
        }

        cameraActivationRoutine = null;
    }
}
