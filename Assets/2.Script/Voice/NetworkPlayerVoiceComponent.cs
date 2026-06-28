using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(VoiceNetworkObject))]
public sealed class NetworkPlayerVoiceComponent : MonoBehaviour
{
    private const float DefaultMinDistance = 1.5f;
    private const float DefaultMaxDistance = 50f;

    [SerializeField] private Transform voiceAnchor;
    [SerializeField] private VoiceNetworkObject voiceNetworkObject;
    [SerializeField] private Speaker speaker;
    [SerializeField] private AudioSource speakerAudioSource;
    [SerializeField] private AudioReverbFilter speakerReverbFilter;
    [SerializeField] private AudioReverbPreset voiceReverbPreset = AudioReverbPreset.Hallway;
    [SerializeField] private float minDistance = DefaultMinDistance;
    [SerializeField] private float maxDistance = DefaultMaxDistance;

    public Transform VoiceAnchor => voiceAnchor;
    public VoiceNetworkObject VoiceNetworkObject => voiceNetworkObject;
    public Speaker Speaker => speaker;
    public AudioSource SpeakerAudioSource => speakerAudioSource;
    public AudioReverbFilter SpeakerReverbFilter => speakerReverbFilter;

    private void Reset()
    {
        AutoWire();
        ConfigureAudioSource(VoiceChatSettingsService.Current.OutputVolume);
    }

    private void Awake()
    {
        AutoWire();
        ConfigureAudioSource(VoiceChatSettingsService.Current.OutputVolume);
    }

    private void OnEnable()
    {
        VoiceChatSettingsService.SettingsChanged += OnVoiceSettingsChanged;
    }

    private void OnDisable()
    {
        VoiceChatSettingsService.SettingsChanged -= OnVoiceSettingsChanged;
    }

    public static void ApplyOutputVolumeToAll(float volume)
    {
        NetworkPlayerVoiceComponent[] components = FindObjectsByType<NetworkPlayerVoiceComponent>(FindObjectsSortMode.None);
        foreach (NetworkPlayerVoiceComponent component in components)
            component.ConfigureAudioSource(volume);
    }

    public void AutoWire()
    {
        if (voiceNetworkObject == null)
            voiceNetworkObject = GetComponent<VoiceNetworkObject>();

        if (voiceAnchor == null)
            voiceAnchor = FindVoiceAnchor();

        if (speaker == null)
            speaker = GetComponentInChildren<Speaker>(true);

        if (speakerAudioSource == null && speaker != null)
            speakerAudioSource = speaker.GetComponent<AudioSource>();

        if (speakerReverbFilter == null && speaker != null)
            speakerReverbFilter = speaker.GetComponent<AudioReverbFilter>();
        if (speakerReverbFilter == null && speaker != null)
            speakerReverbFilter = speaker.gameObject.AddComponent<AudioReverbFilter>();
    }

    public void ConfigureAudioSource(float outputVolume)
    {
        AutoWire();
        if (speakerAudioSource == null)
            return;

        speakerAudioSource.playOnAwake = true;
        speakerAudioSource.spatialBlend = 1f;
        speakerAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        speakerAudioSource.minDistance = minDistance > 0f ? minDistance : DefaultMinDistance;
        speakerAudioSource.maxDistance = Mathf.Max(speakerAudioSource.minDistance + 0.1f, maxDistance);
        speakerAudioSource.volume = Mathf.Clamp01(outputVolume);

        if (speakerReverbFilter != null)
        {
            speakerReverbFilter.enabled = true;
            speakerReverbFilter.reverbPreset = voiceReverbPreset;
        }
    }

    private void OnVoiceSettingsChanged(VoiceChatSettings settings)
    {
        ConfigureAudioSource(settings.OutputVolume);
    }

    private Transform FindVoiceAnchor()
    {
        Transform found = FindChildByName(transform, "VoiceAnchor");
        if (found != null)
            return found;

        found = FindChildByName(transform, "mixamorig:Head");
        if (found != null)
            return found;

        return transform;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
