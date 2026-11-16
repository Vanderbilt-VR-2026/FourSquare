using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class EndScreenUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button quitButton;

    [Header("Panels")]
    [SerializeField] private GameObject endScreenPanel;

    [Header("World-Space Canvas")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform menuCanvas;

    private void Start()
    {
        ConfigureWorldSpaceCanvas();
        ShowEndScreen();
    }

    private void OnEnable()
    {
        if (playAgainButton != null)
            playAgainButton.onClick.AddListener(OnPlayAgainClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
    }

    private void OnDisable()
    {
        if (playAgainButton != null)
            playAgainButton.onClick.RemoveListener(OnPlayAgainClicked);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(OnQuitClicked);
    }

    private void ConfigureWorldSpaceCanvas()
    {
        if (menuCanvas == null)
            return;

        Canvas canvas = menuCanvas.GetComponent<Canvas>();
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            return;

        if (menuCanvas.GetComponent<GraphicRaycaster>() == null)
            menuCanvas.gameObject.AddComponent<GraphicRaycaster>();

        if (menuCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            menuCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        Camera eventCamera = playerCamera != null
            ? playerCamera.GetComponent<Camera>()
            : null;

        if (eventCamera == null)
            eventCamera = Camera.main;

        if (eventCamera == null || !eventCamera.isActiveAndEnabled)
        {
            Debug.LogError(
                "The world-space end screen needs an enabled event camera. " +
                "Assign a camera to Player Camera or tag it MainCamera.",
                this);
            return;
        }

        canvas.worldCamera = eventCamera;
    }


    private void ShowEndScreen()
    {
        if (endScreenPanel != null)
            endScreenPanel.SetActive(true);
    }

    private async void OnPlayAgainClicked()
    {
        if (playAgainButton != null)
            playAgainButton.interactable = false;

        try
        {
            MultiplayerRoomManager roomManager = MultiplayerRoomManager.Instance;
            if (roomManager != null)
                await roomManager.LeaveRoom();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }

        SceneManager.LoadScene("StartScene");
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