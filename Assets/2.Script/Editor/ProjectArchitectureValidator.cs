#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Read-only project architecture checks for editor use and CI. This class intentionally never
/// applies serialized properties, marks objects dirty, saves assets, or invokes migration code.
/// </summary>
public static class ProjectArchitectureValidator
{
    private const string LocalPlayerPrefabPath = "Assets/4.Prefabs/Player.prefab";
    private const string NetworkPlayerPrefabPath = "Assets/4.Prefabs/Network/NetworkPlayer.prefab";
    private const string NetworkEnemyPrefabPath = "Assets/4.Prefabs/Network/NetworkCSHEnemy.prefab";
    private const string GameplayScenePath = "Assets/1.Scenes/CSHObunga/CSHObunga.unity";
    private const string SceneSearchRoot = "Assets/1.Scenes";
    private const string PrefabSearchRoot = "Assets/4.Prefabs";
    private const int HidingSpotStableIdSeed = 486187739;

    [MenuItem("Tools/Prototype005/Architecture/Validate Project (Read Only)")]
    public static void ValidateFromMenu()
    {
        ValidationReport report = ValidateProject();
        report.Log();
    }

    /// <summary>
    /// Batchmode entry point:
    /// -executeMethod ProjectArchitectureValidator.ValidateFromCommandLine
    /// </summary>
    public static void ValidateFromCommandLine()
    {
        ValidationReport report = ValidateProject();
        report.Log();

        if (report.ErrorCount > 0)
        {
            throw new InvalidOperationException(
                $"Project architecture validation failed with {report.ErrorCount} error(s) " +
                $"and {report.WarningCount} warning(s). See the preceding ARCH-* log entries.");
        }
    }

    public static void RunFromCommandLine()
    {
        ValidateFromCommandLine();
    }

    private static ValidationReport ValidateProject()
    {
        ValidationReport report = new ValidationReport();
        ValidateLocalPlayerPrefab(report);
        ValidateNetworkPlayerPrefab(report);
        ValidateNetworkEnemyPrefab(report);
        ValidateFirstPartyPrefabs(report);
        ValidateFirstPartyScenes(report);
        return report;
    }

    private static void ValidateLocalPlayerPrefab(ValidationReport report)
    {
        InspectPrefab(LocalPlayerPrefabPath, report, root =>
        {
            ValidatePrefabRootName(root, "Player", LocalPlayerPrefabPath, report);
            ValidateLocalPlayerNetworkBoundary(root, LocalPlayerPrefabPath, report);
            ValidateCommonPlayerHierarchy(root, LocalPlayerPrefabPath, report, false);
            ValidateNetworkEntityRoot(root, LocalPlayerPrefabPath, report);
        });
    }

    private static void ValidateNetworkPlayerPrefab(ValidationReport report)
    {
        InspectPrefab(NetworkPlayerPrefabPath, report, root =>
        {
            ValidatePrefabRootName(root, "NetworkPlayer", NetworkPlayerPrefabPath, report);
            ValidateSingleRootNetworkObject(root, NetworkPlayerPrefabPath, report);
            ValidateCommonPlayerHierarchy(root, NetworkPlayerPrefabPath, report, true);
            ValidateNetworkPlayerComponents(root, NetworkPlayerPrefabPath, report);
            ValidateNetworkEntityRoot(root, NetworkPlayerPrefabPath, report);
        });
    }

    private static void ValidateNetworkEnemyPrefab(ValidationReport report)
    {
        InspectPrefab(NetworkEnemyPrefabPath, report, root =>
        {
            ValidatePrefabRootName(root, "NetworkCSHEnemy", NetworkEnemyPrefabPath, report);
            ValidateSingleRootNetworkObject(root, NetworkEnemyPrefabPath, report);
            ValidateNetworkEntityRoot(root, NetworkEnemyPrefabPath, report);

            RequireComponentOn(root, root.transform, typeof(CSHEnemy), NetworkEnemyPrefabPath, report);
            RequireComponentOn(root, root.transform, typeof(Rigidbody), NetworkEnemyPrefabPath, report);
            RequireComponentOn(root, root.transform, typeof(NavMeshAgent), NetworkEnemyPrefabPath, report);
            RequireComponentOn(root, root.transform, typeof(Collider), NetworkEnemyPrefabPath, report);

            Transform perception = RequirePath(root, "Services/Perception", NetworkEnemyPrefabPath, report);
            Transform navigation = RequirePath(root, "Services/Navigation", NetworkEnemyPrefabPath, report);
            Transform combat = RequirePath(root, "Services/Combat", NetworkEnemyPrefabPath, report);
            Transform presentation = RequirePath(root, "Presentation", NetworkEnemyPrefabPath, report);
            Transform rig = RequirePath(root, "Rig", NetworkEnemyPrefabPath, report);

            RequireComponentOn(root, perception, typeof(EnemyPerceptionComponent), NetworkEnemyPrefabPath, report);
            RequireComponentOn(root, navigation, typeof(EnemyNavigationComponent), NetworkEnemyPrefabPath, report);
            RequireComponentOn(root, combat, typeof(EnemyCombatComponent), NetworkEnemyPrefabPath, report);
            RequireComponentOn(root, presentation, typeof(EnemyAnimationDriver), NetworkEnemyPrefabPath, report);

            RequirePath(root, "Presentation/HeadmanSound", NetworkEnemyPrefabPath, report);
            RequirePath(root, "Rig/HeadmanVisual", NetworkEnemyPrefabPath, report);

            CSHEnemy coordinator = root.GetComponent<CSHEnemy>();
            if (coordinator != null)
            {
                ValidateSerializedReference(
                    coordinator,
                    "perceptionComponent",
                    perception != null ? perception.GetComponent<EnemyPerceptionComponent>() : null,
                    NetworkEnemyPrefabPath,
                    report);
                ValidateSerializedReference(
                    coordinator,
                    "navigationComponent",
                    navigation != null ? navigation.GetComponent<EnemyNavigationComponent>() : null,
                    NetworkEnemyPrefabPath,
                    report);
                ValidateSerializedReference(
                    coordinator,
                    "combatComponent",
                    combat != null ? combat.GetComponent<EnemyCombatComponent>() : null,
                    NetworkEnemyPrefabPath,
                    report);
            }

            if (rig != null && presentation != null && rig.IsChildOf(presentation))
            {
                report.Error(
                    "ARCH-ENEMY-RIG-NESTING",
                    NetworkEnemyPrefabPath,
                    GetHierarchyPath(rig),
                    "Rig must be a root architecture branch, not a child of Presentation.");
            }
        });
    }

