using System;
using UnityEngine;

public static class VoiceChatSettingsService
{
    private const string Prefix = "Prototype005.Voice.";
    private const string VoiceEnabledKey = Prefix + "VoiceEnabled";
    private const string MicMutedKey = Prefix + "MicMuted";
    private const string InputDeviceIdKey = Prefix + "InputDeviceId";
    private const string InputVolumeKey = Prefix + "InputVolume";
    private const string OutputVolumeKey = Prefix + "OutputVolume";
    private const string ModeKey = Prefix + "Mode";
    private const string PushToTalkKeyKey = Prefix + "PushToTalkKey";

    private static VoiceChatSettings cachedSettings;

    public static event Action<VoiceChatSettings> SettingsChanged;

    public static VoiceChatSettings Current
    {
        get
        {
            if (cachedSettings == null)
                cachedSettings = Load();

            return cachedSettings;
        }
    }

    public static VoiceChatSettings Load()
    {
        VoiceChatSettings settings = new VoiceChatSettings
        {
            VoiceEnabled = PlayerPrefs.GetInt(VoiceEnabledKey, 1) != 0,
            MicMuted = PlayerPrefs.GetInt(MicMutedKey, 0) != 0,
            InputDeviceId = PlayerPrefs.GetString(InputDeviceIdKey, string.Empty),
            InputVolume = PlayerPrefs.GetFloat(InputVolumeKey, 1f),
            OutputVolume = PlayerPrefs.GetFloat(OutputVolumeKey, 1f),
            Mode = (VoiceChatMode)PlayerPrefs.GetInt(ModeKey, (int)VoiceChatMode.PushToTalk),
            PushToTalkKey = (KeyCode)PlayerPrefs.GetInt(PushToTalkKeyKey, (int)KeyCode.V)
        };

        cachedSettings = settings;
        return settings;
    }

    public static void Save(VoiceChatSettings settings)
    {
        if (settings == null)
            return;

        cachedSettings = settings.Clone();
        PlayerPrefs.SetInt(VoiceEnabledKey, cachedSettings.VoiceEnabled ? 1 : 0);
        PlayerPrefs.SetInt(MicMutedKey, cachedSettings.MicMuted ? 1 : 0);
        PlayerPrefs.SetString(InputDeviceIdKey, cachedSettings.InputDeviceId);
        PlayerPrefs.SetFloat(InputVolumeKey, cachedSettings.InputVolume);
        PlayerPrefs.SetFloat(OutputVolumeKey, cachedSettings.OutputVolume);
        PlayerPrefs.SetInt(ModeKey, (int)cachedSettings.Mode);
        PlayerPrefs.SetInt(PushToTalkKeyKey, (int)cachedSettings.PushToTalkKey);
        PlayerPrefs.Save();

        SettingsChanged?.Invoke(cachedSettings.Clone());
    }

    public static void Apply(Action<VoiceChatSettings> change)
    {
        if (change == null)
            return;

        VoiceChatSettings settings = Current.Clone();
        change(settings);
        Save(settings);
    }
}
