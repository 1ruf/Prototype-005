using Fusion;
using Photon.Realtime;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class VoiceChatDiagnostics : MonoBehaviour
{
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private FusionVoiceClient voiceClient;
    [SerializeField] private Recorder recorder;
    [SerializeField] private float checkInterval = 5f;
    [SerializeField] private bool logHealthyState;

    private float nextCheckTime;

    private void Awake()
    {
        AutoWire();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheckTime)
            return;

        nextCheckTime = Time.unscaledTime + Mathf.Max(1f, checkInterval);
        CheckVoiceState();
    }

    public void AutoWire()
    {
        if (runner == null)
            runner = GetComponent<NetworkRunner>();
        if (voiceClient == null)
            voiceClient = GetComponent<FusionVoiceClient>();
        if (recorder == null)
            recorder = GetComponentInChildren<Recorder>(true);
    }

    private void CheckVoiceState()
    {
        AutoWire();

        if (runner == null)
        {
            Debug.LogWarning("VoiceChatDiagnostics: NetworkRunner is missing.");
            return;
        }

        if (voiceClient == null)
        {
            Debug.LogWarning("VoiceChatDiagnostics: FusionVoiceClient is missing.");
            return;
        }

        if (recorder == null)
        {
            Debug.LogWarning("VoiceChatDiagnostics: Recorder is missing.");
            return;
        }

        Recorder[] recorders = GetComponentsInChildren<Recorder>(true);
        if (recorders.Length > 1)
            Debug.LogWarning($"VoiceChatDiagnostics: {recorders.Length} Recorder components found under NetworkGameManager. Only one should be active.");

        if (voiceClient.PrimaryRecorder != recorder)
            Debug.LogWarning("VoiceChatDiagnostics: FusionVoiceClient.PrimaryRecorder is not the managed Recorder.");

        if (!HasVoiceAppId())
            Debug.LogWarning("VoiceChatDiagnostics: Photon Voice AppId is empty. Set AppIdVoice in Photon/Fusion App Settings.");

        if (runner.IsRunning && runner.LocalPlayer != PlayerRef.None)
            CheckLocalPlayerVoiceObject();

        if (runner.IsRunning && voiceClient.ClientState != ClientState.Joined && voiceClient.ClientState != ClientState.Joining)
            Debug.LogWarning($"VoiceChatDiagnostics: Voice client is {voiceClient.ClientState} while Fusion is running.");
        else if (logHealthyState)
            Debug.Log($"VoiceChatDiagnostics: Voice state={voiceClient.ClientState}, recording={recorder.RecordingEnabled}, transmitting={recorder.TransmitEnabled}.");
    }

    private void CheckLocalPlayerVoiceObject()
    {
        NetworkObject playerObject = runner.GetPlayerObject(runner.LocalPlayer);
        if (playerObject == null)
        {
            Debug.LogWarning("VoiceChatDiagnostics: Local player object is not assigned yet.");
            return;
        }

        if (playerObject.GetComponent<VoiceNetworkObject>() == null)
            Debug.LogWarning("VoiceChatDiagnostics: Local player object is missing VoiceNetworkObject.");

        if (playerObject.GetComponentInChildren<Speaker>(true) == null)
            Debug.LogWarning("VoiceChatDiagnostics: Local player object is missing Speaker.");
    }

    private static bool HasVoiceAppId()
    {
#if FUSION2
        return !string.IsNullOrWhiteSpace(Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdVoice);
#else
        return !string.IsNullOrWhiteSpace(Fusion.Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdVoice);
#endif
    }
}