    private static void ValidateCommonPlayerHierarchy(
        GameObject root,
        string assetPath,
        ValidationReport report,
        bool networked)
    {
        RequireComponentOn(root, root.transform, typeof(PlayerMovement), assetPath, report);

        Transform simulation = RequirePath(root, "Simulation", assetPath, report);
        Transform locomotion = RequirePath(root, "Simulation/Locomotion", assetPath, report);
        RequirePath(root, "Simulation/Inventory", assetPath, report);
        RequirePath(root, "Simulation/Hiding", assetPath, report);
        RequirePath(root, "Simulation/Emotes", assetPath, report);
        Transform entityComponents = RequirePath(root, "Simulation/PlayerEntityComponents", assetPath, report);
        Transform presentation = RequirePath(root, "Presentation", assetPath, report);
        Transform presentationComponents = RequirePath(root, "Presentation/PlayerPresentationComponents", assetPath, report);
        Transform sensors = RequirePath(root, "Sensors", assetPath, report);
        Transform rig = RequirePath(root, "Rig", assetPath, report);

        RequirePath(root, "Sensors/GroundChecker", assetPath, report);
        RequirePath(root, "Sensors/ItemChecker", assetPath, report);
        RequirePath(root, "Rig/Visual", assetPath, report);
        RequireComponentOn(root, locomotion, typeof(PlayerStamina), assetPath, report);
        RequireComponentOn(root, presentationComponents, typeof(PlayerCameraPresentation), assetPath, report);
        RequireComponentOn(root, presentationComponents, typeof(PlayerAnimationPresentation), assetPath, report);
        RequireComponentOn(root, presentationComponents, typeof(PlayerNetworkPowerBridge), assetPath, report);

        if (networked)
        {
            RequirePath(root, "Presentation/CameraComponent", assetPath, report);
            Transform deadCamera = RequirePath(root, "Presentation/DeadCamera", assetPath, report);
            RequireComponentOn(root, deadCamera, typeof(DeadCameraController), assetPath, report);
            RequirePath(root, "Rig/HeldItemPoseTargets", assetPath, report);
        }
        else
        {
            RequirePath(root, "Presentation/Camera", assetPath, report);
        }

        ValidateBranchParent(simulation, root.transform, assetPath, report);
        ValidateBranchParent(presentation, root.transform, assetPath, report);
        ValidateBranchParent(sensors, root.transform, assetPath, report);
        ValidateBranchParent(rig, root.transform, assetPath, report);

        if (entityComponents != null && simulation != null && entityComponents.parent != simulation)
        {
            report.Error(
                "ARCH-PLAYER-ENTITY-BRANCH",
                assetPath,
                GetHierarchyPath(entityComponents),
                "PlayerEntityComponents must be a direct child of Simulation.");
        }
    }

    private static void ValidateNetworkPlayerComponents(
        GameObject root,
        string assetPath,
        ValidationReport report)
    {
        RequireComponentOn(root, root.transform, typeof(NetworkCharacterController), assetPath, report);
        RequireComponentInSubtree<NetworkPlayerVoiceComponent>(root, assetPath, report);

        Transform inventory = root.transform.Find("Simulation/Inventory");
        Transform hiding = root.transform.Find("Simulation/Hiding");
        Transform emotes = root.transform.Find("Simulation/Emotes");
        Transform entityComponents = root.transform.Find("Simulation/PlayerEntityComponents");
        Transform presentationComponents = root.transform.Find("Presentation/PlayerPresentationComponents");

        RequireComponentOn(root, inventory, typeof(NetworkInventory), assetPath, report);
        RequireComponentOn(root, inventory, typeof(PlayerInventoryInput), assetPath, report);
        RequireComponentOn(root, hiding, typeof(NetworkPlayerHidingComponent), assetPath, report);
        RequireComponentOn(root, emotes, typeof(NetworkEmoteAudioPlayer), assetPath, report);

        RequireComponentOn(root, entityComponents, typeof(NetworkHealthComponent), assetPath, report);
        RequireComponentOn(root, entityComponents, typeof(NetworkDeathComponent), assetPath, report);
        RequireComponentOn(root, entityComponents, typeof(RagdollEntityComponent), assetPath, report);

        RequireComponentOn(root, presentationComponents, typeof(NetworkPlayerItemHolder), assetPath, report);
        RequireComponentOn(root, presentationComponents, typeof(NetworkPlayerVisualPose), assetPath, report);
        RequireComponentOn(root, presentationComponents, typeof(InventoryHeldItemPresentation), assetPath, report);
        RequireComponentOn(root, presentationComponents, typeof(PlayerHidingPresentation), assetPath, report);

        if (entityComponents != null && entityComponents.GetComponent<BloodSplatterComponent>() == null)
        {
            report.Warning(
                "ARCH-PLAYER-BLOOD-OPTIONAL",
                assetPath,
                GetHierarchyPath(entityComponents),
                "BloodSplatterComponent is not present; death works, but blood presentation is unavailable.");
        }

        if (entityComponents != null && entityComponents.GetComponent<RagdollEntityComponent>() != null)
        {
            Transform rigPresentation = RequirePath(
                root,
                "Simulation/PlayerEntityComponents/RagdollServices/RigPresentation",
                assetPath,
                report);
            Transform surfaceBlood = RequirePath(
                root,
                "Simulation/PlayerEntityComponents/RagdollServices/SurfaceBlood",
                assetPath,
                report);
            RequireComponentOn(root, rigPresentation, typeof(RagdollRigPresentation), assetPath, report);
            RequireComponentOn(root, surfaceBlood, typeof(RagdollSurfaceBloodController), assetPath, report);
        }
    }

