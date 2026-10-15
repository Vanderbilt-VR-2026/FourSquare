
using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomLobbyUI : MonoBehaviour
{
    [Header("Multiplayer")]
    [SerializeField]
    private MultiplayerRoomManager multiplayerRoomManager;

    [Header("Start Screen Navigation")]
    [SerializeField]
    private StartScreenUI startScreenUI;

    [Header("Lobby UI")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private TMP_Text playerListText;
    [SerializeField] private Button leaveRoomButton;

    private bool isLeaving = false;

    private void OnEnable()
    {
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.AddListener(LeaveRoom);
    }

    private void OnDisable()
    {
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.RemoveListener(LeaveRoom);
    }

    private void Update()
    {
        if (lobbyPanel == null ||
            !lobbyPanel.activeInHierarchy ||
            multiplayerRoomManager == null)
        {
            return;
        }

        UpdateLobby();
    }

    private void UpdateLobby()
    {
        int count = Mathf.Clamp(
            multiplayerRoomManager.PlayerCount,
            0,
            multiplayerRoomManager.MaximumPlayers
        );

        // Display the current Photon room code.
        if (roomCodeText != null)
        {
            string code =
                multiplayerRoomManager.CurrentRoomCode;

            roomCodeText.text = "ROOM CODE: " +
                (string.IsNullOrEmpty(code) ? "----" : code);
        }

        // Display the number of connected players.
        if (playerCountText != null)
        {
            playerCountText.text =
                "PLAYERS: " + count + "/" + multiplayerRoomManager.MaximumPlayers +
                "\nCITY STARTS AT: " +
                multiplayerRoomManager.RequiredPlayersCount;
        }

        // Display four player slots.
        if (playerListText != null)
        {
            StringBuilder text = new StringBuilder();

            for (int i = 1; i <= multiplayerRoomManager.MaximumPlayers; i++)
            {
                if (i <= count)
                {
                    text.AppendLine(
                        "Player " + i + " - Connected"
                    );
                }
                else
                {
                    text.AppendLine(
                        "Waiting for Player " + i + "..."
                    );
                }
            }

            playerListText.text = text.ToString();
        }
    }

    private async void LeaveRoom()
    {
        if (isLeaving || multiplayerRoomManager == null)
            return;

        isLeaving = true;

        if (leaveRoomButton != null)
            leaveRoomButton.interactable = false;

        try
        {
            // Disconnect from the Photon Fusion session.
            await multiplayerRoomManager.LeaveRoom();

            Debug.Log("Successfully left the multiplayer room.");

            // Hide the lobby panel.
            if (lobbyPanel != null)
                lobbyPanel.SetActive(false);

            // Return to the FourSquare Start Screen.
            if (startScreenUI != null)
            {
                startScreenUI.ReturnToStartScreen();
            }
            else
            {
                Debug.LogWarning(
                    "StartScreenUI reference is missing."
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Leave Room error: " + exception.Message
            );
        }
        finally
        {
            isLeaving = false;

            if (leaveRoomButton != null)
                leaveRoomButton.interactable = true;
        }
    }
}
