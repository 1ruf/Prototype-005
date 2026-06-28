using Fusion;
using Photon.Voice;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using Photon.Voice.Unity.UtilityScripts;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkRunner))]
public sealed class VoiceChatManager : MonoBehaviour
{
    [SerializeField] private FusionVoiceClient voiceClient;
    [SerializeField] private Recorder recorder;
    [SerializeField] private MicAmplifier micAmplifier;
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private bool applySettingsOnStart = true;
    [SerializeField] private bool logVoiceStateChanges = true;

    private VoiceChatSettings settings;
    private bool lastTransmitEnabled;
    private bool lastRecordingEnabled = true;

    public FusionVoiceClient VoiceClient => voiceClient;
    public Recorder Recorder => recorder;
    public VoiceChatSettings Settings => settings ?? VoiceChatSettingsService.Current;

    private void Awake()
    {
        EnsureComponents();
        ConfigureVoiceClient();
        ConfigureRecorder();
    }

    private void OnEnable()
    {
        VoiceChatSettingsService.SettingsChanged += OnSettingsChanged;
        SubscribeVoiceClientEvents();
    }

    private void Start()
    {
        if (applySettingsOnStart)
            ApplySettings(VoiceChatSettingsService.Current);
    }

    private void Update()
    {
        if (recorder == null)
            return;

        bool shouldRecord = CanLocalPlayerUseVoice();
        if (lastRecordingEnabled != shouldRecord || recorder.RecordingEnabled != shouldRecord)
        {
            recorder.RecordingEnabled = shouldRecord;
            lastRecordingEnabled = shouldRecord;
        }

        bool shouldTransmit = ShouldTransmit();
        if (lastTransmitEnabled == shouldTransmit && recorder.TransmitEnabled == shouldTransmit)
            return;

        recorder.TransmitEnabled = shouldTransmit;
        lastTransmitEnabled = shouldTransmit;

        if (logVoiceStateChanges)
            Debug.Log($"VoiceChatManager: TransmitEnabled={shouldTransmit}, key={Settings.PushToTalkKey}, mode={Settings.Mode}, muted={Settings.MicMuted}.");
    }

    private void OnDisable()
    {
        VoiceChatSettingsService.SettingsChanged -= OnSettingsChanged;
        UnsubscribeVoiceClientEvents();
        if (recorder != null)
            recorder.TransmitEnabled = false;
    }

    public void ApplySettings(VoiceChatSettings newSettings)
    {
        settings = (newSettings ?? VoiceChatSettingsService.Current).Clone();
        ConfigureRecorder();
        ApplyInputDevice();
        ApplyInputVolume();
        NetworkPlayerVoiceComponent.ApplyOutputVolumeToAll(settings.OutputVolume);
        lastTransmitEnabled = !ShouldTransmit();
    }

    private void OnSettingsChanged(VoiceChatSettings newSettings)
    {
        ApplySettings(newSettings);
    }

    private void EnsureComponents()
    {
        if (voiceClient == null)
            voiceClient = GetComponent<FusionVoiceClient>();
        if (voiceClient == null)
            voiceClient = gameObject.AddComponent<FusionVoiceClient>();

        if (runner == null)
            runner = GetComponent<NetworkRunner>();

        if (recorder == null)
            recorder = GetComponentInChildren<Recorder>(true);
        if (recorder == null)
            recorder = gameObject.AddComponent<Recorder>();

        if (micAmplifier == null)
            micAmplifier = recorder.GetComponent<MicAmplifier>();
        if (micAmplifier == null)
            micAmplifier = recorder.gameObject.AddComponent<MicAmplifier>();

        if (recorder.GetComponent<MicrophonePermission>() == null)
            recorder.gameObject.AddComponent<MicrophonePermission>();
    }

    private void ConfigureVoiceClient()
    {
        if (voiceClient == null || recorder == null)
            return;

        voiceClient.PrimaryRecorder = recorder;
        voiceClient.UseFusionAppSettings = true;
        voiceClient.UseFusionAuthValues = true;
        SubscribeVoiceClientEvents();
    }