    private static void ValidateLocalPlayerNetworkBoundary(
        GameObject root,
        string assetPath,
        ValidationReport report)
    {
        NetworkObject[] networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
        if (networkObjects.Length != 0)
        {
            report.Error(
                "ARCH-LOCAL-PLAYER-NETWORK-OBJECT",
                assetPath,
                root.name,
                $"Local Player prefab must not contain NetworkObject components; found {networkObjects.Length}.");
        }
    }

    private static void ValidateSingleRootNetworkObject(
        GameObject root,
        string assetPath,
        ValidationReport report)
    {
        NetworkObject[] networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
        NetworkObject rootNetworkObject = root.GetComponent<NetworkObject>();

        if (rootNetworkObject == null)
        {
            report.Error(
                "ARCH-NETWORK-ROOT-MISSING",
                assetPath,
                root.name,
                "A NetworkObject is required on the prefab root.");
        }

        if (networkObjects.Length != 1)
        {
            report.Error(
                "ARCH-NETWORK-OBJECT-COUNT",
                assetPath,
                root.name,
                $"Network prefab must contain exactly one NetworkObject on its root; found {networkObjects.Length}.");
        }

        foreach (NetworkObject networkObject in networkObjects)
        {
            if (networkObject != null && networkObject.gameObject != root)
            {
                report.Error(
                    "ARCH-NESTED-NETWORK-OBJECT",
                    assetPath,
                    GetHierarchyPath(networkObject.transform),
                    "Nested NetworkObject is forbidden because it creates a second Fusion object boundary.");
            }
        }

        foreach (NetworkBehaviour behaviour in root.GetComponentsInChildren<NetworkBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            NetworkObject nearestNetworkObject = behaviour.GetComponentInParent<NetworkObject>(true);
            if (rootNetworkObject == null || nearestNetworkObject != rootNetworkObject)
            {
                report.Error(
                    "ARCH-NETWORK-BEHAVIOUR-BOUNDARY",
                    assetPath,
                    GetHierarchyPath(behaviour.transform),
                    $"{behaviour.GetType().Name} is not owned by the root NetworkObject subtree.");
            }
        }

        if (rootNetworkObject != null)
            ValidateRegisteredNetworkBehaviours(root, rootNetworkObject, assetPath, report);
    }

    private static void ValidateRegisteredNetworkBehaviours(
        GameObject root,
        NetworkObject rootNetworkObject,
        string assetPath,
        ValidationReport report)
    {
        SerializedObject serializedNetworkObject = new SerializedObject(rootNetworkObject);
        SerializedProperty registeredProperty = serializedNetworkObject.FindProperty("NetworkedBehaviours");
        if (registeredProperty == null || !registeredProperty.isArray)
        {
            report.Warning(
                "ARCH-FUSION-REGISTRY-UNAVAILABLE",
                assetPath,
                root.name,
                "Fusion NetworkedBehaviours serialized table could not be inspected.");
            return;
        }

        HashSet<NetworkBehaviour> registered = new HashSet<NetworkBehaviour>();
        for (int i = 0; i < registeredProperty.arraySize; i++)
        {
            Object reference = registeredProperty.GetArrayElementAtIndex(i).objectReferenceValue;
            if (reference == null)
            {
                report.Warning(
                    "ARCH-FUSION-REGISTRY-NULL",
                    assetPath,
                    root.name,
                    $"Fusion NetworkedBehaviours contains a null entry at index {i}.");
                continue;
            }

            if (reference is not NetworkBehaviour registeredBehaviour)
                continue;

            if (!registered.Add(registeredBehaviour))
            {
                report.Warning(
                    "ARCH-FUSION-REGISTRY-DUPLICATE",
                    assetPath,
                    GetHierarchyPath(registeredBehaviour.transform),
                    $"{registeredBehaviour.GetType().Name} appears more than once in Fusion's registry.");
            }

            if (registeredBehaviour.transform != root.transform
                && !registeredBehaviour.transform.IsChildOf(root.transform))
            {
                report.Error(
                    "ARCH-FUSION-REGISTRY-EXTERNAL",
                    assetPath,
                    root.name,
                    $"Fusion registry references {registeredBehaviour.name} outside the prefab root.");
            }
        }

        foreach (NetworkBehaviour behaviour in root.GetComponentsInChildren<NetworkBehaviour>(true))
        {
            if (behaviour != null && !registered.Contains(behaviour))
            {
                report.Warning(
                    "ARCH-FUSION-REGISTRY-MISSING",
                    assetPath,
                    GetHierarchyPath(behaviour.transform),
                    $"{behaviour.GetType().Name} is not present in the serialized Fusion behaviour table; reimport/codegen should be verified.");
            }
        }
    }

