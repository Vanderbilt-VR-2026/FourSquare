
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
    [SerializeField] private Button joinRoomBackButton;
    [SerializeField] private Button joinRoomConfirmButton;

    [Header("Panels")]
    [SerializeField] private GameObject startScreenPanel;
    [SerializeField] private GameObject joinRoomPanel;

    [Header("VR Menu Placement")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform menuCanvas;
    [SerializeField] private float menuDistance = 2f;
    [SerializeField] private float trackingInitializationTimeout = 5f;

    [Header("Room Creation")]
    [SerializeField] private RoomCodeManager roomCodeManager;

    [Header("Join Room")]
    [SerializeField] private TMP_InputField roomCodeInputField;

    [Header("Connection Feedback")]
    [SerializeField] private TMP_Text connectionStatusText;

    [Header("Multiplayer")]
    [SerializeField] private MultiplayerRoomManager multiplayerRoomManager;

    private bool isConnecting = false;
    private TMP_Text createRoomButtonText;
    private TMP_Text joinRoomConfirmButtonText;
    private string createRoomButtonLabel;
    private string joinRoomConfirmButtonLabel;
    private float loadingAnimationTimer;
    private int loadingDotCount;
    private Camera startupOverlayCamera;
    private CameraClearFlags startupCameraClearFlags;
    private Color startupCameraBackgroundColor;
    private int startupCameraCullingMask;
    private bool isStartupOverlayVisible;
    private static StartScreenUI startupInstance;

    private IEnumerator Start()
    {
        if (startupInstance != null)
            yield break;

        startupInstance = this;
        CacheButtonLabels();
        SetConnectionStatus(string.Empty);
        SetAllPanelsInactive();

        if (playerCamera == null || menuCanvas == null)
        {
            Debug.LogError("Player camera or menu canvas is not assigned in StartScreenUI.", this);
            ShowStartScreen();
            yield break;
        }

        ShowStartupOverlay();

        Canvas canvas = menuCanvas.GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
        {
            if (menuCanvas.GetComponent<GraphicRaycaster>() == null)
                menuCanvas.gameObject.AddComponent<GraphicRaycaster>();

            Camera eventCamera = playerCamera.GetComponent<Camera>();
            if (eventCamera == null)
                eventCamera = Camera.main;

            if (eventCamera == null || !eventCamera.isActiveAndEnabled)
            {
                Debug.LogError(
                    "The world-space start menu needs an enabled event camera. Assign an enabled camera " +
                    "to Player Camera or tag it MainCamera.",
                    this);
                HideStartupOverlay();
                ShowStartScreen();
                yield break;
            }

            canvas.worldCamera = eventCamera;
        }

        yield return new WaitForSecondsRealtime(0.5f);
        bool hasTrackedHeadPose = HasTrackedHeadPose();
        PlacePlayerInFrontOfMenu();
        ShowStartScreen();

        if (hasTrackedHeadPose)
        {
            HideStartupOverlay();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < trackingInitializationTimeout)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;

            if (HasTrackedHeadPose())
            {
                PlacePlayerInFrontOfMenu();
                HideStartupOverlay();
                yield break;
            }
        }

        HideStartupOverlay();
    }

    private void OnDestroy()
    {
        if (startupInstance == this)
            startupInstance = null;
    }

    private void ShowStartupOverlay()
    {
        Camera camera = playerCamera.GetComponent<Camera>();
        if (camera == null)
            camera = Camera.main;

        if (camera == null)
            return;

        startupOverlayCamera = camera;
        startupCameraClearFlags = camera.clearFlags;
        startupCameraBackgroundColor = camera.backgroundColor;
        startupCameraCullingMask = camera.cullingMask;
        isStartupOverlayVisible = true;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 0;
    }

    private void HideStartupOverlay()
    {
        if (!isStartupOverlayVisible || startupOverlayCamera == null)
            return;

        startupOverlayCamera.clearFlags = startupCameraClearFlags;
        startupOverlayCamera.backgroundColor = startupCameraBackgroundColor;
        startupOverlayCamera.cullingMask = startupCameraCullingMask;
        startupOverlayCamera = null;
        isStartupOverlayVisible = false;
    }

    private void OnEnable()
    {
        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateRoomClicked);

        if (joinRoomButton != null)
            joinRoomButton.onClick.AddListener(OnJoinRoomClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

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

        if (joinRoomBackButton != null)
            joinRoomBackButton.onClick.RemoveListener(OnBackClicked);

        if (joinRoomConfirmButton != null)
            joinRoomConfirmButton.onClick.RemoveListener(OnJoinRoomConfirmed);
    }

    private void Update()
    {
        if (!isConnecting)
            return;

        loadingAnimationTimer += Time.unscaledDeltaTime;
        if (loadingAnimationTimer < 0.35f)
            return;

        loadingAnimationTimer = 0f;
        loadingDotCount = (loadingDotCount + 1) % 4;
        string dots = new string('.', loadingDotCount);

        if (createRoomButton != null && !createRoomButton.interactable)
            SetButtonLabel(createRoomButtonText, createRoomButtonLabel + dots);

        if (joinRoomConfirmButton != null && !joinRoomConfirmButton.interactable)
            SetButtonLabel(joinRoomConfirmButtonText, joinRoomConfirmButtonLabel + dots);
    }

    private void CacheButtonLabels()
    {
        if (createRoomButton != null)
        {
            createRoomButtonText = createRoomButton.GetComponentInChildren<TMP_Text>();
            createRoomButtonLabel = createRoomButtonText != null
                ? createRoomButtonText.text
                : string.Empty;
        }

        if (joinRoomConfirmButton != null)
        {
            joinRoomConfirmButtonText =
                joinRoomConfirmButton.GetComponentInChildren<TMP_Text>();
            joinRoomConfirmButtonLabel = joinRoomConfirmButtonText != null
                ? joinRoomConfirmButtonText.text
                : string.Empty;
        }
    }

    private void SetButtonLabel(TMP_Text buttonText, string label)
    {
        if (buttonText != null)
            buttonText.text = label;
    }

    private void SetConnectionStatus(string message)
    {
        if (connectionStatusText != null)
        {
            connectionStatusText.text = message;
            connectionStatusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }
    }

    private void BeginConnectionFeedback(string message)
    {
        isConnecting = true;
        loadingAnimationTimer = 0f;
        loadingDotCount = 0;
        SetConnectionStatus(message);
    }

    private void EndConnectionFeedback()
    {
        isConnecting = false;
        SetButtonLabel(createRoomButtonText, createRoomButtonLabel);
        SetButtonLabel(joinRoomConfirmButtonText, joinRoomConfirmButtonLabel);
        SetConnectionStatus(string.Empty);
    }

    private void SetAllPanelsInactive()
    {
        if (startScreenPanel != null)
            startScreenPanel.SetActive(false);

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(false);
    }

    private void ShowStartScreen()
    {
        SetAllPanelsInactive();

        if (startScreenPanel != null)
            startScreenPanel.SetActive(true);
    }

    private void ShowJoinRoomPanel()
    {
        SetAllPanelsInactive();

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(true);
    }

    private bool HasTrackedHeadPose()
    {
        return UnityEngine.XR.XRSettings.isDeviceActive &&
            playerCamera != null &&
            playerCamera.position.y - playerCamera.root.position.y > 0.5f;
    }

    private void PlacePlayerInFrontOfMenu()
    {
        if (playerCamera == null || menuCanvas == null)
            return;

        Transform playerRoot = playerCamera.root;
        if (playerRoot == null)
            return;

        Vector3 menuForward = Vector3.ProjectOnPlane(
            menuCanvas.forward,
            Vector3.up
        );

        if (menuForward.sqrMagnitude < 0.001f)
            return;

        menuForward.Normalize();

        Vector3 cameraForward = Vector3.ProjectOnPlane(
            playerCamera.forward,
            Vector3.up
        );

        if (cameraForward.sqrMagnitude > 0.001f)
        {
            cameraForward.Normalize();
            float yawCorrection = Vector3.SignedAngle(
                cameraForward,
                menuForward,
                Vector3.up
            );
            playerRoot.rotation =
                Quaternion.AngleAxis(yawCorrection, Vector3.up) * playerRoot.rotation;
        }

        Vector3 menuCenter = menuCanvas.position;
        RectTransform menuRect = menuCanvas as RectTransform;
        if (menuRect != null)
            menuCenter = menuRect.TransformPoint(menuRect.rect.center);

        Vector3 targetCameraPosition = menuCenter - menuForward * menuDistance;
        targetCameraPosition.y = playerCamera.position.y;
        playerRoot.position += targetCameraPosition - playerCamera.position;
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

        if (multiplayerRoomManager.IsConnected)
            return;

        BeginConnectionFeedback("Creating room...");

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
                Debug.Log("Room created: " + code);
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
            EndConnectionFeedback();

            if (createRoomButton != null)
                createRoomButton.interactable = true;
        }
    }

    private void OnJoinRoomClicked()
    {
        if (isConnecting)
            return;

        if (multiplayerRoomManager != null &&
            multiplayerRoomManager.IsConnected)
            return;

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
            SetConnectionStatus("Enter a 4-letter room code.");
            Debug.LogWarning(
                "Please enter exactly four letters."
            );
            return;
        }

        foreach (char letter in code)
        {
            if (letter < 'A' || letter > 'Z')
            {
                SetConnectionStatus("Room code must contain only A-Z.");
                Debug.LogWarning(
                    "Room code must contain only A-Z."
                );
                return;
            }
        }

        BeginConnectionFeedback("Joining room...");

        if (joinRoomConfirmButton != null)
            joinRoomConfirmButton.interactable = false;

        try
        {
            bool success =
                await multiplayerRoomManager.JoinRoom(code);

            if (success)
            {
                Debug.Log(
                    "Successfully joined room: " + code
                );
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
            EndConnectionFeedback();

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

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
