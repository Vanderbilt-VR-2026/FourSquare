using TMPro;
using UnityEngine;

[RequireComponent(typeof(Canvas))]
public class RoomCodeOverlayUI : MonoBehaviour
{
    [SerializeField] private TMP_Text roomCodeText;

    private Canvas canvas;
    private MultiplayerRoomManager multiplayerRoomManager;
    private string displayedRoomCode;

    public void AttachToHead(Transform head)
    {
        if (head == null)
        {
            Debug.LogError("Cannot attach the player overlay without a headset transform.", this);
            return;
        }

        Camera headCamera = head.GetComponent<Camera>();
        if (headCamera == null)
        {
            Debug.LogError("Cannot attach the player overlay without a headset camera.", this);
            return;
        }

        if (canvas == null)
            canvas = GetComponent<Canvas>();

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCamera;
        transform.SetParent(head, false);
        transform.localPosition = new Vector3(0f, 0f, .5f);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * 0.001f;
    }

    public void SetVisible(bool isVisible)
    {
        gameObject.SetActive(isVisible);
    }

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    private void Start()
    {
        UpdateRoomCode();
    }

    private void Update()
    {
        UpdateRoomCode();
    }

    private void UpdateRoomCode()
    {
        if (roomCodeText == null) return;

        if (multiplayerRoomManager == null)
            multiplayerRoomManager = MultiplayerRoomManager.Instance;

        if (multiplayerRoomManager == null)
            return;

        string roomCode = multiplayerRoomManager.CurrentRoomCode ?? string.Empty;
        if (roomCode == displayedRoomCode)
            return;

        roomCodeText.text = "Room: " + (string.IsNullOrEmpty(roomCode) ? "----" : roomCode);
        displayedRoomCode = roomCode;
    }
}
