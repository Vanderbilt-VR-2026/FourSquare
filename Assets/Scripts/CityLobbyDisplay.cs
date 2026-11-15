using System.Linq;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

[DisallowMultipleComponent]
public sealed class CityLobbyDisplay : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;

    private TMP_Text statusText;
    private TMP_Text toggleHintText;
    private Transform viewTransform;
    private Canvas displayCanvas;
    private float nextRefreshTime;
    private bool displayVisible = true;
    private bool toggleButtonWasPressed;

    public void Initialize(Transform head, Camera playerCamera)
    {
        if (head == null)
        {
            Debug.LogError("CityLobbyDisplay requires the local XR head transform.", this);
            enabled = false;
            return;
        }

        viewTransform = playerCamera != null ? playerCamera.transform : head;
        transform.SetParent(head, false);
        transform.localPosition = new Vector3(0f, -0.28f, 1.4f);
        transform.localScale = Vector3.one * 0.0012f;

        displayCanvas = gameObject.AddComponent<Canvas>();
        displayCanvas.renderMode = RenderMode.WorldSpace;
        displayCanvas.worldCamera = playerCamera;
        displayCanvas.sortingOrder = 110;

        RectTransform canvasRect = (RectTransform)transform;
        canvasRect.sizeDelta = new Vector2(700f, 210f);

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(transform, false);
        RectTransform backgroundRect = (RectTransform)backgroundObject.transform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        Image background = backgroundObject.GetComponent<Image>();
        background.color = new Color(0.02f, 0.04f, 0.07f, 0.88f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject("Lobby Status", typeof(RectTransform));
        textObject.transform.SetParent(backgroundObject.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = new Vector2(0.04f, 0.27f);
        textRect.anchorMax = new Vector2(0.96f, 0.92f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        statusText = textObject.AddComponent<TextMeshProUGUI>();
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.fontSize = 42f;
        statusText.color = Color.white;
        statusText.raycastTarget = false;
        statusText.text = "CONNECTING TO LOBBY...";

        GameObject hintObject = new GameObject("Toggle Hint", typeof(RectTransform));
        hintObject.transform.SetParent(backgroundObject.transform, false);
        RectTransform hintRect = (RectTransform)hintObject.transform;
        hintRect.anchorMin = new Vector2(0.04f, 0.02f);
        hintRect.anchorMax = new Vector2(0.96f, 0.23f);
        hintRect.offsetMin = Vector2.zero;
        hintRect.offsetMax = Vector2.zero;

        toggleHintText = hintObject.AddComponent<TextMeshProUGUI>();
        toggleHintText.alignment = TextAlignmentOptions.Center;
        toggleHintText.fontSize = 25f;
        toggleHintText.color = new Color(0.8f, 0.86f, 0.92f, 1f);
        toggleHintText.raycastTarget = false;
        toggleHintText.text = "Press RIGHT CONTROLLER B / Y to hide or show this panel";

        UpdateStatus();
    }

    private void Update()
    {
        bool pressed = InputDevices.GetDeviceAtXRNode(XRNode.RightHand)
            .TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondaryButtonPressed)
            && secondaryButtonPressed;
        if (pressed && !toggleButtonWasPressed)
        {
            displayVisible = !displayVisible;
            displayCanvas.enabled = displayVisible;
        }
        toggleButtonWasPressed = pressed;

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        UpdateStatus();
    }

    private void LateUpdate()
    {
        if (viewTransform == null)
            return;

        Vector3 awayFromCamera = transform.position - viewTransform.position;
        if (awayFromCamera.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(awayFromCamera, viewTransform.up);
    }

    private void UpdateStatus()
    {
        if (statusText == null)
            return;

        MultiplayerRoomManager roomManager = MultiplayerRoomManager.Instance;
        NetworkRunner runner = roomManager != null ? roomManager.Runner : null;
        if (runner == null && FourSquareFusionSession.Instance != null)
            runner = FourSquareFusionSession.Instance.Runner;

        string roomCode = roomManager != null ? roomManager.CurrentRoomCode : null;
        if (string.IsNullOrEmpty(roomCode) && runner != null && runner.IsRunning)
            roomCode = runner.SessionInfo.Name;

        if (runner == null || !runner.IsRunning)
        {
            statusText.text = string.IsNullOrEmpty(roomCode)
                ? "LOBBY: NOT CONNECTED"
                : $"LOBBY CODE: {roomCode}\nSTATUS: RECONNECTING";
            return;
        }

        int playerCount = roomManager != null
            ? roomManager.PlayerCount
            : runner.ActivePlayers.Count();
        int maximumPlayers = roomManager != null ? roomManager.MaximumPlayers : 4;
        string gameStatus = FoursquareGameManager.Instance != null &&
                            FoursquareGameManager.Instance.CurrentState != FoursquareGameState.WaitingToStart
            ? "GAME IN PROGRESS"
            : "IN CITY";

        statusText.text =
            $"LOBBY CODE: {roomCode ?? "----"}   PLAYERS: {playerCount}/{maximumPlayers}\n" +
            $"STATUS: {gameStatus}";
    }
}
