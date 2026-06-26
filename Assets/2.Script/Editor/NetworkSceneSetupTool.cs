#if UNITY_EDITOR
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class NetworkSceneSetupTool
{
    private const string CSHScenePath = "Assets/1.Scenes/CSHObunga/CSHObunga.unity";
    private const string NetworkPrefabFolder = "Assets/4.Prefabs/Network";
    private const string PlayerPrefabPath = NetworkPrefabFolder + "/NetworkPlayer.prefab";
    private const string EnemyPrefabPath = NetworkPrefabFolder + "/NetworkCSHEnemy.prefab";
    private const string PlayerVisualControllerPath = "Assets/Resources/PlayerVisual.controller";
    private static bool autoSetupQueued;

    static NetworkSceneSetupTool()
    {
        EditorApplication.delayCall += AutoSetupProjectIfNeeded;
        EditorSceneManager.sceneOpened += (_, _) => QueueAutoSetup();
    }

    [DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        QueueAutoSetup();
    }

    private static void QueueAutoSetup()
    {
        if (autoSetupQueued)
            return;

        autoSetupQueued = true;
        EditorApplication.delayCall += AutoSetupProjectIfNeeded;
    }

    public static void QueueSetupForImport()
    {
        QueueAutoSetup();
    }

    private static void AutoSetupProjectIfNeeded()
    {
        autoSetupQueued = false;

        if (Application.isPlaying)
            return;

        if (!NeedsSetup())
            return;

        Debug.Log("NetworkSceneSetupTool: applying Photon Fusion scene setup.");

        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        Scene cshScene = FindOpenScene(CSHScenePath);
        bool openedTemporarily = !cshScene.IsValid();

        if (openedTemporarily)
            cshScene = EditorSceneManager.OpenScene(CSHScenePath, OpenSceneMode.Additive);

        EditorSceneManager.SetActiveScene(cshScene);
        SetupCurrentScene();
        EditorSceneManager.SaveScene(cshScene);

        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            EditorSceneManager.SetActiveScene(previousActiveScene);

        if (openedTemporarily)
            EditorSceneManager.CloseScene(cshScene, true);
    }

    private static bool NeedsSetup()
    {
        bool missingPrefabs = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath) == null ||
                              AssetDatabase.LoadAssetAtPath<NetworkObject>(EnemyPrefabPath) == null;

        if (missingPrefabs)
            return true;

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null ||
            MissingPlayerAnimationSetup(playerPrefab) ||
            !HasComponentByTypeName(playerPrefab, "CameraShakeController") ||
            playerPrefab.GetComponentInChildren<NetworkHealthComponent>(true) == null ||
            playerPrefab.GetComponentInChildren<NetworkDeathComponent>(true) == null ||
            playerPrefab.GetComponentInChildren<RagdollEntityComponent>(true) == null ||
            MissingRagdollPartSetup(playerPrefab))
            return true;

        Scene cshScene = FindOpenScene(CSHScenePath);
        if (!cshScene.IsValid())
            return true;

        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SetActiveScene(cshScene);
        bool missingManager = Object.FindFirstObjectByType<NetworkGameManager>() == null;

        if (previousActiveScene.IsValid())
            EditorSceneManager.SetActiveScene(previousActiveScene);

        return missingManager;
    }

    private static Scene FindOpenScene(string path)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.path == path)
                return scene;
        }

        return default;
    }

    [MenuItem("Tools/Prototype005/Setup CSH Photon Fusion Scene")]
    public static void SetupCSHScene()
    {
        EditorSceneManager.OpenScene(CSHScenePath);
        SetupCurrentScene();
        EditorSceneManager.SaveOpenScenes();
    }

    [MenuItem("Tools/Prototype005/Setup Photon Fusion Scene")]
    public static void SetupCurrentScene()
    {
        EnsureFolder("Assets/4.Prefabs", "Network");

        PlayerMovement scenePlayer = Object.FindFirstObjectByType<PlayerMovement>();
        CSHEnemy sceneEnemy = Object.FindFirstObjectByType<CSHEnemy>();

        if (scenePlayer == null)
        {
            Debug.LogError("Cannot find PlayerMovement in the open scene.");
            return;
        }

        if (sceneEnemy == null)
        {
            Debug.LogError("Cannot find CSHEnemy in the open scene.");
            return;
        }

        NetworkObject playerPrefab = SavePlayerPrefab(scenePlayer.gameObject);
        NetworkObject enemyPrefab = SaveEnemyPrefab(sceneEnemy.gameObject);
        NetworkGameManager manager = EnsureNetworkManager(playerPrefab, enemyPrefab);

        scenePlayer.gameObject.SetActive(false);
        sceneEnemy.gameObject.SetActive(false);

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();

        Debug.Log("Photon Fusion setup complete. Network prefabs were created and NetworkGameManager was configured.");
    }

    private static NetworkObject SavePlayerPrefab(GameObject source)
    {
        GameObject clone = Object.Instantiate(source);
        clone.name = "NetworkPlayer";
        clone.SetActive(true);
        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        EnsureComponent<NetworkObject>(clone);
        EnsureComponent<NetworkCharacterController>(clone);
        EnsurePlayerAnimationSetup(clone);
        EnsureCameraShakeSetup(clone);
        EnsureNetworkEntitySetup(clone);
        EnsureRagdollSetup(clone);

        NetworkObject prefab = SavePrefab(clone, PlayerPrefabPath);
        Object.DestroyImmediate(clone);
        return prefab;
    }

    private static NetworkObject SaveEnemyPrefab(GameObject source)
    {
        GameObject clone = Object.Instantiate(source);
        clone.name = "NetworkCSHEnemy";
        clone.SetActive(true);

        EnsureComponent<NetworkObject>(clone);
        CSHEnemy enemy = clone.GetComponent<CSHEnemy>();
        SerializedObject serializedEnemy = new SerializedObject(enemy);
        serializedEnemy.FindProperty("target").objectReferenceValue = null;
        serializedEnemy.FindProperty("ui").objectReferenceValue = null;
        serializedEnemy.ApplyModifiedPropertiesWithoutUndo();

        NetworkObject prefab = SavePrefab(clone, EnemyPrefabPath);
        Object.DestroyImmediate(clone);
        return prefab;
    }

    private static NetworkGameManager EnsureNetworkManager(NetworkObject playerPrefab, NetworkObject enemyPrefab)
    {
        NetworkGameManager manager = Object.FindFirstObjectByType<NetworkGameManager>();
        if (manager == null)
        {
            GameObject managerObject = new GameObject("NetworkGameManager");
            manager = managerObject.AddComponent<NetworkGameManager>();
        }

        EnsureComponent<NetworkRunner>(manager.gameObject);
        EnsureComponent<NetworkSceneManagerDefault>(manager.gameObject);
        EnsureComponent<NetworkObjectProviderDefault>(manager.gameObject);

        SerializedObject serializedManager = new SerializedObject(manager);
        serializedManager.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
        serializedManager.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
        serializedManager.FindProperty("sessionName").stringValue = "Prototype005";
        serializedManager.FindProperty("enemyCountPerPlayer").floatValue = 1.5f;
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        return manager;
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
            component = gameObject.AddComponent<T>();

        return component;
    }

    private static void EnsurePlayerAnimationSetup(GameObject player)
    {
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement == null)
            return;

        Transform visual = FindChildByName(player.transform, "Visual");
        Animator visualAnimator = visual != null ? visual.GetComponent<Animator>() : null;
        if (visualAnimator == null)
        {
            if (visual != null)
                visualAnimator = visual.gameObject.AddComponent<Animator>();
            else
                visualAnimator = player.GetComponentInChildren<Animator>(true);
        }

        if (visualAnimator != null)
        {
            visualAnimator.enabled = true;
            visualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            visualAnimator.updateMode = AnimatorUpdateMode.Normal;
            visualAnimator.applyRootMotion = false;
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerVisualControllerPath);
            if (controller != null)
                visualAnimator.runtimeAnimatorController = controller;
        }

        SerializedObject serializedMovement = new SerializedObject(movement);
        SerializedProperty visualAnimatorProperty = serializedMovement.FindProperty("_visualAnimator");
        if (visualAnimatorProperty != null)
            visualAnimatorProperty.objectReferenceValue = visualAnimator;

        SerializedProperty visualControllerProperty = serializedMovement.FindProperty("_visualAnimatorController");
        if (visualControllerProperty != null)
            visualControllerProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerVisualControllerPath);

        SerializedProperty crossFadeProperty = serializedMovement.FindProperty("_visualCrossFadeTime");
        if (crossFadeProperty != null && crossFadeProperty.floatValue <= 0f)
            crossFadeProperty.floatValue = 0.12f;

        serializedMovement.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureCameraShakeSetup(GameObject player)
    {
        Transform cameraRoot = FindChildByName(player.transform, "Camera");
        if (cameraRoot == null)
            return;

        Camera mainCamera = cameraRoot.GetComponentInChildren<Camera>(true);
        if (mainCamera == null)
        {
            GameObject mainCameraObject = new GameObject("Main Camera");
            mainCameraObject.transform.SetParent(cameraRoot, false);
            mainCameraObject.tag = "MainCamera";
            mainCamera = mainCameraObject.AddComponent<Camera>();
            mainCameraObject.AddComponent<AudioListener>();
        }

        if (mainCamera != null)
        {
            EnsureComponentByTypeName(mainCamera.gameObject, "Unity.Cinemachine.CinemachineBrain");

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null)
            {
                SerializedObject serializedMovement = new SerializedObject(movement);
                serializedMovement.FindProperty("_playerCamera").objectReferenceValue = mainCamera;
                serializedMovement.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        Component cinemachineCamera = EnsureComponentByTypeName(cameraRoot.gameObject, "Unity.Cinemachine.CinemachineCamera");
        Component noise = EnsureComponentByTypeName(cameraRoot.gameObject, "Unity.Cinemachine.CinemachineBasicMultiChannelPerlin");
        if (noise != null)
        {
            SerializedObject serializedNoise = new SerializedObject(noise);
            serializedNoise.FindProperty("NoiseProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Object>("Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset");
            serializedNoise.FindProperty("AmplitudeGain").floatValue = 0f;
            serializedNoise.FindProperty("FrequencyGain").floatValue = 0f;
            serializedNoise.FindProperty("PivotOffset").vector3Value = Vector3.zero;
            serializedNoise.ApplyModifiedPropertiesWithoutUndo();
        }

        Component shakeController = EnsureComponentByTypeName(cameraRoot.gameObject, "CameraShakeController");
        if (shakeController != null)
        {
            SerializedObject serializedShake = new SerializedObject(shakeController);
            serializedShake.FindProperty("noise").objectReferenceValue = noise;
            serializedShake.ApplyModifiedPropertiesWithoutUndo();
        }

        EnsureComponentByTypeName(cameraRoot.gameObject, "CameraBobbingController");

        if (cinemachineCamera != null && mainCamera != null)
        {
            SerializedObject serializedCamera = new SerializedObject(cinemachineCamera);
            SerializedProperty lens = serializedCamera.FindProperty("Lens");
            SerializedProperty fieldOfView = lens?.FindPropertyRelative("FieldOfView");
            if (fieldOfView != null)
                fieldOfView.floatValue = mainCamera.fieldOfView;
            serializedCamera.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void EnsureRagdollSetup(GameObject player)
    {
        RagdollEntityComponent ragdoll = EnsureComponent<RagdollEntityComponent>(player);

        Rigidbody[] rigidbodies = player.GetComponentsInChildren<Rigidbody>(true);
        var parts = new System.Collections.Generic.List<RagdollPartComponent>(rigidbodies.Length);

        foreach (Rigidbody body in rigidbodies)
        {
            if (body == null || body.transform == player.transform)
                continue;

            RagdollPartComponent part = EnsureComponent<RagdollPartComponent>(body.gameObject);
            parts.Add(part);
        }

        var disabledBehaviours = new System.Collections.Generic.List<UnityEngine.Behaviour>();
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null)
            disabledBehaviours.Add(movement);

        MouseLookSystem mouseLook = player.GetComponentInChildren<MouseLookSystem>(true);
        if (mouseLook != null)
            disabledBehaviours.Add(mouseLook);

        CameraBobbingController bobbing = player.GetComponentInChildren<CameraBobbingController>(true);
        if (bobbing != null)
            disabledBehaviours.Add(bobbing);

        SerializedObject serializedRagdoll = new SerializedObject(ragdoll);
        SetObjectArray(serializedRagdoll.FindProperty("parts"), parts);
        SetObjectArray(serializedRagdoll.FindProperty("animators"), player.GetComponentsInChildren<Animator>(true));
        serializedRagdoll.FindProperty("characterController").objectReferenceValue = player.GetComponent<CharacterController>();
        serializedRagdoll.FindProperty("networkController").objectReferenceValue = player.GetComponent<NetworkCharacterController>();
        SetObjectArray(serializedRagdoll.FindProperty("disableOnDeath"), disabledBehaviours);
        serializedRagdoll.ApplyModifiedPropertiesWithoutUndo();

        ragdoll.Initialize(player);
    }

    private static void EnsureNetworkEntitySetup(GameObject player)
    {
        NetworkHealthComponent health = EnsureComponent<NetworkHealthComponent>(player);
        RagdollEntityComponent ragdoll = EnsureComponent<RagdollEntityComponent>(player);
        NetworkDeathComponent death = EnsureComponent<NetworkDeathComponent>(player);
        PlayerDebugDeathInput debugDeathInput = EnsureComponent<PlayerDebugDeathInput>(player);

        SerializedObject serializedDeath = new SerializedObject(death);
        serializedDeath.FindProperty("health").objectReferenceValue = health;
        serializedDeath.FindProperty("ragdoll").objectReferenceValue = ragdoll;
        serializedDeath.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedDebugInput = new SerializedObject(debugDeathInput);
        serializedDebugInput.FindProperty("health").objectReferenceValue = health;
        serializedDebugInput.FindProperty("playerMovement").objectReferenceValue = player.GetComponent<PlayerMovement>();
        serializedDebugInput.FindProperty("killKey").intValue = (int)KeyCode.F;
        serializedDebugInput.ApplyModifiedPropertiesWithoutUndo();

        health.Initialize(player);
        death.Initialize(player);
    }

    private static void SetObjectArray<T>(SerializedProperty property, System.Collections.Generic.IList<T> objects)
        where T : Object
    {
        property.arraySize = objects.Count;
        for (int i = 0; i < objects.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
    }

    private static Component EnsureComponentByTypeName(GameObject gameObject, string typeName)
    {
        System.Type type = System.Type.GetType(typeName + ", Unity.Cinemachine") ??
                           System.Type.GetType(typeName + ", Assembly-CSharp");
        if (type == null || !typeof(Component).IsAssignableFrom(type))
            return null;

        Component component = gameObject.GetComponent(type);
        if (component == null)
            component = gameObject.AddComponent(type);

        return component;
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

    private static bool HasComponentByTypeName(GameObject gameObject, string typeName)
    {
        System.Type type = System.Type.GetType(typeName + ", Unity.Cinemachine") ??
                           System.Type.GetType(typeName + ", Assembly-CSharp");
        return type != null && gameObject.GetComponentInChildren(type, true) != null;
    }

    private static bool MissingRagdollPartSetup(GameObject playerPrefab)
    {
        Rigidbody[] rigidbodies = playerPrefab.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in rigidbodies)
        {
            if (body != null && body.transform != playerPrefab.transform && body.GetComponent<RagdollPartComponent>() == null)
                return true;
        }

        return false;
    }

    private static bool MissingPlayerAnimationSetup(GameObject playerPrefab)
    {
        PlayerMovement movement = playerPrefab.GetComponent<PlayerMovement>();
        if (movement == null)
            return true;

        SerializedObject serializedMovement = new SerializedObject(movement);
        SerializedProperty visualAnimatorProperty = serializedMovement.FindProperty("_visualAnimator");
        if (visualAnimatorProperty == null || visualAnimatorProperty.objectReferenceValue == null)
            return true;

        Animator visualAnimator = visualAnimatorProperty.objectReferenceValue as Animator;
        return visualAnimator == null ||
               !visualAnimator.enabled ||
               visualAnimator.runtimeAnimatorController == null;
    }

    private static NetworkObject SavePrefab(GameObject clone, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, path);
        if (prefab == null)
            throw new System.InvalidOperationException($"Failed to save prefab at {path}");

        return prefab.GetComponent<NetworkObject>();
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }
}
#endif
