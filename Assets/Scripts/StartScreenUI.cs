using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StartScreenUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button createRoomBackButton;
    [SerializeField] private Button joinRoomBackButton;

    [Header("Panels")]
    [SerializeField] private GameObject startScreenPanel;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject joinRoomPanel;

    [Header("VR Menu Placement")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform menuCanvas;

    // Distance of the menu from the player's headset.
    [SerializeField] private float menuDistance = 2f;

    // Moves the menu slightly above eye level.
    [SerializeField] private float menuHeightOffset = 0.15f;

    private IEnumerator Start()
    {
        // Hide all UI panels while XR tracking initializes.
        // This prevents the menu from briefly appearing
        // backwards before it is positioned correctly.
        if (startScreenPanel != null)
            startScreenPanel.SetActive(false);

        if (createRoomPanel != null)
            createRoomPanel.SetActive(false);

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(false);

        // Give Quest/XR tracking time to establish
        // the correct headset position and direction.
        yield return new WaitForSeconds(0.5f);

        // Position the canvas once in front of the player.
        CenterMenuInFrontOfPlayer();

        // Show only the main Start Screen.
        ShowStartScreen();
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
    }

    private void ShowStartScreen()
    {
        // Only change which panel is visible.
        // Do NOT reposition the canvas here.

        if (startScreenPanel != null)
            startScreenPanel.SetActive(true);

        if (createRoomPanel != null)
            createRoomPanel.SetActive(false);

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(false);
    }

    private void CenterMenuInFrontOfPlayer()
    {
        if (playerCamera == null || menuCanvas == null)
            return;

        // Get the horizontal direction the headset is facing.
        // Removing the vertical component keeps the menu upright.
        Vector3 forward = Vector3.ProjectOnPlane(
            playerCamera.forward,
            Vector3.up
        );

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();

        // Position the menu directly in front of the headset.
        Vector3 targetPosition =
            playerCamera.position + (forward * menuDistance);

        // Position the menu slightly above eye level.
        targetPosition.y =
            playerCamera.position.y + menuHeightOffset;

        menuCanvas.position = targetPosition;

        // Orient the canvas correctly so the text is readable.
        menuCanvas.rotation = Quaternion.LookRotation(
            forward,
            Vector3.up
        );
    }

    private void OnCreateRoomClicked()
    {
        if (startScreenPanel != null)
            startScreenPanel.SetActive(false);

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(false);

        if (createRoomPanel != null)
            createRoomPanel.SetActive(true);
    }

    private void OnJoinRoomClicked()
    {
        if (startScreenPanel != null)
            startScreenPanel.SetActive(false);

        if (createRoomPanel != null)
            createRoomPanel.SetActive(false);

        if (joinRoomPanel != null)
            joinRoomPanel.SetActive(true);
    }

    private void OnBackClicked()
    {
        // Return to the Start Screen without
        // repositioning the canvas.
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