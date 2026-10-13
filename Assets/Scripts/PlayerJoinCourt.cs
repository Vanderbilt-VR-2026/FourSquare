using UnityEngine;
using Unity.XR.CoreUtils;

public class PlayerJoinCourt : MonoBehaviour
{
    [Header("Court Spawn Points (Index 0 = Sq 1, Index 3 = Sq 4)")]
    public Transform[] squareSpawnPoints; 

    private void OnTriggerEnter(Collider other)
    {
        // Check if the object entering has the XROrigin component (or is inside the rig)
        XROrigin xrOrigin = other.GetComponentInParent<XROrigin>();

        if (xrOrigin != null)
        {
            Transform playerRoot = xrOrigin.transform;
            Debug.Log($"XR Player detected via collider: {playerRoot.name}");

            if (squareSpawnPoints != null && squareSpawnPoints.Length > 0)
            {
                Transform targetSpawn = squareSpawnPoints[squareSpawnPoints.Length - 1];

                if (targetSpawn != null)
                {
                    // Temporarily disable CharacterController if you have one
                    CharacterController cc = playerRoot.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    // Teleport the player root
                    playerRoot.position = targetSpawn.position;
                    playerRoot.rotation = targetSpawn.rotation;

                    if (cc != null) cc.enabled = true;

                    Debug.Log($"SUCCESS! Teleported XR player to {targetSpawn.name}");
                    gameObject.SetActive(false); // Disable join zone
                }
            }
        }
    }
}