    private static void ValidateNetworkEntityRoot(
        GameObject root,
        string assetPath,
        ValidationReport report)
    {
        NetworkEntityRoot[] entityRoots = root.GetComponentsInChildren<NetworkEntityRoot>(true);
        if (entityRoots.Length != 1 || entityRoots[0].gameObject != root)
        {
            report.Error(
                "ARCH-ENTITY-ROOT-COUNT",
                assetPath,
                root.name,
                $"Exactly one NetworkEntityRoot must exist on the prefab root; found {entityRoots.Length}.");
            return;
        }

        NetworkEntityRoot entityRoot = entityRoots[0];
        if (!entityRoot.enabled)
        {
            report.Error(
                "ARCH-ENTITY-ROOT-DISABLED",
                assetPath,
                root.name,
                "NetworkEntityRoot is disabled and cannot initialize entity components.");
        }

        SerializedObject serializedRoot = new SerializedObject(entityRoot);
        SerializedProperty ownerOverride = serializedRoot.FindProperty("ownerOverride");
        SerializedProperty initializeOnAwake = serializedRoot.FindProperty("initializeOnAwake");
        SerializedProperty autoCollect = serializedRoot.FindProperty("autoCollectComponents");
        SerializedProperty componentBehaviours = serializedRoot.FindProperty("componentBehaviours");

        if (ownerOverride == null || initializeOnAwake == null || autoCollect == null || componentBehaviours == null)
        {
            report.Error(
                "ARCH-ENTITY-ROOT-SCHEMA",
                assetPath,
                root.name,
                "NetworkEntityRoot serialized initialization fields could not be inspected.");
            return;
        }

        Object owner = ownerOverride.objectReferenceValue;
        if (owner != null && owner != root)
        {
            report.Error(
                "ARCH-ENTITY-OWNER",
                assetPath,
                root.name,
                "NetworkEntityRoot.ownerOverride must be null (implicit root) or reference the prefab root GameObject.");
        }
        else if (owner == null)
        {
            report.Warning(
                "ARCH-ENTITY-OWNER-IMPLICIT",
                assetPath,
                root.name,
                "NetworkEntityRoot uses its valid implicit root owner; explicit root wiring is preferred for auditability.");
        }

        if (!initializeOnAwake.boolValue)
        {
            report.Warning(
                "ARCH-ENTITY-DEFERRED-INIT",
                assetPath,
                root.name,
                "initializeOnAwake is disabled; initialization depends on Start or an explicit call.");
        }

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        List<MonoBehaviour> entityComponents = behaviours
            .Where(behaviour => behaviour != null && behaviour is IEntityComponent)
            .ToList();

        if (entityComponents.Count == 0)
        {
            report.Error(
                "ARCH-ENTITY-COMPONENTS-EMPTY",
                assetPath,
                root.name,
                "No IEntityComponent implementations are available for NetworkEntityRoot to initialize.");
        }

        if (autoCollect.boolValue)
            return;

        if (!componentBehaviours.isArray || componentBehaviours.arraySize == 0)
        {
            report.Error(
                "ARCH-ENTITY-MANUAL-LIST-EMPTY",
                assetPath,
                root.name,
                "autoCollectComponents is disabled, but componentBehaviours is empty.");
            return;
        }

        HashSet<MonoBehaviour> configured = new HashSet<MonoBehaviour>();
        for (int i = 0; i < componentBehaviours.arraySize; i++)
        {
            Object entry = componentBehaviours.GetArrayElementAtIndex(i).objectReferenceValue;
            if (entry is not MonoBehaviour behaviour)
            {
                report.Error(
                    "ARCH-ENTITY-MANUAL-LIST-INVALID",
                    assetPath,
                    root.name,
                    $"componentBehaviours[{i}] is null or is not a MonoBehaviour.");
                continue;
            }

            configured.Add(behaviour);
            if (behaviour is not IEntityComponent)
            {
                report.Error(
                    "ARCH-ENTITY-MANUAL-LIST-TYPE",
                    assetPath,
                    GetHierarchyPath(behaviour.transform),
                    $"{behaviour.GetType().Name} does not implement IEntityComponent.");
            }

            if (behaviour.transform != root.transform && !behaviour.transform.IsChildOf(root.transform))
            {
                report.Error(
                    "ARCH-ENTITY-MANUAL-LIST-EXTERNAL",
                    assetPath,
                    root.name,
                    $"componentBehaviours[{i}] references an object outside the entity root.");
            }
        }

        foreach (MonoBehaviour entityComponent in entityComponents)
        {
            if (!configured.Contains(entityComponent))
            {
                report.Error(
                    "ARCH-ENTITY-MANUAL-LIST-MISSING",
                    assetPath,
                    GetHierarchyPath(entityComponent.transform),
                    $"{entityComponent.GetType().Name} cannot be initialized because it is absent from componentBehaviours.");
            }
        }
    }

