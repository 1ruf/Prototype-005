#if UNITY_EDITOR
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEditor;
using UnityEngine;

public static class VoiceSceneSetupBootstrap
{
    private const string PlayerPrefabPath = "Assets/4.Prefabs/Network/NetworkPlayer.prefab";

    [MenuItem("Tools/Prototype005/Validate/Voice Setup")]
    public static void ValidateVoiceSetup()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError($"Voice setup validation failed: prefab not found at {PlayerPrefabPath}.");
            return;
        }

        if (IsVoiceSetupMissing(playerPrefab))
            Debug.LogError("Voice setup validation failed. Run Tools/Prototype005/Setup Photon Fusion Scene explicitly to repair it.", playerPrefab);
        else
            Debug.Log("Voice setup validation passed.", playerPrefab);
    }

    private static bool IsVoiceSetupMissing(GameObject playerPrefab)
    {
        return playerPrefab.GetComponentInChildren<VoiceNetworkObject>(true) == null ||
               playerPrefab.GetComponentInChildren<NetworkPlayerVoiceComponent>(true) == null ||
               playerPrefab.GetComponentInChildren<Speaker>(true) == null;
    }
}
#endif
