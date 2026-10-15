using Fusion;
using UnityEngine;
using Unity.XR.CoreUtils;

public class FusionSceneStartup : MonoBehaviour
{
    private void Awake()
    {
        DisableSceneNetworkRig();
        DisableFallbackCameras();

        NetworkRunner runner = FindAnyObjectByType<NetworkRunner>();
        if (runner == null)
        {
            runner = new GameObject("NetworkRunner").AddComponent<NetworkRunner>();
        }

        FusionBootstrap bootstrap = runner.GetComponent<FusionBootstrap>();
        if (bootstrap == null)
        {
            bootstrap = runner.gameObject.AddComponent<FusionBootstrap>();
        }

        bootstrap.RunnerPrefab = runner;
        bootstrap.StartMode = FusionBootstrap.StartModes.Automatic;
        bootstrap.AutoStartAs = GameMode.AutoHostOrClient;
    }

    private static void DisableSceneNetworkRig()
    {
        XROrigin[] origins = FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        foreach (XROrigin origin in origins)
        {
            if (origin.GetComponent<NetworkObject>() != null)
            {
                origin.gameObject.SetActive(false);
            }
        }
    }

    private static void DisableFallbackCameras()
    {
        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Camera camera in cameras)
        {
            if (camera.GetComponentInParent<XROrigin>() != null)
            {
                continue;
            }

            camera.enabled = false;

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }
    }
}