    private static void ValidateFirstPartyPrefabs(ValidationReport report)
    {
        foreach (string prefabPath in FindAssetPaths("t:Prefab", PrefabSearchRoot)
                     .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
        {
            InspectPrefab(prefabPath, report, root =>
            {
                ValidateMissingMonoScripts(root, prefabPath, report);
                ValidatePrefabHidingSpotIds(root, prefabPath, report);
            });
        }
    }

    private static void ValidateFirstPartyScenes(ValidationReport report)
    {
        List<HidingSpotRecord> allHidingSpots = new List<HidingSpotRecord>();
        bool gameplaySceneFound = false;

        foreach (string scenePath in FindAssetPaths("t:Scene", SceneSearchRoot))
        {
            InspectScene(scenePath, report, scene =>
            {
                ValidateMissingMonoScripts(scene, scenePath, report);
                List<HidingSpotRecord> sceneSpots = CollectHidingSpotRecords(scene, scenePath, report);
                allHidingSpots.AddRange(sceneSpots);
                ValidateSceneHidingSpotIds(sceneSpots, scenePath, report);

                if (string.Equals(scenePath, GameplayScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    gameplaySceneFound = true;
                    ValidateNetworkGameManager(scene, scenePath, report);
                }
                else
                {
                    List<NetworkGameManager> managers = GetSceneComponents<NetworkGameManager>(scene);
                    if (managers.Count > 0)
                    {
                        report.Warning(
                            "ARCH-NETWORK-MANAGER-NON-GAMEPLAY",
                            scenePath,
                            string.Join(", ", managers.Select(manager => GetHierarchyPath(manager.transform))),
                            "NetworkGameManager exists outside the designated gameplay scene.");
                    }
                }
            });
        }

        if (!gameplaySceneFound)
        {
            report.Error(
                "ARCH-GAMEPLAY-SCENE-MISSING",
                GameplayScenePath,
                string.Empty,
                "Designated gameplay scene was not found under Assets/1.Scenes.");
        }

        ValidateCrossSceneHidingSpotRisks(allHidingSpots, report);
    }

    private static void ValidateNetworkGameManager(
        Scene scene,
        string scenePath,
        ValidationReport report)
    {
        List<NetworkGameManager> managers = GetSceneComponents<NetworkGameManager>(scene);
        if (managers.Count != 1)
        {
            report.Error(
                "ARCH-NETWORK-MANAGER-COUNT",
                scenePath,
                string.Empty,
                $"Gameplay scene must contain exactly one NetworkGameManager; found {managers.Count}.");
            return;
        }

        NetworkGameManager manager = managers[0];
        if (!manager.enabled || !manager.gameObject.activeInHierarchy)
        {
            report.Error(
                "ARCH-NETWORK-MANAGER-INACTIVE",
                scenePath,
                GetHierarchyPath(manager.transform),
                "NetworkGameManager must be enabled on an active GameObject.");
        }

        SerializedObject serializedManager = new SerializedObject(manager);
        ValidateManagerPrefabReference(
            serializedManager.FindProperty("playerPrefab"),
            NetworkPlayerPrefabPath,
            "playerPrefab",
            scenePath,
            manager,
            report);
        ValidateManagerPrefabReference(
            serializedManager.FindProperty("enemyPrefab"),
            NetworkEnemyPrefabPath,
            "enemyPrefab",
            scenePath,
            manager,
            report);

        SerializedProperty sessionName = serializedManager.FindProperty("sessionName");
        if (sessionName == null || string.IsNullOrWhiteSpace(sessionName.stringValue))
        {
            report.Error(
                "ARCH-NETWORK-SESSION-NAME",
                scenePath,
                GetHierarchyPath(manager.transform),
                "NetworkGameManager.sessionName must be non-empty.");
        }

        ValidateSpawnPointList(
            serializedManager.FindProperty("playerSpawnPoints"),
            "playerSpawnPoints",
            scene,
            scenePath,
            manager,
            report);
        ValidateSpawnPointList(
            serializedManager.FindProperty("enemySpawnPoints"),
            "enemySpawnPoints",
            scene,
            scenePath,
            manager,
            report);

        List<NetworkRunner> runners = GetSceneComponents<NetworkRunner>(scene);
        if (runners.Count > 1)
        {
            report.Error(
                "ARCH-NETWORK-RUNNER-COUNT",
                scenePath,
                string.Join(", ", runners.Select(runner => GetHierarchyPath(runner.transform))),
                $"Only one NetworkRunner may exist in the gameplay scene; found {runners.Count}.");
        }
        else if (runners.Count == 1 && runners[0].gameObject != manager.gameObject)
        {
            report.Error(
                "ARCH-NETWORK-RUNNER-HOST",
                scenePath,
                GetHierarchyPath(runners[0].transform),
                "NetworkRunner must be hosted by NetworkGameManager; NetworkSessionService otherwise creates a second runner.");
        }
        else if (runners.Count == 0)
        {
            report.Warning(
                "ARCH-NETWORK-RUNNER-RUNTIME-INSTALL",
                scenePath,
                GetHierarchyPath(manager.transform),
                "NetworkRunner is not serialized; NetworkSessionService will install it at runtime.");
        }

        ValidateOptionalServiceHostComponent<NetworkSceneManagerDefault>(manager, scenePath, report);
        ValidateOptionalServiceHostComponent<NetworkObjectProviderDefault>(manager, scenePath, report);
    }

    private static void ValidateManagerPrefabReference(
        SerializedProperty property,
        string expectedPath,
        string propertyName,
        string scenePath,
        NetworkGameManager manager,
        ValidationReport report)
    {
        if (property == null || property.objectReferenceValue == null)
        {
            report.Error(
                "ARCH-NETWORK-PREFAB-REFERENCE",
                scenePath,
                GetHierarchyPath(manager.transform),
                $"NetworkGameManager.{propertyName} is not assigned.");
            return;
        }

        string actualPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
        if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            report.Error(
                "ARCH-NETWORK-PREFAB-PATH",
                scenePath,
                GetHierarchyPath(manager.transform),
                $"NetworkGameManager.{propertyName} references '{actualPath}', expected '{expectedPath}'.");
        }
    }

    private static void ValidateSpawnPointList(
        SerializedProperty property,
        string propertyName,
        Scene ownerScene,
        string scenePath,
        NetworkGameManager manager,
        ValidationReport report)
    {
        if (property == null || !property.isArray || property.arraySize == 0)
        {
            report.Error(
                "ARCH-NETWORK-SPAWN-POINTS-EMPTY",
                scenePath,
                GetHierarchyPath(manager.transform),
                $"NetworkGameManager.{propertyName} must contain at least one scene Transform.");
            return;
        }

        HashSet<Object> unique = new HashSet<Object>();
        for (int i = 0; i < property.arraySize; i++)
        {
            Object reference = property.GetArrayElementAtIndex(i).objectReferenceValue;
            if (reference is not Transform spawnPoint)
            {
                report.Error(
                    "ARCH-NETWORK-SPAWN-POINT-NULL",
                    scenePath,
                    GetHierarchyPath(manager.transform),
                    $"{propertyName}[{i}] is null or is not a Transform.");
                continue;
            }

            if (spawnPoint.gameObject.scene != ownerScene)
            {
                report.Error(
                    "ARCH-NETWORK-SPAWN-POINT-EXTERNAL",
                    scenePath,
                    GetHierarchyPath(spawnPoint),
                    $"{propertyName}[{i}] belongs to a different scene or asset.");
            }

            if (!unique.Add(reference))
            {
                report.Warning(
                    "ARCH-NETWORK-SPAWN-POINT-DUPLICATE",
                    scenePath,
                    GetHierarchyPath(spawnPoint),
                    $"{propertyName} contains a duplicate Transform reference.");
            }
        }
    }

    private static void ValidateOptionalServiceHostComponent<T>(
        NetworkGameManager manager,
        string scenePath,
        ValidationReport report)
        where T : Component
    {
        if (manager.GetComponent<T>() == null)
        {
            report.Warning(
                "ARCH-NETWORK-SERVICE-RUNTIME-INSTALL",
                scenePath,
                GetHierarchyPath(manager.transform),
                $"{typeof(T).Name} is not serialized; NetworkSessionService will install it at runtime.");
        }
    }

    private static void ValidateMissingMonoScripts(
        GameObject root,
        string assetPath,
        ValidationReport report)
    {
        foreach (Transform transform in EnumerateHierarchy(root.transform))
        {
            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (missingCount <= 0)
                continue;

            report.Error(
                "ARCH-MISSING-MONOSCRIPT",
                assetPath,
                GetHierarchyPath(transform),
                $"GameObject contains {missingCount} unresolved MonoScript component(s).");
        }
    }

    private static void ValidateMissingMonoScripts(
        Scene scene,
        string assetPath,
        ValidationReport report)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            ValidateMissingMonoScripts(root, assetPath, report);
    }

