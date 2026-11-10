
using UnityEngine;

public class TimeOfDaySkyBox : MonoBehaviour
{
    [Header("In-Game Clock")]
    [Range(0, 23)]
    [SerializeField] private float startingHour = 8f;

    [Tooltip("Real-world seconds for one full in-game day.")]
    [Min(1f)]
    [SerializeField] private float dayLengthInSeconds = 1200f;

    [SerializeField] private bool clockRuns = true;

    public float CurrentHour { get; private set; }

    [Header("Skybox Materials")]
    [SerializeField] private Material middaySkybox;
    [SerializeField] private Material eveningSkybox;
    [SerializeField] private Material midnightSkybox;
    [SerializeField] private Material sunsetSkybox;
    [SerializeField] private Material daybreakSkybox;

    [Header("Directional Light")]
    [SerializeField] private Light sunLight;

    [Header("Midday Lighting")]
    [SerializeField] private Color middayColor = Color.white;
    [SerializeField] private float middayIntensity = 1.2f;

    [Header("Sunset Lighting")]
    [SerializeField] private Color sunsetColor =
        new Color(1f, 0.55f, 0.3f);
    [SerializeField] private float sunsetIntensity = 0.8f;

    [Header("Evening Lighting")]
    [SerializeField] private Color eveningColor =
        new Color(0.4f, 0.45f, 0.7f);
    [SerializeField] private float eveningIntensity = 0.3f;

    [Header("Midnight Lighting")]
    [SerializeField] private Color midnightColor =
        new Color(0.15f, 0.18f, 0.3f);
    [SerializeField] private float midnightIntensity = 0.1f;

    [Header("Daybreak Lighting")]
    [SerializeField] private Color daybreakColor =
        new Color(1f, 0.65f, 0.4f);
    [SerializeField] private float daybreakIntensity = 0.6f;

    [Header("Time Settings")]
    [Range(0, 23)] [SerializeField] private int daybreakStart = 5;
    [Range(0, 23)] [SerializeField] private int middayStart = 8;
    [Range(0, 23)] [SerializeField] private int sunsetStart = 17;
    [Range(0, 23)] [SerializeField] private int eveningStart = 19;
    [Range(0, 23)] [SerializeField] private int midnightStart = 21;

    private void Start()
    {
        CurrentHour = startingHour;
        UpdateSkybox();
    }

    private void Update()
    {
        if (!clockRuns)
            return;

        // Advance the in-game clock.
        CurrentHour += Time.deltaTime * 24f / dayLengthInSeconds;
        CurrentHour %= 24f;

        UpdateSkybox();
    }

    public void UpdateSkybox()
    {
        int hour = Mathf.FloorToInt(CurrentHour);

        Material selectedSkybox;
        Color selectedColor;
        float selectedIntensity;

        if (hour >= midnightStart || hour < daybreakStart)
        {
            selectedSkybox = midnightSkybox;
            selectedColor = midnightColor;
            selectedIntensity = midnightIntensity;
        }
        else if (hour < middayStart)
        {
            selectedSkybox = daybreakSkybox;
            selectedColor = daybreakColor;
            selectedIntensity = daybreakIntensity;
        }
        else if (hour < sunsetStart)
        {
            selectedSkybox = middaySkybox;
            selectedColor = middayColor;
            selectedIntensity = middayIntensity;
        }
        else if (hour < eveningStart)
        {
            selectedSkybox = sunsetSkybox;
            selectedColor = sunsetColor;
            selectedIntensity = sunsetIntensity;
        }
        else
        {
            selectedSkybox = eveningSkybox;
            selectedColor = eveningColor;
            selectedIntensity = eveningIntensity;
        }

        if (selectedSkybox != null &&
            RenderSettings.skybox != selectedSkybox)
        {
            RenderSettings.skybox = selectedSkybox;
            DynamicGI.UpdateEnvironment();
        }

        if (sunLight != null)
        {
            sunLight.color = selectedColor;
            sunLight.intensity = selectedIntensity;
        }
    }
}
