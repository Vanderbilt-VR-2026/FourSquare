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

    private void OnCreateRoomClicked()
    {
        startScreenPanel.SetActive(false);
        joinRoomPanel.SetActive(false);
        createRoomPanel.SetActive(true);
    }

    private void OnJoinRoomClicked()
    {
        startScreenPanel.SetActive(false);
        createRoomPanel.SetActive(false);
        joinRoomPanel.SetActive(true);
    }

    private void OnBackClicked()
    {
        createRoomPanel.SetActive(false);
        joinRoomPanel.SetActive(false);
        startScreenPanel.SetActive(true);
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