    private void ConfigureRecorder()
    {
        if (recorder == null)
            return;

        recorder.SourceType = Recorder.InputSourceType.Microphone;
        recorder.MicrophoneType = Recorder.MicType.Unity;
        recorder.RecordWhenJoined = true;
        recorder.RecordingEnabled = true;
        recorder.VoiceDetection = false;
        recorder.TransmitEnabled = false;
        recorder.InterestGroup = 0;
    }

    private void ApplyInputDevice()
    {
        if (recorder == null || settings == null)
            return;

        recorder.MicrophoneDevice = string.IsNullOrWhiteSpace(settings.InputDeviceId)
            ? DeviceInfo.Default
            : new DeviceInfo(settings.InputDeviceId);
    }

    private void ApplyInputVolume()
    {
        if (micAmplifier != null && settings != null)
            micAmplifier.AmplificationFactor = settings.InputVolume;
    }

    private bool ShouldTransmit()
    {
        if (!CanLocalPlayerUseVoice())
            return false;

        VoiceChatSettings activeSettings = Settings;
        if (!activeSettings.VoiceEnabled || activeSettings.MicMuted)
            return false;

        if (activeSettings.Mode == VoiceChatMode.OpenMic)
            return true;

        return Input.GetKey(activeSettings.PushToTalkKey);
    }

    private bool CanLocalPlayerUseVoice()
    {
        PlayerMovement localPlayer = Manager.Instance != null && Manager.Instance.PlayerManager != null
            ? Manager.Instance.PlayerManager.LocalPlayer
            : null;

        if (localPlayer == null && runner != null && runner.IsRunning && runner.LocalPlayer != PlayerRef.None)
        {
            NetworkObject localPlayerObject = runner.GetPlayerObject(runner.LocalPlayer);
            if (localPlayerObject != null)
                localPlayer = localPlayerObject.GetComponent<PlayerMovement>() ?? localPlayerObject.GetComponentInChildren<PlayerMovement>(true);
        }

        if (localPlayer == null)
            return true;

        NetworkHealthComponent health = localPlayer.GetComponent<NetworkHealthComponent>()
            ?? localPlayer.GetComponentInChildren<NetworkHealthComponent>(true);
        if (health != null && (health.IsDead || health.CurrentHealth <= 0f))
            return false;

        RagdollEntityComponent ragdoll = localPlayer.GetComponent<RagdollEntityComponent>()
            ?? localPlayer.GetComponentInChildren<RagdollEntityComponent>(true);
        return ragdoll == null || (!ragdoll.IsDead && !ragdoll.IsRagdollEnabled);
    }

    private void SubscribeVoiceClientEvents()
    {
        if (voiceClient == null)
            return;

        voiceClient.RemoteVoiceAdded -= OnRemoteVoiceAdded;
        voiceClient.SpeakerLinked -= OnSpeakerLinked;
        voiceClient.RemoteVoiceAdded += OnRemoteVoiceAdded;
        voiceClient.SpeakerLinked += OnSpeakerLinked;
    }

    private void UnsubscribeVoiceClientEvents()
    {
        if (voiceClient == null)
            return;

        voiceClient.RemoteVoiceAdded -= OnRemoteVoiceAdded;
        voiceClient.SpeakerLinked -= OnSpeakerLinked;
    }

    private void OnRemoteVoiceAdded(RemoteVoiceLink remoteVoice)
    {
        if (!logVoiceStateChanges)
            return;

        Debug.Log($"VoiceChatManager: Remote voice added. player={remoteVoice.PlayerId}, voice={remoteVoice.VoiceId}, userData={remoteVoice.VoiceInfo.UserData}.");
    }

    private void OnSpeakerLinked(Speaker linkedSpeaker)
    {
        if (!logVoiceStateChanges)
            return;

        Debug.Log($"VoiceChatManager: Speaker linked. speaker={linkedSpeaker.name}.");
    }
}
