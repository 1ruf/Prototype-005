#if UNITY_EDITOR
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class VoiceSceneSetupBootstrap
{
    private const string PlayerPrefabPath = "Assets/4.Prefabs/Network/NetworkPlayer.prefab";
    private const string SessionKey = "Prototype005.VoiceSceneSetupBootstrap.Ran";

    [DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        QueueSetupIfNeeded();
    }

    [InitializeOnLoadMethod]
    private static void QueueSetupIfNeeded()
    {
        if (Application.isPlaying || SessionState.GetBool(SessionKey, false))
            return;

        EditorApplication.delayCall += ApplyOnceIfVoiceSetupIsMissing;
    }

    private static void ApplyOnceIfVoiceSetupIsMissing()
    {
        if (Application.isPlaying || SessionState.GetBool(SessionKey, false))
            return;

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null || !IsVoiceSetupMissing(playerPrefab))
            return;

        SessionState.SetBool(SessionKey, true);
        Debug.Log("VoiceSceneSetupBootstrap: applying Photon Voice scene setup.");
        NetworkSceneSetupTool.SetupCSHScene();
    }

    private static bool IsVoiceSetupMissing(GameObject playerPrefab)
    {
        return playerPrefab.GetComponentInChildren<VoiceNetworkObject>(true) == null ||
               playerPrefab.GetComponentInChildren<NetworkPlayerVoiceComponent>(true) == null ||
               playerPrefab.GetComponentInChildren<Speaker>(true) == null;
    }
}
#endif
