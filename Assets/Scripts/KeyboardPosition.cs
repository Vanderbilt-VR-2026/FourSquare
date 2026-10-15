
using UnityEngine;

public class KeyboardPosition : MonoBehaviour
{
    [SerializeField] private Transform menuCanvas;
    [SerializeField] private Transform keyboard;

    [Header("Position Relative to Menu")]
    [SerializeField] private float horizontalOffset = 0f;
    [SerializeField] private float verticalOffset = -0.65f;
    [SerializeField] private float forwardOffset = -0.2f;

    private void LateUpdate()
    {
        if (menuCanvas == null || keyboard == null)
            return;

        keyboard.position =
            menuCanvas.position
            + menuCanvas.right * horizontalOffset
            + menuCanvas.up * verticalOffset
            + menuCanvas.forward * forwardOffset;

        keyboard.rotation = menuCanvas.rotation;
    }
}
