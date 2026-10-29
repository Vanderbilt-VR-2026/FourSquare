using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class CourtLeaderboardBillboard : MonoBehaviour
{
    private const float RefreshInterval = 1f;
    private const float CanvasScale = 0.004f;
    private const int BoardWidth = 2000;
    private const int BoardHeight = 1400;
    public const float WorldWidth = BoardWidth * CanvasScale;
    public const float WorldHeight = BoardHeight * CanvasScale;
    private const int WaitingFontSize = 150;
    private const int MinimumRankingFontSize = 12;
    private const int MaximumPlayerNameLength = 20;

    private readonly Dictionary<string, RoleTimer> roleTimers = new Dictionary<string, RoleTimer>();
    private readonly Dictionary<string, float> fractionalScorePoints = new Dictionary<string, float>();
    private readonly Dictionary<string, FourSquareFusionPlayer> networkPlayers =
        new Dictionary<string, FourSquareFusionPlayer>();
    private BoxCollider courtBounds;
    private Transform anchor;
    private Transform[] squareSpawnPoints;
    private Transform boardTransform;
    private Text rosterText;
    private float heightOffset;
    private float nextRefreshTime;
    private bool previousGameStarted;

    public Transform BoardTransform => boardTransform;

    private struct RoleTimer
    {
        public SquareRole role;
        public float startedAt;
    }

    public void Initialize(BoxCollider bounds, Transform[] spawnPoints, float verticalOffset)
    {
        courtBounds = bounds;
        squareSpawnPoints = spawnPoints;
        anchor = GetBoardAnchor(transform, bounds);
        heightOffset = verticalOffset;
        Debug.Log(
            $"[CourtLeaderboard] Initialize on '{name}' in scene '{gameObject.scene.name}': " +
            $"offset={heightOffset:F3}, bounds={(courtBounds != null ? courtBounds.name : "NULL")}, " +
            $"spawnArray={(squareSpawnPoints != null ? squareSpawnPoints.Length.ToString() : "NULL")}, " +
            $"anchor={(anchor != null ? anchor.name : "NULL")}.",
            this);
        CreateBoard();
    }

    public static Transform GetBoardAnchor(Transform owner, BoxCollider bounds)
    {
        return bounds != null && bounds.transform.parent != null
            ? bounds.transform.parent
            : owner;
    }

    public static Vector3 GetBoardPosition(
        Transform anchor, BoxCollider bounds, Transform[] spawnPoints, float verticalOffset)
    {
        Vector3 spawnCenter = Vector3.zero;
        float courtTop = float.NegativeInfinity;
        int spawnCount = 0;
        if (spawnPoints != null)
        {
            foreach (Transform spawnPoint in spawnPoints)
            {
                if (spawnPoint == null) continue;

                Vector3 spawnPosition = spawnPoint.position;
                spawnCenter += spawnPosition;
                courtTop = Mathf.Max(courtTop, spawnPosition.y);
                spawnCount++;
            }
        }

        if (spawnCount > 0)
        {
            spawnCenter /= spawnCount;
            return new Vector3(
                spawnCenter.x,
                courtTop + verticalOffset + WorldHeight * 0.5f,
                spawnCenter.z);
        }

        if (bounds != null)
        {
            Bounds court = bounds.bounds;
            return new Vector3(
                court.center.x,
                court.max.y + verticalOffset + WorldHeight * 0.5f,
                court.center.z);
        }

        return anchor.position + Vector3.up * (verticalOffset + WorldHeight * 0.5f);
    }

    public static void GetBoardPose(
        Transform anchor, BoxCollider bounds, Transform[] spawnPoints, float verticalOffset,
        out Vector3 position, out Quaternion rotation)
    {
        position = GetBoardPosition(anchor, bounds, spawnPoints, verticalOffset);
        rotation = anchor.rotation;
    }

    private void OnEnable()
    {
        if (boardTransform != null) boardTransform.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (boardTransform != null) boardTransform.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (boardTransform != null) Destroy(boardTransform.gameObject);
    }

    private void Update()
    {
        GameStateManager manager = GameStateManager.Instance;
        if (manager == null)
        {
            rosterText.alignment = TextAnchor.MiddleCenter;
            SetRosterText("WAITING FOR PLAYERS", WaitingFontSize);
            return;
        }

        bool gameStarted = manager.HasGameStarted;
        if (gameStarted && !previousGameStarted)
        {
            roleTimers.Clear();
            nextRefreshTime = 0f;
        }
        previousGameStarted = gameStarted;

        if (gameStarted)
        {
            AccrueRoleScores(manager);
        }

        if (Time.unscaledTime < nextRefreshTime) return;

        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        RefreshNetworkPlayers();
        UpdateRoleTimers(manager, gameStarted);
        RenderBoard(manager, gameStarted);
    }

    private void LateUpdate()
    {
        UpdateBoardPosition();
    }

    private void UpdateRoleTimers(GameStateManager manager, bool gameStarted)
    {
        if (!gameStarted) return;

        HashSet<string> currentPlayers = new HashSet<string>();
        foreach (FourSquarePlayer player in manager.Players)
        {
            if (player == null || string.IsNullOrEmpty(player.playerId)) continue;
            currentPlayers.Add(player.playerId);
            SquareRole role = GetPlayerRole(player);
            if (!IsRank(role))
            {
                roleTimers.Remove(player.playerId);
                continue;
            }

            if (!roleTimers.TryGetValue(player.playerId, out RoleTimer timer)
                || timer.role != role)
            {
                roleTimers[player.playerId] = new RoleTimer
                {
                    role = role,
                    startedAt = Time.unscaledTime
                };
            }
        }

        List<string> departedPlayers = new List<string>();
        foreach (string playerId in roleTimers.Keys)
        {
            if (!currentPlayers.Contains(playerId)) departedPlayers.Add(playerId);
        }
        foreach (string playerId in departedPlayers)
        {
            roleTimers.Remove(playerId);
        }
    }

    private void AccrueRoleScores(GameStateManager manager)
    {
        float elapsed = Time.unscaledDeltaTime;
        if (elapsed <= 0f) return;

        foreach (FourSquarePlayer player in manager.Players)
        {
            if (player == null || string.IsNullOrEmpty(player.playerId) || !IsRank(player.currentRole))
            {
                continue;
            }

            fractionalScorePoints.TryGetValue(player.playerId, out float fractionalPoints);
            fractionalPoints += elapsed * (int)player.currentRole;

            int wholePoints = Mathf.FloorToInt(fractionalPoints);
            if (wholePoints > 0)
            {
                player.score += wholePoints;
                fractionalPoints -= wholePoints;
            }

            fractionalScorePoints[player.playerId] = fractionalPoints;
        }
    }

    private void RefreshNetworkPlayers()
    {
        networkPlayers.Clear();
        FourSquareFusionPlayer[] players =
            FindObjectsByType<FourSquareFusionPlayer>(FindObjectsSortMode.None);
        foreach (FourSquareFusionPlayer player in players)
        {
            if (player != null && !string.IsNullOrEmpty(player.PlayerId))
            {
                networkPlayers[player.PlayerId] = player;
            }
        }
    }

    private void RenderBoard(GameStateManager manager, bool gameStarted)
    {
        List<FourSquarePlayer> players = new List<FourSquarePlayer>();
        foreach (FourSquarePlayer player in manager.Players)
        {
            if (player != null) players.Add(player);
        }

        StringBuilder diagnostics = new StringBuilder(
            $"[CourtLeaderboard] Refresh: gameStarted={gameStarted}, " +
            $"managerPlayers={players.Count}, networkPlayers={networkPlayers.Count}.");
        foreach (FourSquarePlayer player in players)
        {
            bool foundNetworkPlayer =
                networkPlayers.TryGetValue(player.playerId, out FourSquareFusionPlayer networkPlayer);
            SquareRole displayedRole = GetPlayerRole(player);
            diagnostics.Append(
                $" [managerId='{player.playerId}', localRole={player.currentRole}, " +
                $"networkMatch={foundNetworkPlayer}");
            if (foundNetworkPlayer)
            {
                bool hasStateAuthority = networkPlayer.Object != null
                    && networkPlayer.Object.IsValid
                    && networkPlayer.Object.HasStateAuthority;
                diagnostics.Append(
                    $", networkId='{networkPlayer.PlayerId}', networkRoleValue={networkPlayer.NetworkRole}, " +
                    $"networkRole={networkPlayer.PlayerRole}, " +
                    $"stateAuthority={hasStateAuthority}");
            }
            diagnostics.Append($", displayedRole={displayedRole}]");
        }
        Debug.Log(diagnostics.ToString(), this);

        players.Sort((left, right) => GetPlayerRole(right).CompareTo(GetPlayerRole(left)));

        if (!gameStarted)
        {
            rosterText.alignment = TextAnchor.MiddleCenter;
            SetRosterText("WAITING FOR PLAYERS", WaitingFontSize);
            return;
        }

        rosterText.alignment = TextAnchor.MiddleLeft;
        StringBuilder text = new StringBuilder();
        text.AppendLine("COURT LEADERBOARD");
        text.AppendLine("PLAYER | TIME IN RANK | RANK | SCORE");
        text.AppendLine("-----------------------------------------------");
        foreach (FourSquarePlayer player in players)
        {
            SquareRole role = GetPlayerRole(player);
            if (!IsRank(role)) continue;

            string roleColor = GetRoleColor(role);
            string playerName = GetPlayerName(player);
            string duration = roleTimers.TryGetValue(player.playerId, out RoleTimer timer)
                ? FormatDuration(Time.unscaledTime - timer.startedAt)
                : "00:00";
            text.Append("<color=")
                .Append(roleColor)
                .Append('>')
                .Append(EscapeRichText(playerName))
                .Append(" | ")
                .Append(duration)
                .Append(" | ")
                .Append(role.ToString().ToUpperInvariant())
                .Append(" | ")
                .Append(player.score)
                .AppendLine("</color>");
        }

        string boardText = text.Length > 0
            && players.Exists(player => IsRank(GetPlayerRole(player)))
            ? text.ToString()
            : "COURT LEADERBOARD\nNO PLAYERS";
        SetRosterText(boardText);
    }

    private SquareRole GetPlayerRole(FourSquarePlayer player)
    {
        return networkPlayers.TryGetValue(player.playerId, out FourSquareFusionPlayer networkPlayer)
            ? networkPlayer.PlayerRole
            : player.currentRole;
    }

    private void SetRosterText(string value, int maximumFontSize = 0)
    {
        rosterText.text = value;
        RectTransform textRect = rosterText.rectTransform;
        Vector2 availableSize = textRect.rect.size;
        TextGenerator generator = rosterText.cachedTextGeneratorForLayout;
        TextGenerationSettings settings = rosterText.GetGenerationSettings(availableSize);

        int min = MinimumRankingFontSize;
        int max = Mathf.CeilToInt(Mathf.Min(availableSize.x, availableSize.y));
        if (maximumFontSize > 0) max = Mathf.Min(max, maximumFontSize);
        int bestFit = min;
        while (min <= max)
        {
            int candidate = min + (max - min) / 2;
            settings.fontSize = candidate;
            float preferredWidth = generator.GetPreferredWidth(value, settings);
            float preferredHeight = generator.GetPreferredHeight(value, settings);
            if (preferredWidth <= availableSize.x && preferredHeight <= availableSize.y)
            {
                bestFit = candidate;
                min = candidate + 1;
            }
            else
            {
                max = candidate - 1;
            }
        }

        rosterText.fontSize = bestFit;
    }

    private string GetPlayerName(FourSquarePlayer player)
    {
        string playerName;
        if (networkPlayers.TryGetValue(player.playerId, out FourSquareFusionPlayer networkPlayer))
        {
            playerName = networkPlayer.DisplayName;
        }
        else
        {
            playerName = string.IsNullOrWhiteSpace(player.playerName)
                ? "Player " + player.playerId
                : player.playerName;
        }

        return playerName.Length <= MaximumPlayerNameLength
            ? playerName
            : playerName.Substring(0, MaximumPlayerNameLength - 3) + "...";
    }

    private void CreateBoard()
    {
        GameObject canvasObject = new GameObject("Court Leaderboard", typeof(RectTransform));
        canvasObject.transform.SetParent(anchor, false);
        canvasObject.transform.localScale = Vector3.one * CanvasScale;
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        boardTransform = canvasRect;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 90;

        canvasRect.sizeDelta = new Vector2(BoardWidth, BoardHeight);

        GameObject panelObject = new GameObject("Leaderboard Background", typeof(RectTransform));
        panelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image background = panelObject.AddComponent<Image>();
        background.color = new Color(0.035f, 0.055f, 0.08f, 0.92f);
        panelObject.AddComponent<RectMask2D>();

        rosterText = CreateText("Leaderboard Players", panelObject.transform, WaitingFontSize, TextAnchor.MiddleCenter);
        RectTransform rosterRect = rosterText.rectTransform;
        rosterRect.anchorMin = new Vector2(0.02f, 0.03f);
        rosterRect.anchorMax = new Vector2(0.98f, 0.97f);
        rosterRect.offsetMin = Vector2.zero;
        rosterRect.offsetMax = Vector2.zero;
        rosterText.lineSpacing = 1.2f;

        ApplyBoardPose();
        GetBoardPose(anchor, courtBounds, squareSpawnPoints, heightOffset,
            out Vector3 targetPosition, out Quaternion targetRotation);
        Debug.Log(
            $"[CourtLeaderboard] Created '{canvasObject.name}': parent='{canvasObject.transform.parent?.name}', " +
            $"offset={heightOffset:F3}, targetPosition={targetPosition}, actualPosition={boardTransform.position}, " +
            $"targetRotation={targetRotation.eulerAngles}, actualRotation={boardTransform.rotation.eulerAngles}, " +
            $"localPosition={boardTransform.localPosition}, " +
            $"lossyScale={boardTransform.lossyScale}, rectSize={canvasRect.sizeDelta}, " +
            $"bounds={(courtBounds != null ? courtBounds.bounds.ToString() : "NULL")}, " +
            $"spawnPositions={GetSpawnPositionsForLog()}.",
            this);
    }

    private void UpdateBoardPosition()
    {
        if (boardTransform == null || anchor == null) return;

        ApplyBoardPose();
    }

    private void ApplyBoardPose()
    {
        if (boardTransform == null || anchor == null) return;

        GetBoardPose(anchor, courtBounds, squareSpawnPoints, heightOffset,
            out Vector3 position, out Quaternion rotation);
        boardTransform.SetPositionAndRotation(position, rotation);
    }

    private string GetSpawnPositionsForLog()
    {
        if (squareSpawnPoints == null) return "NULL";

        StringBuilder positions = new StringBuilder("[");
        for (int i = 0; i < squareSpawnPoints.Length; i++)
        {
            if (i > 0) positions.Append(", ");
            Transform spawnPoint = squareSpawnPoints[i];
            if (spawnPoint == null)
            {
                positions.Append("NULL/DESTROYED");
                continue;
            }

            Vector3 spawnPosition = spawnPoint.position;
            positions.Append(spawnPosition);
        }
        return positions.Append(']').ToString();
    }

    private static Text CreateText(string objectName, Transform parent, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static bool IsRank(SquareRole role)
    {
        int rank = (int)role;
        return rank >= (int)SquareRole.Peasant && rank <= (int)SquareRole.King;
    }

    private static string GetRoleColor(SquareRole role)
    {
        switch (role)
        {
            case SquareRole.King:
                return "#FFD700";
            case SquareRole.Queen:
                return "#C0C0C0";
            case SquareRole.Jack:
                return "#CD7F32";
            case SquareRole.Peasant:
                return "#A9A9A9";
            default:
                return "#FFFFFF";
        }
    }

    private static string FormatDuration(float elapsedSeconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsedSeconds));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return minutes.ToString("00") + ":" + seconds.ToString("00");
    }

    private static string EscapeRichText(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
