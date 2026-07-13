using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using Photon.Voice.Unity.UtilityScripts;
using UnityEngine;

/// <summary>
/// Installs and configures the single process-wide Photon Voice runtime owned by the session host.
/// </summary>
public sealed class NetworkVoiceRuntimeInstaller
{
    private readonly GameObject host;

    private bool addedRecorder;
    private bool addedVoiceClient;
    private bool addedVoiceManager;
    private bool addedDiagnostics;
    private bool addedMicAmplifier;
    private bool addedMicrophonePermission;

    public NetworkVoiceRuntimeInstaller(GameObject host)
    {
        this.host = host;
    }

    public FusionVoiceClient VoiceClient { get; private set; }
    public Recorder Recorder { get; private set; }
    public INetworkRunnerCallbacks RunnerCallbacks => VoiceClient;

    public void Install()
    {
        if (host == null)
            return;

        Recorder = host.GetComponentInChildren<Recorder>(true);
        if (Recorder == null)
        {
            Recorder = host.AddComponent<Recorder>();
            addedRecorder = true;
        }

        VoiceClient = host.GetComponent<FusionVoiceClient>();
        if (VoiceClient == null)
        {
            VoiceClient = host.AddComponent<FusionVoiceClient>();
            addedVoiceClient = true;
        }

        VoiceChatManager voiceManager = host.GetComponent<VoiceChatManager>();
        if (voiceManager == null)
        {
            host.AddComponent<VoiceChatManager>();
            addedVoiceManager = true;
        }

        VoiceChatDiagnostics diagnostics = host.GetComponent<VoiceChatDiagnostics>();
        if (diagnostics == null)
        {
            host.AddComponent<VoiceChatDiagnostics>();
            addedDiagnostics = true;
        }

        if (Recorder.GetComponent<MicAmplifier>() == null)
        {
            Recorder.gameObject.AddComponent<MicAmplifier>();
            addedMicAmplifier = true;
        }

        if (Recorder.GetComponent<MicrophonePermission>() == null)
        {
            Recorder.gameObject.AddComponent<MicrophonePermission>();
            addedMicrophonePermission = true;
        }

        VoiceClient.PrimaryRecorder = Recorder;
        VoiceClient.UseFusionAppSettings = true;
        VoiceClient.UseFusionAuthValues = true;
    }

    public void Deactivate()
    {
        if (Recorder == null)
            return;

        Recorder.TransmitEnabled = false;
        Recorder.RecordingEnabled = false;
    }

    public void UninstallAddedComponents()
    {
        Deactivate();

        if (host == null)
            return;

        if (addedDiagnostics)
            DestroyComponent(host.GetComponent<VoiceChatDiagnostics>());
        if (addedVoiceManager)
            DestroyComponent(host.GetComponent<VoiceChatManager>());
        if (addedVoiceClient)
            DestroyComponent(VoiceClient);
        if (addedMicrophonePermission && Recorder != null)
            DestroyComponent(Recorder.GetComponent<MicrophonePermission>());
        if (addedMicAmplifier && Recorder != null)
            DestroyComponent(Recorder.GetComponent<MicAmplifier>());
        if (addedRecorder)
            DestroyComponent(Recorder);

        VoiceClient = null;
        Recorder = null;
    }

    private static void DestroyComponent(Component component)
    {
        if (component != null)
            Object.Destroy(component);
    }
}
