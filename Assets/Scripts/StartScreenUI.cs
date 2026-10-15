
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StartScreenUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button createRoomBackButton;
    [SerializeField] private Button joinRoomBackButton;
    [SerializeField] private Button joinRoomConfirmButton;

    [Header("Panels")]
    [SerializeField] private GameObject startScreenPanel;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject joinRoomPanel;

    [Header("Multiplayer Lobby")]
    [SerializeField] private GameObject roomLobbyPanel;

    [Header("VR Menu Placement")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform menuCanvas;
    [SerializeField] private float menuDistance = 2f;
    [SerializeField] private float menuHeightOffset = 0.15f;
    [SerializeField] private float fallbackCameraHeight = 1.6f;
    [SerializeField] private float trackingInitializationTimeout = 5f;

    [Header("Room Creation")]
    [SerializeField] private RoomCodeManager roomCodeManager;
    [SerializeField] private TMP_Text roomCodeText;

    [Header("Join Room")]
    [SerializeField] private TMP_InputField roomCodeInputField;

    [Header("Multiplayer")]
    [SerializeField] private MultiplayerRoomManager multiplayerRoomManager;

    private string currentRoomCode;
    private bool isConnecting = false;

    private IEnumerator Start()
    {
        SetAllPanelsInactive();

        if (playerCamera == null || menuCanvas == null)
        {
            Debug.LogError("Player camera or menu canvas is not assigned in StartScreenUI.", this);
            ShowStartScreen();
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.5f);
        bool hasTrackedHeadPose = HasTrackedHeadPose();
        CenterMenuInFrontOfPlayer(hasTrackedHeadPose);
        ShowStartScreen();

        if (hasTrackedHeadPose)
            yield break;

        float elapsed = 0f;
        while (elapsed < trackingInitializationTimeout)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;

            if (HasTrackedHeadPose())
            {
                CenterMenuInFrontOfPlayer(true);
                yield break;
            }
        }
    }

    private void OnEnable()
    {
        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateRoomClicked);

        if (joinRoomButton != null)
            joinRoomButton.onClick.AddListener(OnJoinRoomClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

        if (createRoomBackButton != null)
            createRoomBackButton.onClick.AddListener(OnBackClicked);

        if (joinRoomBackButton != null)
            joinRoomBackButton.onClick.AddListener(OnBackClicked);

        if (joinRoomConfirmButton != null)
            joinRoomConfirmButton.onClick.AddListener(OnJoinRoomConfirmed);
    }

    private void OnDisable()
    {
        if (createRoomButton != null)
            createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);

        if (joinRoomButton != null)
            joinRoomButton.onClick.RemoveListener(OnJoinRoomClicked);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(OnQuitClicked);

        if (createRoomBackButton != null)
            createRoomBackButton.onClick.RemoveListener(OnBackClicked);

        if (joinRoomBackButton != null)
            joinRoomBackButton.onClick.RemoveListener(OnBackClicked);

        if (joinRoomConfirmButton != null)
            joinRoomConfirmButton.onClick.RemoveListener(OnJoinRoomConfirmed);
    }

    private void SetAllPanelsInactive()
    {
        if (startScreenPanel != null)
            startScreenPanel.SetActive(false);

        if (createRoomPanel != null)
            createRoomPanel.SetActive(false);

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(false);

        if (roomLobbyPanel != null)
            roomLobbyPanel.SetActive(false);
    }

    private void ShowStartScreen()
    {
        SetAllPanelsInactive();

        if (startScreenPanel != null)
            startScreenPanel.SetActive(true);
    }

    private void ShowCreateRoomPanel()
    {
        SetAllPanelsInactive();

        if (createRoomPanel != null)
            createRoomPanel.SetActive(true);
    }

    private void ShowJoinRoomPanel()
    {
        SetAllPanelsInactive();

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(true);
    }

    private void ShowLobby()
    {
        SetAllPanelsInactive();

        if (roomLobbyPanel != null)
        {
            roomLobbyPanel.SetActive(true);
            Debug.Log("Multiplayer lobby opened.");
        }
        else
        {
            Debug.LogError(
                "RoomLobbyPanel is not assigned in StartScreenUI."
            );
        }
    }

    private bool HasTrackedHeadPose()
    {
        return UnityEngine.XR.XRSettings.isDeviceActive &&
            playerCamera != null &&
            playerCamera.position.y - playerCamera.root.position.y > 0.5f;
    }

    private void CenterMenuInFrontOfPlayer(bool useTrackedHeight)
    {
        if (playerCamera == null || menuCanvas == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(
            playerCamera.forward,
            Vector3.up
        );

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 targetPosition =
            playerCamera.position + forward * menuDistance;

        targetPosition.y = useTrackedHeight
            ? playerCamera.position.y + menuHeightOffset
            : playerCamera.root.position.y + fallbackCameraHeight + menuHeightOffset;

        menuCanvas.position = targetPosition;

        menuCanvas.rotation = Quaternion.LookRotation(
            forward,
            Vector3.up
        );
    }

    private async void OnCreateRoomClicked()
    {
        if (isConnecting)
            return;

        if (roomCodeManager == null ||
            multiplayerRoomManager == null)
        {
            Debug.LogError(
                "RoomCodeManager or MultiplayerRoomManager is missing."
            );
            return;
        }

        // If already connected, reopen the existing lobby.
        if (multiplayerRoomManager.IsConnected)
        {
            currentRoomCode =
                multiplayerRoomManager.CurrentRoomCode;

            ShowLobby();
            return;
        }

        isConnecting = true;

        if (createRoomButton != null)
            createRoomButton.interactable = false;

        try
        {
            // Generate a candidate four-letter room code.
            string code =
                roomCodeManager.CreateUniqueRoomCode();

            bool success =
                await multiplayerRoomManager.CreateRoom(code);

            if (success)
            {
                currentRoomCode = code;

                DisplayRoomCode(code);

                Debug.Log("Room created: " + code);

                // Open the multiplayer lobby.
                ShowLobby();
            }
            else
            {
                Debug.LogError(
                    "Unable to create Photon room. " +
                    "Check the Console for details."
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Create Room error: " + exception.Message
            );
        }
        finally
        {
            isConnecting = false;

            if (createRoomButton != null)
                createRoomButton.interactable = true;
        }
    }

    private void DisplayRoomCode(string code)
    {
        if (roomCodeText != null)
            roomCodeText.text = "ROOM CODE: " + code;
    }

    private void OnJoinRoomClicked()
    {
        if (isConnecting)
            return;

        if (multiplayerRoomManager != null &&
            multiplayerRoomManager.IsConnected)
        {
            ShowLobby();
            return;
        }

        ShowJoinRoomPanel();

        if (roomCodeInputField != null)
            roomCodeInputField.text = "";
    }

    private async void OnJoinRoomConfirmed()
    {
        if (isConnecting)
            return;

        if (multiplayerRoomManager == null ||
            roomCodeInputField == null)
        {
            Debug.LogError(
                "MultiplayerRoomManager or RoomCodeInputField is missing."
            );
            return;
        }

        string code = roomCodeInputField.text
            .Trim()
            .ToUpperInvariant();

        // Validate the four-letter code.
        if (code.Length != 4)
        {
            Debug.LogWarning(
                "Please enter exactly four letters."
            );
            return;
        }

        foreach (char letter in code)
        {
            if (letter < 'A' || letter > 'Z')
            {
                Debug.LogWarning(
                    "Room code must contain only A-Z."
                );
                return;
            }
        }

        isConnecting = true;

        if (joinRoomConfirmButton != null)
            joinRoomConfirmButton.interactable = false;

        try
        {
            bool success =
                await multiplayerRoomManager.JoinRoom(code);

            if (success)
            {
                currentRoomCode = code;

                Debug.Log(
                    "Successfully joined room: " + code
                );

                // Show the lobby after joining.
                ShowLobby();
            }
            else
            {
                Debug.LogWarning(
                    "Could not join room. " +
                    "Check the room code or availability."
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Join Room error: " + exception.Message
            );
        }
        finally
        {
            isConnecting = false;

            if (joinRoomConfirmButton != null)
                joinRoomConfirmButton.interactable = true;
        }
    }

    private void OnBackClicked()
    {
        if (isConnecting)
            return;

        // Back changes panels but does not disconnect.
        ShowStartScreen();
    }

    // Called by RoomLobbyUI after successfully leaving.
    public void ReturnToStartScreen()
    {
        currentRoomCode = null;
        ShowStartScreen();
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
