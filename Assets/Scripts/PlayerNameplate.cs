using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerNameplate : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField, Min(0.1f)] private float billboardTurnSpeed = 12f;

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

        Vector3 towardViewer = viewCamera.transform.position - transform.position;
        if (towardViewer.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(towardViewer, Vector3.up);
        float blend = 1f - Mathf.Exp(-billboardTurnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
    }
}
