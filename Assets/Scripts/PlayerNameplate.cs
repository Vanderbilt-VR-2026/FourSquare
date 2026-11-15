using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerNameplate : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private Camera viewCamera;

    private void Awake()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
    }

    public void Initialize(
        Transform head,
        string playerName,
        Camera playerCamera,
        Vector3 localOffset,
        bool hidden)
    {
        if (head == null)
        {
            Debug.LogError("PlayerNameplate needs a head transform.", this);
            return;
        }

        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            Debug.LogError("PlayerNameplate needs a TextMeshPro component.", this);
            return;
        }

        transform.SetParent(head, false);
        transform.localPosition = localOffset;
        viewCamera = playerCamera;
        label.text = playerName;
        gameObject.SetActive(!hidden);
    }

    private void LateUpdate()
    {
        if (viewCamera == null)
            viewCamera = Camera.main;
        if (viewCamera == null)
            return;

        Vector3 towardNameplate = transform.position - viewCamera.transform.position;
        if (towardNameplate.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(towardNameplate, Vector3.up);
    }
}