    private static void ValidatePrefabHidingSpotIds(
        GameObject root,
        string prefabPath,
        ValidationReport report)
    {
        NetworkHidingSpot[] spots = root.GetComponentsInChildren<NetworkHidingSpot>(true);
        Dictionary<int, List<NetworkHidingSpot>> groups = new Dictionary<int, List<NetworkHidingSpot>>();

        foreach (NetworkHidingSpot spot in spots)
        {
            int serializedId = ReadSerializedHidingSpotId(spot, prefabPath, report);
            if (serializedId <= 0)
            {
                report.Error(
                    "ARCH-HIDING-PREFAB-ID-INVALID",
                    prefabPath,
                    GetHierarchyPath(spot.transform),
                    $"Serialized hiding spot id must be positive; found {serializedId}.");
                continue;
            }

            if (!groups.TryGetValue(serializedId, out List<NetworkHidingSpot> matches))
            {
                matches = new List<NetworkHidingSpot>();
                groups.Add(serializedId, matches);
            }

            matches.Add(spot);
        }

        foreach (KeyValuePair<int, List<NetworkHidingSpot>> group in groups)
        {
            if (group.Value.Count <= 1)
                continue;

            report.Error(
                "ARCH-HIDING-PREFAB-ID-DUPLICATE",
                prefabPath,
                string.Join(", ", group.Value.Select(spot => GetHierarchyPath(spot.transform))),
                $"Serialized hiding spot id {group.Key} is duplicated {group.Value.Count} times inside one prefab.");
        }
    }

    private static List<HidingSpotRecord> CollectHidingSpotRecords(
        Scene scene,
        string scenePath,
        ValidationReport report)
    {
        List<HidingSpotRecord> records = new List<HidingSpotRecord>();
        foreach (NetworkHidingSpot spot in GetSceneComponents<NetworkHidingSpot>(scene))
        {
            int serializedId = ReadSerializedHidingSpotId(spot, scenePath, report);
            records.Add(new HidingSpotRecord
            {
                ScenePath = scenePath,
                SceneName = scene.name,
                HierarchyPath = GetHierarchyPath(spot.transform),
                SerializedId = serializedId
            });
        }

        return records;
    }

