
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

    [Header("Connection Status")]
    [SerializeField] private TMP_Text connectionStatusText;
    [SerializeField] private TMP_Text joinConnectionStatusText;

    private string currentRoomCode;
    private bool isConnecting = false;

    // ---------------- INITIALIZATION ----------------

    private IEnumerator Start()
    {
        SetAllPanelsInactive();
        ClearStatusMessages();

        if (playerCamera == null || menuCanvas == null)
        {
            Debug.LogError(
                "Player camera or menu canvas is not assigned in StartScreenUI.",
                this
            );

            ShowStartScreen();
            yield break;
        }

        Canvas canvas = menuCanvas.GetComponent<Canvas>();

        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
        {
            if (menuCanvas.GetComponent<GraphicRaycaster>() == null)
            {
                menuCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            Camera eventCamera = playerCamera.GetComponent<Camera>();

            if (eventCamera == null)
                eventCamera = Camera.main;

            if (eventCamera == null || !eventCamera.isActiveAndEnabled)
            {
                Debug.LogError(
                    "The world-space start menu needs an enabled event camera. " +
                    "Assign an enabled camera to Player Camera or tag it MainCamera.",
                    this
                );

                ShowStartScreen();
                yield break;
            }

            canvas.worldCamera = eventCamera;
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

    // ---------------- BUTTON LISTENERS ----------------

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

    // ---------------- PANELS ----------------

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
        ClearStatusMessages();

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
        SetJoinRoomStatus("");

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(true);
    }

    private void ShowLobby()
    {
        SetAllPanelsInactive();
        ClearStatusMessages();

        if (roomLobbyPanel != null)
        {
            roomLobbyPanel.SetActive(true);
            Debug.Log("[FourSquare] Multiplayer lobby opened.");
        }
        else
        {
            Debug.LogError(
                "RoomLobbyPanel is not assigned in StartScreenUI.",
                this
            );
        }
    }

    // ---------------- STATUS MESSAGES ----------------

    private void ClearStatusMessages()
    {
        SetCreateRoomStatus("");
        SetJoinRoomStatus("");
    }

    private void SetCreateRoomStatus(string message)
    {
        if (connectionStatusText != null)
            connectionStatusText.text = message;
    }

    private void SetJoinRoomStatus(string message)
    {
        if (joinConnectionStatusText != null)
            joinConnectionStatusText.text = message;
    }

    // ---------------- VR MENU PLACEMENT ----------------

    private bool HasTrackedHeadPose()
    {
        return UnityEngine.XR.XRSettings.isDeviceActive &&
            playerCamera != null &&
            playerCamera.position.y -
            playerCamera.root.position.y > 0.5f;
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
            : playerCamera.root.position.y +
              fallbackCameraHeight + menuHeightOffset;

        menuCanvas.position = targetPosition;

        menuCanvas.rotation = Quaternion.LookRotation(
            forward,
            Vector3.up
        );
    }

    // ---------------- CREATE ROOM ----------------

    private void OnCreateRoomClicked()
    {
        if (isConnecting)
            return;

        if (roomCodeManager == null ||
            multiplayerRoomManager == null)
        {
            SetCreateRoomStatus("Could not create room.");

            Debug.LogError(
                "RoomCodeManager or MultiplayerRoomManager is missing.",
                this
            );
            return;
        }

        if (multiplayerRoomManager.IsConnected)
        {
            currentRoomCode =
                multiplayerRoomManager.CurrentRoomCode;

            DisplayRoomCode(currentRoomCode);
            ShowLobby();
            return;
        }

        isConnecting = true;

        if (createRoomButton != null)
            createRoomButton.interactable = false;

        SetCreateRoomStatus("CREATING ROOM...");

        StartCoroutine(CreateRoomAfterFrame());
    }

    private IEnumerator CreateRoomAfterFrame()
    {
        // Give Unity one frame to render the loading message.
        yield return null;

        CreateRoomAsync();
    }

    private async void CreateRoomAsync()
    {
        float startTime = Time.realtimeSinceStartup;

        try
        {
            string code =
                roomCodeManager.CreateUniqueRoomCode();

            Debug.Log(
                "[FourSquare] Creating room: " + code
            );

            bool success =
                await multiplayerRoomManager.CreateRoom(code);

            float elapsed =
                Time.realtimeSinceStartup - startTime;

            Debug.Log(
                $"[FourSquare] Room creation took " +
                $"{elapsed:F2} seconds. Success: {success}"
            );

            if (success)
            {
                currentRoomCode = code;
                DisplayRoomCode(code);

                Debug.Log(
                    "[FourSquare] Room created: " + code
                );

                ShowLobby();
            }
            else
            {
                SetCreateRoomStatus("Could not create room.");

                Debug.LogError(
                    "[FourSquare] Unable to create Photon room.",
                    this
                );
            }
        }
        catch (Exception exception)
        {
            SetCreateRoomStatus("Could not create room.");

            Debug.LogError(
                "[FourSquare] Create Room error: " + exception,
                this
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

    // ---------------- JOIN ROOM ----------------

    private void OnJoinRoomClicked()
    {
        if (isConnecting)
            return;

        if (multiplayerRoomManager != null &&
            multiplayerRoomManager.IsConnected)
        {
            currentRoomCode =
                multiplayerRoomManager.CurrentRoomCode;

            DisplayRoomCode(currentRoomCode);
            ShowLobby();
            return;
        }

        ShowJoinRoomPanel();

        if (roomCodeInputField != null)
            roomCodeInputField.text = "";
    }

    private void OnJoinRoomConfirmed()
    {
        if (isConnecting)
            return;

        if (multiplayerRoomManager == null ||
            roomCodeInputField == null)
        {
            SetJoinRoomStatus("Unable to join room.");

            Debug.LogError(
                "MultiplayerRoomManager or RoomCodeInputField is missing.",
                this
            );
            return;
        }

        string code = roomCodeInputField.text
            .Trim()
            .ToUpperInvariant();

        // Empty code
        if (code.Length == 0)
        {
            SetJoinRoomStatus("Enter a 4-letter code.");
            return;
        }

        // Incorrect length
        if (code.Length != 4)
        {
            SetJoinRoomStatus("Enter exactly 4 letters.");
            return;
        }

        // Invalid characters
        foreach (char letter in code)
        {
            if (letter < 'A' || letter > 'Z')
            {
                SetJoinRoomStatus("Use letters A-Z only.");
                return;
            }
        }

        isConnecting = true;

        if (joinRoomConfirmButton != null)
            joinRoomConfirmButton.interactable = false;

        SetJoinRoomStatus("Joining room...");

        StartCoroutine(JoinRoomAfterFrame(code));
    }

    private IEnumerator JoinRoomAfterFrame(string code)
    {
        // Allow the loading message to render first.
        yield return null;

        JoinRoomAsync(code);
    }

    private async void JoinRoomAsync(string code)
    {
        float startTime = Time.realtimeSinceStartup;

        try
        {
            Debug.Log(
                "[FourSquare] Joining room: " + code
            );

            bool success =
                await multiplayerRoomManager.JoinRoom(code);

            float elapsed =
                Time.realtimeSinceStartup - startTime;

            Debug.Log(
                $"[FourSquare] Join Room took " +
                $"{elapsed:F2} seconds. Success: {success}"
            );

            if (success)
            {
                currentRoomCode = code;
                DisplayRoomCode(code);

                Debug.Log(
                    "[FourSquare] Successfully joined room: " + code
                );

                ShowLobby();
            }
            else
            {
                SetJoinRoomStatus("Could not join room.");

                Debug.LogWarning(
                    "[FourSquare] Could not join room. " +
                    "Check the room code or availability.",
                    this
                );
            }
        }
        catch (Exception exception)
        {
            SetJoinRoomStatus("Could not join room.");

            Debug.LogError(
                "[FourSquare] Join Room error: " + exception,
                this
            );
        }
        finally
        {
            isConnecting = false;

            if (joinRoomConfirmButton != null)
                joinRoomConfirmButton.interactable = true;
        }
    }

    // ---------------- BACK / RETURN ----------------

    private void OnBackClicked()
    {
        if (isConnecting)
            return;

        ShowStartScreen();
    }

    // Called by RoomLobbyUI after successfully leaving.
    public void ReturnToStartScreen()
    {
        currentRoomCode = null;
        ShowStartScreen();
    }

    // ---------------- QUIT ----------------

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
