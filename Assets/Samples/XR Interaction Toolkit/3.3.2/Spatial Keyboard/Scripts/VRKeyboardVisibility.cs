
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;

public class VRKeyboardVisibility : MonoBehaviour
{
    [SerializeField] private GameObject joinRoomPanel;
    [SerializeField] private XRKeyboard keyboard;

    private void Start()
    {
        HideKeyboard();
    }

    private void Update()
    {
        if (joinRoomPanel != null &&
            !joinRoomPanel.activeInHierarchy)
        {
            HideKeyboard();
        }
    }

    public void HideKeyboard()
    {
        if (keyboard != null && keyboard.gameObject.activeInHierarchy)
        {
            keyboard.Close();
        }
    }
}