    private static void ValidateSceneHidingSpotIds(
        List<HidingSpotRecord> records,
        string scenePath,
        ValidationReport report)
    {
        Dictionary<int, List<HidingSpotRecord>> positiveGroups = records
            .Where(record => record.SerializedId > 0)
            .GroupBy(record => record.SerializedId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (HidingSpotRecord record in records)
        {
            if (record.SerializedId <= 0)
            {
                report.Error(
                    "ARCH-HIDING-SCENE-ID-INVALID",
                    scenePath,
                    record.HierarchyPath,
                    $"Serialized hiding spot id must be positive; found {record.SerializedId}.");
            }
        }

        foreach (KeyValuePair<int, List<HidingSpotRecord>> group in positiveGroups)
        {
            if (group.Value.Count <= 1)
                continue;

            report.Error(
                "ARCH-HIDING-SERIALIZED-DUPLICATE",
                scenePath,
                string.Join(", ", group.Value.Select(record => record.HierarchyPath)),
                $"Serialized hiding spot id {group.Key} is duplicated {group.Value.Count} times. " +
                "Runtime fallback IDs hide the conflict but make auditing and reservations fragile.");
        }

        foreach (HidingSpotRecord record in records)
        {
            bool hasUniquePositiveId = record.SerializedId > 0
                && positiveGroups.TryGetValue(record.SerializedId, out List<HidingSpotRecord> matches)
                && matches.Count == 1;
            record.EffectiveCandidate = hasUniquePositiveId
                ? record.SerializedId
                : BuildStableHidingSpotId(record.SceneName, record.HierarchyPath);
        }

        foreach (IGrouping<int, HidingSpotRecord> collision in records.GroupBy(record => record.EffectiveCandidate))
        {
            List<HidingSpotRecord> matches = collision.ToList();
            if (matches.Count <= 1)
                continue;

            report.Error(
                "ARCH-HIDING-EFFECTIVE-COLLISION",
                scenePath,
                string.Join(", ", matches.Select(record => record.HierarchyPath)),
                $"Effective hiding spot candidate id {collision.Key} collides for {matches.Count} objects. " +
                "Runtime increment fallback is registration-order dependent.");
        }
    }

    private static void ValidateCrossSceneHidingSpotRisks(
        List<HidingSpotRecord> records,
        ValidationReport report)
    {
        foreach (IGrouping<int, HidingSpotRecord> group in records
                     .Where(record => record.SerializedId > 0)
                     .GroupBy(record => record.SerializedId))
        {
            List<string> scenes = group.Select(record => record.ScenePath).Distinct().ToList();
            if (scenes.Count <= 1)
                continue;

            report.Warning(
                "ARCH-HIDING-CROSS-SCENE-SERIALIZED",
                string.Join(", ", scenes),
                string.Empty,
                $"Serialized hiding spot id {group.Key} occurs in {scenes.Count} scenes and can collide if scenes are loaded additively.");
        }

        foreach (IGrouping<int, HidingSpotRecord> group in records.GroupBy(record => record.EffectiveCandidate))
        {
            List<string> scenes = group.Select(record => record.ScenePath).Distinct().ToList();
            if (scenes.Count <= 1)
                continue;

            report.Warning(
                "ARCH-HIDING-CROSS-SCENE-EFFECTIVE",
                string.Join(", ", scenes),
                string.Empty,
                $"Effective hiding spot candidate id {group.Key} occurs in multiple scenes and is unsafe for additive loading.");
        }
    }

    private static int ReadSerializedHidingSpotId(
        NetworkHidingSpot spot,
        string assetPath,
        ValidationReport report)
    {
        SerializedObject serializedSpot = new SerializedObject(spot);
        SerializedProperty spotId = serializedSpot.FindProperty("spotId");
        if (spotId != null)
            return spotId.intValue;

        report.Error(
            "ARCH-HIDING-ID-SCHEMA",
            assetPath,
            GetHierarchyPath(spot.transform),
            "NetworkHidingSpot.spotId could not be inspected.");
        return 0;
    }

    private static int BuildStableHidingSpotId(string sceneName, string hierarchyPath)
    {
        unchecked
        {
            int hash = HidingSpotStableIdSeed;
            AppendStableHash(ref hash, sceneName);
            AppendStableHash(ref hash, hierarchyPath);
            int stableId = Mathf.Abs(hash == int.MinValue ? int.MaxValue : hash);
            return stableId > 0 ? stableId : 1;
        }
    }

    private static void AppendStableHash(ref int hash, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        for (int i = 0; i < value.Length; i++)
            hash = (hash * 31) ^ value[i];
    }

    private static void InspectPrefab(
        string assetPath,
        ValidationReport report,
        Action<GameObject> inspect)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null)
        {
            report.Error("ARCH-PREFAB-MISSING", assetPath, string.Empty, "Required prefab asset does not exist or cannot be loaded.");
            return;
        }

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null)
            {
                report.Error("ARCH-PREFAB-LOAD", assetPath, string.Empty, "Prefab contents could not be loaded.");
                return;
            }

            inspect(root);
        }
        catch (Exception exception)
        {
            report.Error("ARCH-PREFAB-EXCEPTION", assetPath, string.Empty, exception.ToString());
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void InspectScene(
        string scenePath,
        ValidationReport report,
        Action<Scene> inspect)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForInspection = !scene.IsValid() || !scene.isLoaded;

        try
        {
            if (openedForInspection)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            if (!scene.IsValid() || !scene.isLoaded)
            {
                report.Error("ARCH-SCENE-LOAD", scenePath, string.Empty, "Scene could not be loaded for read-only validation.");
                return;
            }

            inspect(scene);
        }
        catch (Exception exception)
        {
            report.Error("ARCH-SCENE-EXCEPTION", scenePath, string.Empty, exception.ToString());
        }
        finally
        {
            if (openedForInspection && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static IEnumerable<string> FindAssetPaths(string filter, string searchRoot)
    {
        return AssetDatabase.FindAssets(filter, new[] { searchRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidatePrefabRootName(
        GameObject root,
        string expectedName,
        string assetPath,
        ValidationReport report)
    {
        if (!string.Equals(root.name, expectedName, StringComparison.Ordinal))
        {
            report.Error(
                "ARCH-PREFAB-ROOT-NAME",
                assetPath,
                root.name,
                $"Prefab root must be named '{expectedName}'.");
        }
    }

    private static Transform RequirePath(
        GameObject root,
        string relativePath,
        string assetPath,
        ValidationReport report)
    {
        Transform transform = root.transform.Find(relativePath);
        if (transform == null)
        {
            report.Error(
                "ARCH-HIERARCHY-MISSING",
                assetPath,
                root.name,
                $"Required hierarchy path '{relativePath}' is missing.");
        }

        return transform;
    }

    private static void RequireComponentOn(
        GameObject root,
        Transform target,
        Type componentType,
        string assetPath,
        ValidationReport report)
    {
        if (target == null)
            return;

        Component[] components = target.GetComponents(componentType);
        if (components.Length == 0)
        {
            report.Error(
                "ARCH-COMPONENT-MISSING",
                assetPath,
                GetHierarchyPath(target),
                $"Required component {componentType.Name} is missing.");
        }
        else if (components.Length > 1)
        {
            report.Error(
                "ARCH-COMPONENT-DUPLICATE",
                assetPath,
                GetHierarchyPath(target),
                $"Required component {componentType.Name} appears {components.Length} times.");
        }
    }

    private static void RequireComponentInSubtree<T>(
        GameObject root,
        string assetPath,
        ValidationReport report)
        where T : Component
    {
        T[] components = root.GetComponentsInChildren<T>(true);
        if (components.Length == 0)
        {
            report.Error(
                "ARCH-COMPONENT-MISSING",
                assetPath,
                root.name,
                $"Required component {typeof(T).Name} is missing from the prefab subtree.");
        }
        else if (components.Length > 1)
        {
            report.Error(
                "ARCH-COMPONENT-DUPLICATE",
                assetPath,
                root.name,
                $"Required component {typeof(T).Name} appears {components.Length} times in the prefab subtree.");
        }
    }

    private static void ValidateSerializedReference(
        Object owner,
        string propertyName,
        Object expected,
        string assetPath,
        ValidationReport report)
    {
        SerializedObject serializedObject = new SerializedObject(owner);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            report.Error(
                "ARCH-SERIALIZED-SCHEMA",
                assetPath,
                owner.name,
                $"Serialized property '{propertyName}' could not be inspected.");
            return;
        }

        if (expected == null || property.objectReferenceValue != expected)
        {
            report.Error(
                "ARCH-SERIALIZED-WIRING",
                assetPath,
                owner.name,
                $"Serialized property '{propertyName}' is not wired to the required architecture component.");
        }
    }

    private static void ValidateBranchParent(
        Transform branch,
        Transform expectedParent,
        string assetPath,
        ValidationReport report)
    {
        if (branch != null && branch.parent != expectedParent)
        {
            report.Error(
                "ARCH-BRANCH-PARENT",
                assetPath,
                GetHierarchyPath(branch),
                $"Architecture branch must be a direct child of {expectedParent.name}.");
        }
    }

    private static List<T> GetSceneComponents<T>(Scene scene) where T : Component
    {
        List<T> results = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<T>(true));
        return results;
    }

    private static IEnumerable<Transform> EnumerateHierarchy(Transform root)
    {
        yield return root;
        for (int i = 0; i < root.childCount; i++)
        {
            foreach (Transform descendant in EnumerateHierarchy(root.GetChild(i)))
                yield return descendant;
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private sealed class HidingSpotRecord
    {
        public string ScenePath;
        public string SceneName;
        public string HierarchyPath;
        public int SerializedId;
        public int EffectiveCandidate;
    }

    private enum ValidationSeverity
    {
        Warning,
        Error
    }

    private readonly struct ValidationIssue
    {
        public readonly ValidationSeverity Severity;
        public readonly string Code;
        public readonly string AssetPath;
        public readonly string ObjectPath;
        public readonly string Message;

        public ValidationIssue(
            ValidationSeverity severity,
            string code,
            string assetPath,
            string objectPath,
            string message)
        {
            Severity = severity;
            Code = code;
            AssetPath = assetPath ?? string.Empty;
            ObjectPath = objectPath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            string location = string.IsNullOrWhiteSpace(ObjectPath)
                ? AssetPath
                : $"{AssetPath} :: {ObjectPath}";
            return $"[{Code}] {location} - {Message}";
        }
    }

    private sealed class ValidationReport
    {
        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();

        public int ErrorCount => issues.Count(issue => issue.Severity == ValidationSeverity.Error);
        public int WarningCount => issues.Count(issue => issue.Severity == ValidationSeverity.Warning);

        public void Error(string code, string assetPath, string objectPath, string message)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, code, assetPath, objectPath, message));
        }

        public void Warning(string code, string assetPath, string objectPath, string message)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, code, assetPath, objectPath, message));
        }

        public void Log()
        {
            foreach (ValidationIssue issue in issues
                         .OrderByDescending(issue => issue.Severity)
                         .ThenBy(issue => issue.AssetPath, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(issue => issue.Code, StringComparer.Ordinal))
            {
                if (issue.Severity == ValidationSeverity.Error)
                    Debug.LogError(issue.ToString());
                else
                    Debug.LogWarning(issue.ToString());
            }

            string summary =
                $"ProjectArchitectureValidator (read-only): {ErrorCount} error(s), {WarningCount} warning(s).";
            if (ErrorCount > 0)
                Debug.LogError(summary);
            else if (WarningCount > 0)
                Debug.LogWarning(summary);
            else
                Debug.Log(summary);
        }
    }
}
#endif
