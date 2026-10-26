
using System;
using UnityEngine;

public class TimeOfDaySkyBox : MonoBehaviour
{
    [Header("Skybox Materials")]
    [SerializeField] private Material middaySkybox;
    [SerializeField] private Material eveningSkybox;
    [SerializeField] private Material midnightSkybox;
    [SerializeField] private Material sunsetSkybox;
    [SerializeField] private Material daybreakSkybox;

    [Header("Time Settings (24-hour format)")]
    [Range(0, 23)] [SerializeField] private int daybreakStart = 5;
    [Range(0, 23)] [SerializeField] private int middayStart = 8;
    [Range(0, 23)] [SerializeField] private int sunsetStart = 17;
    [Range(0, 23)] [SerializeField] private int eveningStart = 19;
    [Range(0, 23)] [SerializeField] private int midnightStart = 21;

    private void Start()
    {
        UpdateSkybox();
    }

    // Recheck periodically in case the game remains open
    // while the real-world hour changes.
    private void Update()
    {
        if (DateTime.Now.Minute == 0 &&
            DateTime.Now.Second == 0)
        {
            UpdateSkybox();
        }
    }

    public void UpdateSkybox()
    {
        int hour = DateTime.Now.Hour;

        Material selectedSkybox;

        if (hour >= midnightStart || hour < daybreakStart)
        {
            selectedSkybox = midnightSkybox;
        }
        else if (hour < middayStart)
        {
            selectedSkybox = daybreakSkybox;
        }
        else if (hour < sunsetStart)
        {
            selectedSkybox = middaySkybox;
        }
        else if (hour < eveningStart)
        {
            selectedSkybox = sunsetSkybox;
        }
        else
        {
            selectedSkybox = eveningSkybox;
        }

        if (selectedSkybox != null)
        {
            RenderSettings.skybox = selectedSkybox;
            DynamicGI.UpdateEnvironment();
        }
    }
}
