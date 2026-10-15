
using System;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

public class MultiplayerRoomManager : MonoBehaviour
{
    private NetworkRunner runner;

    public string CurrentRoomCode { get; private set; }

    public bool IsConnected =>
        runner != null && runner.IsRunning;

    public int PlayerCount =>
        IsConnected ? runner.ActivePlayers.Count() : 0;

    public async Task<bool> CreateRoom(string roomCode)
    {
        return await StartRoom(roomCode, GameMode.Host);
    }

    public async Task<bool> JoinRoom(string roomCode)
    {
        return await StartRoom(
            roomCode.ToUpperInvariant(),
            GameMode.Client
        );
    }

    private async Task<bool> StartRoom(
        string roomCode,
        GameMode mode)
    {
        if (runner != null)
        {
            Debug.LogWarning("Already connected to a room.");
            return false;
        }

        GameObject runnerObject = null;

        try
        {
            runnerObject = new GameObject("NetworkRunner");

            runner = runnerObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;

            var result = await runner.StartGame(
                new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = roomCode,
                    PlayerCount = 4,
                    EnableClientSessionCreation = false
                }
            );

            if (result.Ok)
            {
                CurrentRoomCode = roomCode;

                Debug.Log(
                    "Connected to Photon room: " + roomCode
                );

                return true;
            }

            Debug.LogError(
                "Photon connection failed: " +
                result.ShutdownReason
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Photon room error: " + exception.Message
            );
        }

        // Clean up after a failed connection.
        if (runner != null)
        {
            try
            {
                await runner.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Runner cleanup warning: " +
                    exception.Message
                );
            }
        }

        if (runnerObject != null)
            Destroy(runnerObject);

        runner = null;
        CurrentRoomCode = null;

        return false;
    }

    public async Task LeaveRoom()
    {
        if (runner == null)
            return;

        NetworkRunner oldRunner = runner;

        try
        {
            await oldRunner.Shutdown();
        }
        finally
        {
            if (oldRunner != null)
                Destroy(oldRunner.gameObject);

            runner = null;
            CurrentRoomCode = null;
        }

        Debug.Log("Left Photon room.");
    }
}
