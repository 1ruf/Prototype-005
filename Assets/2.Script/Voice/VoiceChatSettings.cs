using System;
using UnityEngine;

[Serializable]
public sealed class VoiceChatSettings
{
    [SerializeField] private bool voiceEnabled = true;
    [SerializeField] private bool micMuted;
    [SerializeField] private string inputDeviceId = string.Empty;
    [SerializeField] private float inputVolume = 1f;
    [SerializeField] private float outputVolume = 1f;
    [SerializeField] private VoiceChatMode mode = VoiceChatMode.PushToTalk;
    [SerializeField] private KeyCode pushToTalkKey = KeyCode.V;

    public bool VoiceEnabled
    {
        get => voiceEnabled;
        set => voiceEnabled = value;
    }

    public bool MicMuted
    {
        get => micMuted;
        set => micMuted = value;
    }

    public string InputDeviceId
    {
        get => inputDeviceId;
        set => inputDeviceId = value ?? string.Empty;
    }

    public float InputVolume
    {
        get => inputVolume;
        set => inputVolume = Mathf.Clamp(value, 0f, 2f);
    }

    public float OutputVolume
    {
        get => outputVolume;
        set => outputVolume = Mathf.Clamp01(value);
    }

    public VoiceChatMode Mode
    {
        get => mode;
        set => mode = value;
    }

    public KeyCode PushToTalkKey
    {
        get => pushToTalkKey;
        set => pushToTalkKey = value;
    }

    public VoiceChatSettings Clone()
    {
        return new VoiceChatSettings
        {
            voiceEnabled = voiceEnabled,
            micMuted = micMuted,
            inputDeviceId = inputDeviceId,
            inputVolume = inputVolume,
            outputVolume = outputVolume,
            mode = mode,
            pushToTalkKey = pushToTalkKey
        };
    }
}
