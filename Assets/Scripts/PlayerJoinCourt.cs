using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR;
using System.Collections.Generic;
using Fusion;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerJoinCourt : MonoBehaviour
{
    [Header("Court Setup")]
    [Tooltip("Index 0 is Square 1 and index 3 is Square 4.")]
    [SerializeField] private Transform[] squareSpawnPoints;
    [Tooltip("BoxCollider covering the playable court area.")]
    [SerializeField] private BoxCollider courtBounds;
    [SerializeField] private Transform exitPoint;

    [Header("Join Popup")]
    [SerializeField] private float popupDistance = 1.25f;
    [SerializeField, Min(1)] private int minimumPlayersToStart = 1;
    [SerializeField] private int maximumPlayers = 4;
    [SerializeField, Min(0f)] private float leaderboardHeightOffset = 2f;
    [SerializeField, Min(0.1f)] private float ballDropHeight = 1.5f;
    [SerializeField, Min(0f)] private float ballForwardOffset = 0.75f;
    [SerializeField, Min(0.1f)] private float exitHoldDuration = 2f;

    private const float PopupWidth = 700f;
    private const float PopupHeight = 360f;
    private const float UiRayDistance = 5f;
    private const float ExitPopupSize = 128f;
    private const float PlayerSpawnTolerance = 1f;
    private static readonly Dictionary<XROrigin, PlayerSession> joinedPlayers = new Dictionary<XROrigin, PlayerSession>();
    private static readonly List<PlayerJoinCourt> activeCourts = new List<PlayerJoinCourt>();
    private static int lastDisplayedPlayerCount = -1;
    private static bool lastDisplayedGameStarted;
    private static Sprite exitRingSprite;
    private JoinZoneAura joinZoneAura;
    private CourtLeaderboardBillboard leaderboardBillboard;

    private class PlayerSession
    {
        public XROrigin origin;
        public GameObject popup;
        public Text statusText;
        public Button startButton;
        public PlayerJoinCourt court;
        public Transform squareSpawnPoint;
        public Transform leftHand;
        public Transform rightHand;
        public LineRenderer leftUiRay;
        public LineRenderer rightUiRay;
        public Button leftHoveredButton;
        public Button rightHoveredButton;
        public Dictionary<Button, Color> buttonNormalColors;
        public bool leftTriggerWasPressed;
        public bool rightTriggerWasPressed;
        public GameObject exitHoldPopup;
        public Image exitHoldProgress;
        public float exitHoldTime;
    }

    public static bool CanStartGame
    {
        get
        {
            int minimumPlayers = activeCourts.Count > 0
                ? Mathf.Max(1, activeCourts[0].minimumPlayersToStart)
                : 1;
            return GetPlayerCount() >= minimumPlayers
                && GameStateManager.Instance != null
                && !GameStateManager.Instance.HasGameStarted;
        }
    }

    public static int ConnectedPlayerCount => GetPlayerCount();
    public static int CourtPlayerCount => joinedPlayers.Count;
    public static bool IsGameInProgress
    {
        get => GameStateManager.Instance != null && GameStateManager.Instance.HasGameStarted;
    }

    public static void RefreshJoinZoneAuras()
    {
        foreach (PlayerJoinCourt court in activeCourts)
        {
            if (court != null && court.joinZoneAura != null)
            {
                court.joinZoneAura.RefreshState();
            }
        }
    }

    public static void OnNetworkPlayerLeft(PlayerRef playerRef)
    {
        List<XROrigin> originsToRemove = new List<XROrigin>();
        foreach (KeyValuePair<XROrigin, PlayerSession> entry in joinedPlayers)
        {
            FourSquareFusionPlayer player = entry.Key != null
                ? entry.Key.GetComponent<FourSquareFusionPlayer>()
                : null;
            if (player != null && player.Object != null && player.Object.IsValid
                && player.Object.InputAuthority == playerRef)
            {
                originsToRemove.Add(entry.Key);
            }
        }

        foreach (XROrigin origin in originsToRemove)
        {
            RemoveCourtSession(origin);
        }

        UpdateAllPopupMessages();
    }

    public static void OnNetworkSessionShutdown()
    {
        List<XROrigin> networkOrigins = new List<XROrigin>();
        foreach (KeyValuePair<XROrigin, PlayerSession> entry in joinedPlayers)
        {
            if (entry.Key != null && entry.Key.GetComponent<FourSquareFusionPlayer>() != null)
            {
                networkOrigins.Add(entry.Key);
            }
        }

        foreach (XROrigin origin in networkOrigins)
        {
            RemoveCourtSession(origin);
        }

        UpdateAllPopupMessages();
    }

    private void Awake()
    {
        joinZoneAura = GetComponent<JoinZoneAura>();
        if (joinZoneAura == null) joinZoneAura = gameObject.AddComponent<JoinZoneAura>();
        joinZoneAura.Configure(GetComponent<Renderer>(), maximumPlayers);
        leaderboardBillboard = gameObject.AddComponent<CourtLeaderboardBillboard>();
        leaderboardBillboard.Initialize(courtBounds, squareSpawnPoints, leaderboardHeightOffset);
    }

    private void OnEnable()
    {
        if (!activeCourts.Contains(this)) activeCourts.Add(this);
    }

    private void OnDisable()
    {
        activeCourts.Remove(this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;

        Transform boardAnchor = CourtLeaderboardBillboard.GetBoardAnchor(transform, courtBounds);
        CourtLeaderboardBillboard.GetBoardPose(
            boardAnchor, courtBounds, squareSpawnPoints, leaderboardHeightOffset,
            out Vector3 position, out Quaternion rotation);
        if (Application.isPlaying && leaderboardBillboard != null
            && leaderboardBillboard.BoardTransform != null)
        {
            position = leaderboardBillboard.BoardTransform.position;
            rotation = leaderboardBillboard.BoardTransform.rotation;
        }

        Vector3 right = rotation * Vector3.right * (CourtLeaderboardBillboard.WorldWidth * 0.5f);
        Vector3 up = rotation * Vector3.up * (CourtLeaderboardBillboard.WorldHeight * 0.5f);
        Vector3[] corners =
        {
            position - right - up,
            position - right + up,
            position + right + up,
            position + right - up
        };

        Handles.DrawSolidRectangleWithOutline(
            corners,
            new Color(0.04f, 0.08f, 0.12f, 0.55f),
            new Color(0.45f, 0.9f, 1f, 1f));

        Vector2 screenMin = HandleUtility.WorldToGUIPoint(corners[0]);
        Vector2 screenMax = screenMin;
        foreach (Vector3 corner in corners)
        {
            Vector2 screenPoint = HandleUtility.WorldToGUIPoint(corner);
            screenMin = Vector2.Min(screenMin, screenPoint);
            screenMax = Vector2.Max(screenMax, screenPoint);
        }

        Rect boardRect = Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
        float labelFontSize = Mathf.Clamp(
            Mathf.Min(boardRect.height * 0.25f, boardRect.width / 22f),
            8f,
            24f);
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(1, Mathf.FloorToInt(labelFontSize)),
            clipping = TextClipping.Clip,
            wordWrap = false
        };
        style.normal.textColor = Color.white;
        Handles.BeginGUI();
        GUI.Label(boardRect, "WAITING FOR PLAYERS", style);
        Handles.EndGUI();
    }
#endif

    public static bool IsBallOutsideCourt(Vector3 worldPosition)
    {
        foreach (PlayerJoinCourt court in activeCourts)
        {
            if (court == null || court.courtBounds == null) continue;

            Transform boundsTransform = court.courtBounds.transform;
            Vector3 localPosition = boundsTransform.InverseTransformPoint(worldPosition);
            Vector3 halfSize = court.courtBounds.size * 0.5f;
            Vector3 center = court.courtBounds.center;
            bool outside = localPosition.x < center.x - halfSize.x || localPosition.x > center.x + halfSize.x
                || localPosition.z < center.z - halfSize.z || localPosition.z > center.z + halfSize.z;
            if (outside)
            {
                Debug.LogWarning(
                    $"[RoleTrace] Out-of-court bounds check: court='{court.name}', " +
                    $"ballWorld={worldPosition}, ballLocal={localPosition}, " +
                    $"boundsCenterLocal={center}, boundsSizeLocal={court.courtBounds.size}, " +
                    $"boundsWorld={court.courtBounds.bounds}.",
                    court);
            }

            return outside;
        }

        return false;
    }

    public static bool TryGetKingBallSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        foreach (PlayerJoinCourt court in activeCourts)
        {
            if (court == null) continue;

            FourSquareFusionPlayer[] networkPlayers =
                FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
            Transform kingTransform = null;
            foreach (FourSquareFusionPlayer networkPlayer in networkPlayers)
            {
                if (networkPlayer == null || networkPlayer.PlayerRole != SquareRole.King) continue;

                kingTransform = networkPlayer.HeadTransform != null
                    ? networkPlayer.HeadTransform
                    : networkPlayer.transform;
                break;
            }

            if (court.squareSpawnPoints == null) continue;

            int kingSpawnIndex = GetSpawnIndex(SquareRole.King);
            if (kingSpawnIndex >= court.squareSpawnPoints.Length
                || court.squareSpawnPoints[kingSpawnIndex] == null) continue;

            Transform kingSpawn = court.squareSpawnPoints[kingSpawnIndex];
            Vector3 forward = kingTransform != null ? kingTransform.forward : kingSpawn.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = kingSpawn.forward;
                forward.y = 0f;
            }
            forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;

            Vector3 kingPosition = kingTransform != null ? kingTransform.position : kingSpawn.position;
            float floorHeight = kingSpawn.position.y;
            position = kingPosition + forward * court.ballForwardOffset;
            position.y = floorHeight + court.ballDropHeight;
            if (court.courtBounds != null)
            {
                Bounds bounds = court.courtBounds.bounds;
                const float boundaryInset = 0.35f;
                position.x = Mathf.Clamp(position.x, bounds.min.x + boundaryInset, bounds.max.x - boundaryInset);
                position.z = Mathf.Clamp(position.z, bounds.min.z + boundaryInset, bounds.max.z - boundaryInset);
            }

            rotation = Quaternion.LookRotation(forward, Vector3.up);
            Debug.Log($"[FourSquare] King ball spawn pose: camera={(kingTransform != null ? kingTransform.position : kingSpawn.position)}, " +
                $"cameraForward={forward}, ball={position}.", court);
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    public static void MoveJoinedPlayerToRole(XROrigin xrOrigin, SquareRole role)
    {
        if (xrOrigin == null || role == SquareRole.Spectator) return;

        int spawnIndex = GetSpawnIndex(role);
        PlayerSession session = joinedPlayers.TryGetValue(xrOrigin, out PlayerSession joinedSession)
            ? joinedSession
            : null;
        PlayerJoinCourt court = session != null && session.court != null
            ? session.court
            : activeCourts.Find(activeCourt => activeCourt != null
                && activeCourt.squareSpawnPoints != null
                && spawnIndex >= 0
                && spawnIndex < activeCourt.squareSpawnPoints.Length
                && activeCourt.squareSpawnPoints[spawnIndex] != null);
        if (court == null)
        {
            Debug.LogError($"No court spawn point is available for the {role} role.", xrOrigin);
            return;
        }

        if (spawnIndex < 0 || spawnIndex >= court.squareSpawnPoints.Length
            || court.squareSpawnPoints[spawnIndex] == null)
        {
            Debug.LogError($"No court spawn point is configured for the {role} role.", court);
            return;
        }

        Transform spawnPoint = court.squareSpawnPoints[spawnIndex];
        Vector3 positionOffset = xrOrigin.transform.position - spawnPoint.position;
        if (positionOffset.sqrMagnitude > PlayerSpawnTolerance * PlayerSpawnTolerance)
        {
            TeleportPlayer(xrOrigin, spawnPoint, court.courtBounds);
            Debug.Log(
                $"[FourSquare] Teleported local player to {role} square '{spawnPoint.name}' at {spawnPoint.position}.",
                xrOrigin);
        }

        if (session != null) session.squareSpawnPoint = spawnPoint;
    }

    public static void TeleportJoinedPlayersToAssignedRoles()
    {
        PlayerSession[] sessions = new PlayerSession[joinedPlayers.Count];
        joinedPlayers.Values.CopyTo(sessions, 0);

        foreach (PlayerSession session in sessions)
        {
            if (session.origin == null) continue;

            FourSquareFusionPlayer networkPlayer =
                session.origin.GetComponent<FourSquareFusionPlayer>();
            SquareRole role = networkPlayer != null
                ? networkPlayer.PlayerRole
                : SquareRole.King;
            MoveJoinedPlayerToRole(session.origin, role);
        }
    }

    public static bool ArePlayersAtAssignedSquares(
        FourSquareFusionPlayer[] players, out int playersNotAtSquare)
    {
        playersNotAtSquare = 0;
        int rankedPlayers = 0;
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player == null || player.PlayerRole == SquareRole.Spectator) continue;

            rankedPlayers++;
            bool atAssignedSquare = false;
            foreach (PlayerJoinCourt court in activeCourts)
            {
                if (court == null) continue;

                int spawnIndex = GetSpawnIndex(player.PlayerRole);
                if (court.squareSpawnPoints == null
                    || spawnIndex < 0
                    || spawnIndex >= court.squareSpawnPoints.Length
                    || court.squareSpawnPoints[spawnIndex] == null)
                {
                    continue;
                }

                Vector3 target = court.squareSpawnPoints[spawnIndex].position;
                Vector3 playerPosition = player.NetworkRootPosition;
                Vector2 horizontalOffset = new Vector2(
                    playerPosition.x - target.x,
                    playerPosition.z - target.z);
                if (horizontalOffset.sqrMagnitude <= PlayerSpawnTolerance * PlayerSpawnTolerance
                    && Mathf.Abs(playerPosition.y - target.y) <= PlayerSpawnTolerance)
                {
                    atAssignedSquare = true;
                    break;
                }
            }

            if (!atAssignedSquare) playersNotAtSquare++;
        }

        return rankedPlayers > 0 && playersNotAtSquare == 0;
    }

    public static void OnGameStarted()
    {
        TeleportJoinedPlayersToAssignedRoles();
        PlayerSession[] sessions = new PlayerSession[joinedPlayers.Count];
        joinedPlayers.Values.CopyTo(sessions, 0);

        foreach (PlayerSession session in sessions)
        {
            if (session.origin == null) continue;

            if (session.popup != null) session.popup.SetActive(false);
            if (session.leftUiRay != null) session.leftUiRay.enabled = false;
            if (session.rightUiRay != null) session.rightUiRay.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        XROrigin xrOrigin = other.GetComponentInParent<XROrigin>();

        if (xrOrigin == null || joinedPlayers.ContainsKey(xrOrigin)) return;
        FourSquareFusionPlayer networkPlayer = xrOrigin.GetComponent<FourSquareFusionPlayer>();
        if (networkPlayer != null && !networkPlayer.IsLocallyOwned) return;
        if (joinedPlayers.Count >= maximumPlayers) return;

        Transform targetSpawn = FindAvailableSpawn();
        if (targetSpawn == null)
        {
            Debug.LogWarning("PlayerJoinCourt needs one spawn point for each player.");
            return;
        }

        bool gameStarted = GameStateManager.Instance != null && GameStateManager.Instance.HasGameStarted;
        PlayerSession session = new PlayerSession
        {
            origin = xrOrigin,
            court = this,
            leftHand = FindHand(xrOrigin.transform, "Left Hand"),
            rightHand = FindHand(xrOrigin.transform, "Right Hand")
        };

        if (gameStarted)
        {
            SquareRole role;
            if (networkPlayer != null
                && GameStateManager.Instance.TryAssignLateJoinRole(networkPlayer, out role))
            {
                session.squareSpawnPoint = GetSpawnPoint(role);
                if (session.squareSpawnPoint == null)
                {
                    Debug.LogError($"No court spawn point is configured for the {role} role.", this);
                    return;
                }
                TeleportPlayer(xrOrigin, session.squareSpawnPoint, courtBounds);
            }
            else if (networkPlayer == null)
            {
                session.squareSpawnPoint = targetSpawn;
                TeleportPlayer(xrOrigin, targetSpawn, courtBounds);
            }
            else
            {
                return;
            }
        }
        else
        {
            session.squareSpawnPoint = targetSpawn;
            TeleportPlayer(xrOrigin, targetSpawn, courtBounds);
            session.popup = CreateStatusPopup(xrOrigin, out Text statusText, out Button startButton);
            session.statusText = statusText;
            session.startButton = startButton;
            session.leftUiRay = CreateUiRay(session.leftHand);
            session.rightUiRay = CreateUiRay(session.rightHand);
            session.buttonNormalColors = new Dictionary<Button, Color>();
            foreach (Button button in session.popup.GetComponentsInChildren<Button>(true))
            {
                if (button.targetGraphic != null)
                {
                    session.buttonNormalColors.Add(button, button.targetGraphic.color);
                }
            }
        }

        joinedPlayers.Add(xrOrigin, session);
        UpdateAllPopupMessages();
    }

    private Transform GetSpawnPoint(SquareRole role)
    {
        int spawnIndex = GetSpawnIndex(role);
        return squareSpawnPoints != null
            && spawnIndex >= 0
            && spawnIndex < squareSpawnPoints.Length
            ? squareSpawnPoints[spawnIndex]
            : null;
    }

    private static int GetSpawnIndex(SquareRole role)
    {
        return (int)SquareRole.King - (int)role;
    }

    private Transform FindAvailableSpawn()
    {
        if (squareSpawnPoints == null) return null;

        foreach (Transform spawnPoint in squareSpawnPoints)
        {
            if (spawnPoint == null) continue;

            bool occupied = false;
            foreach (PlayerSession session in joinedPlayers.Values)
            {
                if (Vector3.Distance(session.origin.transform.position, spawnPoint.position) < 0.5f)
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied) return spawnPoint;
        }

        return null;
    }

    private static void TeleportPlayer(XROrigin xrOrigin, Transform targetSpawn, BoxCollider courtBounds)
    {
        CharacterController characterController = xrOrigin.GetComponent<CharacterController>();
        if (characterController != null) characterController.enabled = false;

        Vector3 facingDirection = courtBounds != null
            ? courtBounds.bounds.center - targetSpawn.position
            : targetSpawn.forward;
        facingDirection.y = 0f;
        if (facingDirection.sqrMagnitude < 0.0001f)
        {
            facingDirection = targetSpawn.forward;
            facingDirection.y = 0f;
        }

        Quaternion targetRotation = facingDirection.sqrMagnitude >= 0.0001f
            ? Quaternion.LookRotation(facingDirection.normalized, Vector3.up)
            : targetSpawn.rotation;
        xrOrigin.transform.SetPositionAndRotation(targetSpawn.position, targetRotation);
        Physics.SyncTransforms();

        if (characterController != null) characterController.enabled = true;
    }

    private static Transform FindHand(Transform root, string handName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == handName) return child;
        }

        return null;
    }

    private static LineRenderer CreateUiRay(Transform hand)
    {
        if (hand == null) return null;

        GameObject rayObject = new GameObject("UI Selection Ray");
        rayObject.transform.SetParent(hand, false);

        LineRenderer ray = rayObject.AddComponent<LineRenderer>();
        ray.useWorldSpace = true;
        ray.positionCount = 2;
        ray.startWidth = 0.008f;
        ray.endWidth = 0.004f;
        ray.startColor = Color.cyan;
        ray.endColor = Color.cyan;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader != null) ray.material = new Material(shader);

        return ray;
    }

    private static void UpdateControllerUi(
        PlayerSession session,
        Transform hand,
        XRNode handNode,
        LineRenderer uiRay,
        bool isLeftHand,
        ref bool triggerWasPressed)
    {
        if (hand == null || session.popup == null || !session.popup.activeInHierarchy)
        {
            if (uiRay != null) uiRay.enabled = false;
            UpdateHoveredButton(session, isLeftHand, null);
            triggerWasPressed = false;
            return;
        }

        if (uiRay != null) uiRay.enabled = true;

        Ray ray = new Ray(hand.position, hand.forward);
        Vector3 rayEnd = ray.GetPoint(UiRayDistance);
        Button targetButton = null;
        Canvas canvas = session.popup.GetComponent<Canvas>();
        Camera eventCamera = canvas != null ? canvas.worldCamera : null;

        if (canvas != null && eventCamera != null)
        {
            Plane popupPlane = new Plane(canvas.transform.forward, canvas.transform.position);
            if (popupPlane.Raycast(ray, out float hitDistance) && hitDistance >= 0f && hitDistance <= UiRayDistance)
            {
                rayEnd = ray.GetPoint(hitDistance);
                Vector3 screenPoint = eventCamera.WorldToScreenPoint(rayEnd);
                if (screenPoint.z > 0f)
                {
                    foreach (Button button in session.popup.GetComponentsInChildren<Button>())
                    {
                        if (button.isActiveAndEnabled
                            && RectTransformUtility.RectangleContainsScreenPoint(
                                button.transform as RectTransform, screenPoint, eventCamera))
                        {
                            targetButton = button;
                            break;
                        }
                    }
                }
            }
        }

        if (uiRay != null)
        {
            uiRay.SetPosition(0, hand.position);
            uiRay.SetPosition(1, rayEnd);
        }

        UpdateHoveredButton(session, isLeftHand, targetButton);
        bool triggerPressed = ReadControllerTrigger(handNode);
        if (triggerPressed && !triggerWasPressed && targetButton != null && targetButton.interactable)
        {
            Debug.Log($"[FourSquare] {handNode} controller selected '{targetButton.name}'.", session.origin);
            targetButton.onClick.Invoke();
        }

        triggerWasPressed = triggerPressed;
    }

    private static void UpdateHoveredButton(PlayerSession session, bool isLeftHand, Button targetButton)
    {
        Button previousButton = isLeftHand ? session.leftHoveredButton : session.rightHoveredButton;
        if (previousButton == targetButton) return;

        Button otherHandButton = isLeftHand ? session.rightHoveredButton : session.leftHoveredButton;
        if (previousButton != null && previousButton != otherHandButton)
        {
            SetButtonColor(session, previousButton, false);
        }

        if (isLeftHand) session.leftHoveredButton = targetButton;
        else session.rightHoveredButton = targetButton;

        if (targetButton != null) SetButtonColor(session, targetButton, true);
    }

    private static void SetButtonColor(PlayerSession session, Button button, bool hovered)
    {
        if (button == null || button.targetGraphic == null
            || session.buttonNormalColors == null
            || !session.buttonNormalColors.TryGetValue(button, out Color normalColor)) return;

        button.targetGraphic.color = hovered
            ? Color.Lerp(normalColor, Color.white, 0.45f)
            : normalColor;
    }

    private static bool ReadControllerTrigger(XRNode handNode)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(handNode);
        if (!device.isValid) return false;
        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton)) return triggerButton;

        return device.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue) && triggerValue >= 0.75f;
    }

    private bool UpdateExitHold(PlayerSession session)
    {
        bool gameStarted = GameStateManager.Instance != null && GameStateManager.Instance.HasGameStarted;
        InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool buttonHeld = gameStarted
            && rightController.isValid
            && rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondaryButton)
            && secondaryButton;

        if (!buttonHeld)
        {
            session.exitHoldTime = 0f;
            if (session.exitHoldPopup != null)
            {
                session.exitHoldPopup.SetActive(false);
            }
            return false;
        }

        if (session.exitHoldPopup == null && session.rightHand != null)
        {
            CreateExitHoldPopup(session);
        }
        if (session.exitHoldPopup != null)
        {
            session.exitHoldPopup.SetActive(true);
            PositionExitHoldPopup(session);
        }

        session.exitHoldTime += Time.unscaledDeltaTime;
        if (session.exitHoldProgress != null)
        {
            session.exitHoldProgress.fillAmount = Mathf.Clamp01(session.exitHoldTime / exitHoldDuration);
        }

        if (session.exitHoldTime < exitHoldDuration) return false;

        LeaveCourt(session.origin);
        return true;
    }

    private static void CreateExitHoldPopup(PlayerSession session)
    {
        GameObject canvasObject = new GameObject("Exit Hold Progress");
        canvasObject.transform.SetParent(session.rightHand, false);
        canvasObject.transform.localPosition = new Vector3(0f, 0f, 0.12f);
        canvasObject.transform.localRotation = Quaternion.identity;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 110;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(ExitPopupSize, ExitPopupSize);
        canvasRect.localScale = Vector3.one * 0.0015f;

        GameObject backgroundObject = CreateUiObject("Exit Progress Background", canvasObject.transform);
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0.08f, 0.08f);
        backgroundRect.anchorMax = new Vector2(0.92f, 0.92f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image background = backgroundObject.AddComponent<Image>();
        background.sprite = GetExitRingSprite();
        background.color = new Color(1f, 1f, 1f, 0.25f);

        GameObject progressObject = CreateUiObject("Exit Progress", canvasObject.transform);
        RectTransform progressRect = progressObject.GetComponent<RectTransform>();
        progressRect.anchorMin = backgroundRect.anchorMin;
        progressRect.anchorMax = backgroundRect.anchorMax;
        progressRect.offsetMin = Vector2.zero;
        progressRect.offsetMax = Vector2.zero;
        Image progress = progressObject.AddComponent<Image>();
        progress.sprite = background.sprite;
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Radial360;
        progress.fillOrigin = (int)Image.Origin360.Top;
        progress.fillClockwise = true;
        progress.fillAmount = 0f;
        progress.color = Color.white;

        Text label = CreateText("Exit Label", canvasObject.transform, 24, TextAnchor.MiddleCenter);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.18f, 0.18f);
        labelRect.anchorMax = new Vector2(0.82f, 0.82f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.text = "Exit";

        session.exitHoldPopup = canvasObject;
        session.exitHoldProgress = progress;
        canvasObject.SetActive(false);
    }

    private static void PositionExitHoldPopup(PlayerSession session)
    {
        Transform popupTransform = session.exitHoldPopup.transform;
        popupTransform.position = session.rightHand.position + session.rightHand.forward * 0.12f;

        Camera playerCamera = session.origin.Camera;
        if (playerCamera == null) playerCamera = session.origin.GetComponentInChildren<Camera>();
        if (playerCamera == null) return;

        Vector3 towardCamera = playerCamera.transform.position - popupTransform.position;
        if (towardCamera.sqrMagnitude > 0.0001f)
        {
            popupTransform.rotation = Quaternion.LookRotation(-towardCamera.normalized, Vector3.up);
        }
    }

    private static Sprite GetExitRingSprite()
    {
        if (exitRingSprite != null) return exitRingSprite;

        const int textureSize = 96;
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[textureSize * textureSize];
        float center = (textureSize - 1) * 0.5f;
        float innerRadius = textureSize * 0.34f;
        float outerRadius = textureSize * 0.48f;
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                pixels[y * textureSize + x] = distance >= innerRadius && distance <= outerRadius
                    ? Color.white
                    : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        exitRingSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            textureSize);
        return exitRingSprite;
    }

    private static void DestroyUiRay(LineRenderer uiRay)
    {
        if (uiRay == null) return;

        Material rayMaterial = uiRay.material;
        if (rayMaterial != null) Destroy(rayMaterial);
        Destroy(uiRay.gameObject);
    }

    private GameObject CreateStatusPopup(XROrigin xrOrigin, out Text statusText, out Button startButton)
    {
        Camera playerCamera = xrOrigin.Camera;
        if (playerCamera == null) playerCamera = xrOrigin.GetComponentInChildren<Camera>();

        GameObject canvasObject = new GameObject("Court Status Popup");
        canvasObject.transform.SetParent(playerCamera != null ? playerCamera.transform : xrOrigin.transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, 0f, popupDistance);
        canvasObject.transform.localRotation = Quaternion.identity;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = playerCamera;
        canvas.sortingOrder = 100;
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
        canvasRect.localScale = Vector3.one * 0.0015f;

        GameObject panelObject = CreateUiObject("Panel", canvasObject.transform);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelObject.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.08f, 0.94f);

        statusText = CreateText("Status", panelObject.transform, 34, TextAnchor.MiddleCenter);
        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.08f, 0.42f);
        statusRect.anchorMax = new Vector2(0.92f, 0.9f);
        statusRect.offsetMin = Vector2.zero;
        statusRect.offsetMax = Vector2.zero;

        GameObject startButtonObject = CreateUiObject("Start Game Button", panelObject.transform);
        RectTransform startButtonRect = startButtonObject.GetComponent<RectTransform>();
        startButtonRect.anchorMin = new Vector2(0.08f, 0.08f);
        startButtonRect.anchorMax = new Vector2(0.48f, 0.32f);
        startButtonRect.offsetMin = Vector2.zero;
        startButtonRect.offsetMax = Vector2.zero;
        Image startButtonImage = startButtonObject.AddComponent<Image>();
        startButtonImage.color = new Color(0.12f, 0.52f, 0.24f, 1f);
        startButton = startButtonObject.AddComponent<Button>();
        startButton.targetGraphic = startButtonImage;
        startButton.onClick.AddListener(() =>
        {
            if (GameStateManager.Instance == null)
            {
                Debug.LogError("The Start Game button was pressed, but no GameStateManager exists in the scene.");
                return;
            }

            GameStateManager.Instance.StartGame();
        });

        Text startButtonText = CreateText("Start Game Label", startButtonObject.transform, 25, TextAnchor.MiddleCenter);
        startButtonText.text = "START GAME";
        RectTransform startButtonTextRect = startButtonText.rectTransform;
        startButtonTextRect.anchorMin = Vector2.zero;
        startButtonTextRect.anchorMax = Vector2.one;
        startButtonTextRect.offsetMin = Vector2.zero;
        startButtonTextRect.offsetMax = Vector2.zero;

        GameObject exitButtonObject = CreateUiObject("Exit Button", panelObject.transform);
        RectTransform buttonRect = exitButtonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.52f, 0.08f);
        buttonRect.anchorMax = new Vector2(0.92f, 0.32f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        Image exitButtonImage = exitButtonObject.AddComponent<Image>();
        exitButtonImage.color = new Color(0.75f, 0.16f, 0.12f, 1f);

        Button exitButton = exitButtonObject.AddComponent<Button>();
        exitButton.targetGraphic = exitButtonImage;
        exitButton.onClick.AddListener(() => LeaveCourt(xrOrigin));

        Text buttonText = CreateText("Exit Label", exitButtonObject.transform, 25, TextAnchor.MiddleCenter);
        buttonText.text = "EXIT COURT";
        buttonText.color = Color.white;
        RectTransform buttonTextRect = buttonText.rectTransform;
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        return canvasObject;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private static Text CreateText(string objectName, Transform parent, int fontSize, TextAnchor alignment)
    {
        Text text = CreateUiObject(objectName, parent).AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void LeaveCourt(XROrigin xrOrigin)
    {
        if (!joinedPlayers.TryGetValue(xrOrigin, out PlayerSession session)) return;

        FourSquareFusionPlayer networkPlayer = xrOrigin.GetComponent<FourSquareFusionPlayer>();
        bool leaveNetworkSession = networkPlayer != null && networkPlayer.IsLocallyOwned;
        PlayerJoinCourt court = session.court;
        if (court != null && court.exitPoint != null)
        {
            TeleportPlayerToExit(xrOrigin, court.exitPoint);
        }

        RemoveCourtSession(xrOrigin);
        UpdateAllPopupMessages();
        RefreshJoinZoneAuras();

        if (leaveNetworkSession && FourSquareFusionSession.Instance != null)
        {
            _ = FourSquareFusionSession.Instance.DisconnectAsync();
        }
    }

    private static void RemoveCourtSession(XROrigin xrOrigin)
    {
        if (!joinedPlayers.TryGetValue(xrOrigin, out PlayerSession session)) return;

        if (session.exitHoldPopup != null) Destroy(session.exitHoldPopup);
        DestroyUiRay(session.leftUiRay);
        DestroyUiRay(session.rightUiRay);
        if (session.popup != null) Destroy(session.popup);
        joinedPlayers.Remove(xrOrigin);
    }

    private static void TeleportPlayerToExit(XROrigin xrOrigin, Transform exitPoint)
    {
        CharacterController characterController = xrOrigin.GetComponent<CharacterController>();
        Vector3 targetPosition = new Vector3(
            exitPoint.position.x,
            xrOrigin.transform.position.y,
            exitPoint.position.z);

        if (characterController != null) characterController.enabled = false;
        xrOrigin.transform.SetPositionAndRotation(targetPosition, exitPoint.rotation);
        Physics.SyncTransforms();
        if (characterController != null) characterController.enabled = true;
    }

    private static void UpdateAllPopupMessages()
    {
        int minimumPlayers = activeCourts.Count > 0 ? Mathf.Max(1, activeCourts[0].minimumPlayersToStart) : 1;
        int playerCount = GetPlayerCount();
        int playersNeeded = Mathf.Max(0, minimumPlayers - playerCount);
        bool gameStarted = GameStateManager.Instance != null && GameStateManager.Instance.HasGameStarted;
        string message = gameStarted
            ? "GAME STARTED\nTHE BALL IS IN PLAY"
            : playersNeeded > 0
                ? "NOT ENOUGH PLAYERS TO START\nNEED " + playersNeeded + (playersNeeded == 1 ? " MORE PLAYER" : " MORE PLAYERS")
                : playerCount + (playerCount == 1 ? " PLAYER READY\nPRESS START GAME" : " PLAYERS READY\nPRESS START GAME");

        foreach (PlayerSession session in joinedPlayers.Values)
        {
            if (session.statusText != null) session.statusText.text = message;
            if (session.startButton != null) session.startButton.interactable = playersNeeded == 0 && !gameStarted;
        }
    }

    private static int GetPlayerCount()
    {
        foreach (PlayerSession session in joinedPlayers.Values)
        {
            if (session.origin == null) continue;

            FourSquareFusionPlayer networkPlayer = session.origin.GetComponent<FourSquareFusionPlayer>();
            if (networkPlayer != null)
            {
                int connectedPlayers = networkPlayer.ConnectedPlayerCount;
                if (connectedPlayers > 0) return connectedPlayers;
            }
        }

        return joinedPlayers.Count;
    }

    private void LateUpdate()
    {
        if (joinZoneAura != null)
        {
            joinZoneAura.RefreshState();
        }

        int playerCount = GetPlayerCount();
        bool gameStarted = GameStateManager.Instance != null && GameStateManager.Instance.HasGameStarted;
        if (playerCount != lastDisplayedPlayerCount || gameStarted != lastDisplayedGameStarted)
        {
            lastDisplayedPlayerCount = playerCount;
            lastDisplayedGameStarted = gameStarted;
            UpdateAllPopupMessages();
        }

        PlayerSession[] sessions = new PlayerSession[joinedPlayers.Count];
        joinedPlayers.Values.CopyTo(sessions, 0);
        foreach (PlayerSession session in sessions)
        {
            if (session.court == this && session.origin != null)
            {
                if (UpdateExitHold(session)) continue;
                UpdateControllerUi(session, session.leftHand, XRNode.LeftHand,
                    session.leftUiRay, true,
                    ref session.leftTriggerWasPressed);
                UpdateControllerUi(session, session.rightHand, XRNode.RightHand,
                    session.rightUiRay, false, ref session.rightTriggerWasPressed);
                if (!joinedPlayers.ContainsKey(session.origin)) continue;
                ClampPlayerToSquare(session.origin, session.squareSpawnPoint);
            }
        }
    }

    private void ClampPlayerToSquare(XROrigin xrOrigin, Transform squareSpawnPoint)
    {
        if (courtBounds == null || squareSpawnPoint == null) return;

        Transform boundsTransform = courtBounds.transform;
        Vector3 localPosition = boundsTransform.InverseTransformPoint(xrOrigin.transform.position);
        Vector3 localSpawnPosition = boundsTransform.InverseTransformPoint(squareSpawnPoint.position);
        Vector3 halfSize = courtBounds.size * 0.5f;
        CharacterController characterController = xrOrigin.GetComponent<CharacterController>();
        float inset = characterController != null ? characterController.radius : 0.2f;

        float minX = localSpawnPosition.x < courtBounds.center.x ? courtBounds.center.x - halfSize.x : courtBounds.center.x;
        float maxX = localSpawnPosition.x < courtBounds.center.x ? courtBounds.center.x : courtBounds.center.x + halfSize.x;
        float minZ = localSpawnPosition.z < courtBounds.center.z ? courtBounds.center.z - halfSize.z : courtBounds.center.z;
        float maxZ = localSpawnPosition.z < courtBounds.center.z ? courtBounds.center.z : courtBounds.center.z + halfSize.z;

        localPosition.x = Mathf.Clamp(localPosition.x, minX + inset, maxX - inset);
        localPosition.z = Mathf.Clamp(localPosition.z, minZ + inset, maxZ - inset);

        xrOrigin.transform.position = boundsTransform.TransformPoint(localPosition);
    }
}