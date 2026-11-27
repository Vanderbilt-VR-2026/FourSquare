
using UnityEngine;

public class KeyboardPosition : MonoBehaviour
{
    [SerializeField] private Transform menuCanvas;
    [SerializeField] private RectTransform joinRoomPanel;
    [SerializeField] private Transform keyboard;

    [Header("Position Relative to Menu")]
    [SerializeField] private float horizontalOffset = 0f;
    [SerializeField] private float verticalOffset = 0.75f;
    [SerializeField] private float forwardOffset = -0.2f;

    private void LateUpdate()
    {
        if (menuCanvas == null || keyboard == null)
            return;

        Vector3 panelCenter = menuCanvas.position;
        if (joinRoomPanel != null)
            panelCenter = joinRoomPanel.TransformPoint(joinRoomPanel.rect.center);

        keyboard.position =
            panelCenter
            + menuCanvas.right * horizontalOffset
            + menuCanvas.up * verticalOffset
            + menuCanvas.forward * forwardOffset;

        keyboard.rotation = menuCanvas.rotation;
    }
}
