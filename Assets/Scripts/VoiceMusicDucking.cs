using System;
using System.Reflection;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Ducks the Music mixer group while Photon Voice reports speech activity.
/// Local voice activity uses the Recorder's transmitting state (with VAD enabled).
/// Remote playback detection is optional and approximate: Speaker.IsPlaying may
/// mean a stream is active rather than that speech is occurring.
/// </summary>
public class VoiceMusicDucking : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string exposedMusicVolume = "MusicVolume";
    [SerializeField] private float normalVolumeDb = 0f;
    [SerializeField] private float speakingVolumeDb = -15f;
    [SerializeField, Min(0.01f)] private float fadeDownSeconds = 0.25f;
    [SerializeField, Min(0.01f)] private float fadeUpSeconds = 1.0f;
    [SerializeField, Min(0f)] private float silenceHoldSeconds = 0.5f;

    [Header("Detection")]
    [Tooltip("Includes the local microphone. Enable Voice Detection on the Photon Recorder.")]
    [SerializeField] private bool detectLocalRecorder = true;
    [Tooltip("Approximation only: some Photon Speaker versions report IsPlaying while silent.")]
    [SerializeField] private bool detectRemoteSpeakerPlayback = false;
    [SerializeField, Min(0.1f)] private float rescanInterval = 1f;

    private Recorder[] recorders = Array.Empty<Recorder>();
    private Speaker[] speakers = Array.Empty<Speaker>();
    private float nextScanTime;
    private float lastVoiceTime = float.NegativeInfinity;
    private float currentVolumeDb;
    private bool manualSpeech;
    private bool warnedMissingMixer;

    private void OnEnable()
    {
        currentVolumeDb = normalVolumeDb;
        nextScanTime = 0f;
        lastVoiceTime = float.NegativeInfinity;
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextScanTime)
        {
            recorders = FindObjectsByType<Recorder>(FindObjectsSortMode.None);
            speakers = FindObjectsByType<Speaker>(FindObjectsSortMode.None);
            nextScanTime = Time.unscaledTime + rescanInterval;
        }

        bool speaking = manualSpeech;
        if (detectLocalRecorder)
        {
            foreach (Recorder recorder in recorders)
            {
                if (recorder != null && recorder.isActiveAndEnabled &&
                    ReadBoolProperty(recorder, "IsCurrentlyTransmitting"))
                {
                    speaking = true;
                    break;
                }
            }
        }

        if (detectRemoteSpeakerPlayback)
        {
            foreach (Speaker speaker in speakers)
            {
                if (speaker != null && speaker.isActiveAndEnabled &&
                    ReadBoolProperty(speaker, "IsPlaying"))
                {
                    speaking = true;
                    break;
                }
            }
        }

        if (speaking) lastVoiceTime = Time.unscaledTime;
        bool duck = Time.unscaledTime - lastVoiceTime < silenceHoldSeconds;
        float target = duck ? speakingVolumeDb : normalVolumeDb;
        float duration = duck ? fadeDownSeconds : fadeUpSeconds;
        float speed = Mathf.Abs(normalVolumeDb - speakingVolumeDb) / Mathf.Max(0.01f, duration);
        currentVolumeDb = Mathf.MoveTowards(currentVolumeDb, target, speed * Time.unscaledDeltaTime);

        if (audioMixer != null)
        {
            if (!audioMixer.SetFloat(exposedMusicVolume, currentVolumeDb))
                Debug.LogWarning("VoiceMusicDucking: Exposed mixer parameter not found: " + exposedMusicVolume, this);
        }
        else if (!warnedMissingMixer)
        {
            warnedMissingMixer = true;
            Debug.LogWarning("VoiceMusicDucking: Assign FourSquareAudioMixer in the Inspector.", this);
        }
    }

    // Optional hook for a more accurate remote voice activity detector.
    public void SetSomeoneSpeaking(bool speaking) => manualSpeech = speaking;

    private static bool ReadBoolProperty(object instance, string name)
    {
        if (instance == null) return false;
        PropertyInfo property = instance.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance);
        if (property == null || property.PropertyType != typeof(bool)) return false;
        try { return (bool)property.GetValue(instance); }
        catch { return false; }
    }

    private void OnDisable()
    {
        if (audioMixer != null) audioMixer.SetFloat(exposedMusicVolume, normalVolumeDb);
    }
}
