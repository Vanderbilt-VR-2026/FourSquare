using TMPro;
using UnityEngine;

[RequireComponent(typeof(FourSquareFusionPlayer))]
public class PlayerNameTag : MonoBehaviour
{
    [SerializeField] private GameObject tagPrefab;
    [SerializeField, Min(0f)] private float heightAboveHead = 0.35f;
    [SerializeField, Min(0.1f)] private float billboardTurnSpeed = 12f;

    private FourSquareFusionPlayer player;
    private TMP_Text text;
    private GameObject tagInstance;
    private Camera viewerCamera;
    private string displayedName;
    private Canvas worldCanvas;

    private void Awake()
    {
        player = GetComponent<FourSquareFusionPlayer>();
        CreateText();
    }

    private void LateUpdate()
    {
        if (player == null || tagInstance == null || text == null)
            return;

        Transform head = player.HeadTransform;
        if (head == null)
            return;

        bool showTag = !player.IsLocallyOwned;
        if (tagInstance.activeSelf != showTag)
            tagInstance.SetActive(showTag);
        if (!showTag)
            return;

        UpdateText();
        if (tagInstance.transform.parent != head)
            tagInstance.transform.SetParent(head, false);
        tagInstance.transform.position = head.position + Vector3.up * heightAboveHead;

        if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
            viewerCamera = Camera.main;

        if (viewerCamera == null)
            return;

        if (worldCanvas != null)
            worldCanvas.worldCamera = viewerCamera;

        Vector3 directionToViewer = viewerCamera.transform.position - tagInstance.transform.position;
        if (directionToViewer.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(directionToViewer, Vector3.up);
        float blend = 1f - Mathf.Exp(-billboardTurnSpeed * Time.deltaTime);
        tagInstance.transform.rotation =
            Quaternion.Slerp(tagInstance.transform.rotation, targetRotation, blend);
    }

    private void CreateText()
    {
        if (tagPrefab == null)
        {
            Debug.LogError("Player name tag prefab is not assigned.", this);
            return;
        }

        tagInstance = Instantiate(tagPrefab, transform);
        tagInstance.name = tagPrefab.name;
        text = tagInstance.GetComponentInChildren<TMP_Text>(true);
        if (text == null)
        {
            Debug.LogError("Player name tag prefab needs a TextMeshPro component.", tagPrefab);
            return;
        }

        worldCanvas = tagInstance.GetComponent<Canvas>();
        if (worldCanvas == null)
            worldCanvas = tagInstance.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        worldCanvas.worldCamera = Camera.main;

        RectTransform canvasRect = worldCanvas.transform as RectTransform;
        if (canvasRect != null)
            canvasRect.sizeDelta = new Vector2(220f, 48f);
        tagInstance.transform.localScale = Vector3.one * 0.001f;

        text.fontSize = 24f;
        text.fontStyle = FontStyles.Normal;
        text.color = new Color(1f, 1f, 1f, 0.9f);
        text.alignment = TextAlignmentOptions.Center;
    }

    private void UpdateText()
    {
        string playerName = player.DisplayName;
        if (playerName == displayedName)
            return;

        text.text = playerName;
        displayedName = playerName;
    }
}
