using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class EndScreenUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button quitButton;

    [Header("Panels")]
    [SerializeField] private GameObject endScreenPanel;

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
        if (endScreenPanel != null)
            endScreenPanel.SetActive(false);

        // Give Quest/XR tracking time to establish
        // the correct headset position and direction.
        yield return new WaitForSeconds(0.5f);

        // Position the canvas once in front of the player.
        CenterMenuInFrontOfPlayer();

        // Show only the main End Screen.
        ShowEndScreen();
    }

    private void OnEnable()
    {
        // if (playAgainButton != null)
        //     playAgainButton.onClick.AddListener(OnPlayAgainClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

    }

    private void OnDisable()
    {
        // if (playAgainButton != null)
        //     playAgainButton.onClick.RemoveListener(OnPlayAgainClicked);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(OnQuitClicked);
    }


    private void ShowEndScreen()
    {
        // Only change which panel is visible.
        // Do NOT reposition the canvas here.

        if (endScreenPanel != null)
            endScreenPanel.SetActive(true);

     
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

    // private void OnPlayAgainClicked()
    // {
    // }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}