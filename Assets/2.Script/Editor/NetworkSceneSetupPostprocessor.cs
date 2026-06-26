#if UNITY_EDITOR
using UnityEditor;

public class NetworkSceneSetupPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        foreach (string asset in importedAssets)
        {
            if (asset == "Assets/2.Script/Editor/NetworkSceneSetupTool.cs" ||
                asset == "Assets/2.Script/NetworkGameManager.cs" ||
                asset == "Assets/2.Script/Player/PlayerMovement.cs" ||
                asset == "Assets/2.Script/Test/CSHEnemy.cs")
            {
                NetworkSceneSetupTool.QueueSetupForImport();
                return;
            }
        }
    }
}
#endif
