using TMPro;
using UnityEngine;

[RequireComponent(typeof(FourSquareFusionPlayer))]
public class PlayerNameTag : MonoBehaviour
{
    [SerializeField] private GameObject tagPrefab;
    [SerializeField, Min(0f)] private float heightAboveHead = 0.35f;

    private FourSquareFusionPlayer player;
    private TMP_Text text;
    private GameObject tagInstance;
    private Camera viewerCamera;
    private string displayedName;

    private void Awake()
    {
        player = GetComponent<FourSquareFusionPlayer>();
        CreateText();
    }

    private void LateUpdate()
    {
        if (player == null || player.HeadTransform == null || text == null)
            return;

        UpdateText();
        text.transform.position = player.HeadTransform.position + Vector3.up * heightAboveHead;

        if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
            viewerCamera = Camera.main;

        if (viewerCamera == null)
            return;

        Vector3 directionToViewer = viewerCamera.transform.position - text.transform.position;
        if (directionToViewer.sqrMagnitude > 0.0001f)
            text.transform.rotation = Quaternion.LookRotation(-directionToViewer, Vector3.up);
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
            Debug.LogError("Player name tag prefab needs a TextMeshPro component.", tagPrefab);
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
