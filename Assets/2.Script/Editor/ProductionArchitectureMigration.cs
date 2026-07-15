#if UNITY_EDITOR
using System;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ProductionArchitectureMigration
{
    private const string LocalPlayerPrefabPath = "Assets/4.Prefabs/Player.prefab";
    private const string NetworkPlayerPrefabPath = "Assets/4.Prefabs/Network/NetworkPlayer.prefab";
    private const string NetworkEnemyPrefabPath = "Assets/4.Prefabs/Network/NetworkCSHEnemy.prefab";

    [MenuItem("Tools/Prototype005/Architecture/Migrate Prefabs")]
    public static void Run()
    {
        MigratePrefab(LocalPlayerPrefabPath, root => ApplyPlayerHierarchy(root, false));
        MigratePrefab(NetworkPlayerPrefabPath, root => ApplyPlayerHierarchy(root, true));
        MigratePrefab(NetworkEnemyPrefabPath, ApplyEnemyHierarchy);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();
        Debug.Log("ProductionArchitectureMigration: player and enemy prefabs migrated successfully.");
    }

    public static void RunFromCommandLine()
    {
        Run();
    }

    public static void ApplyPlayerHierarchy(GameObject player, bool networked)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        NetworkEntityRoot entityRoot = EnsureComponent<NetworkEntityRoot>(player);
        SetObjectReference(entityRoot, "ownerOverride", player);

        Transform simulation = EnsureChild(player.transform, "Simulation");
        Transform presentation = EnsureChild(player.transform, "Presentation");
        Transform sensors = EnsureChild(player.transform, "Sensors");
        Transform rig = EnsureChild(player.transform, "Rig");

        Transform locomotion = EnsureChild(simulation, "Locomotion");
        Transform inventoryRoot = EnsureChild(simulation, "Inventory");
        Transform hidingRoot = EnsureChild(simulation, "Hiding");
        Transform emotesRoot = EnsureChild(simulation, "Emotes");
        Transform entityComponents = EnsureExistingOrChild(player.transform, simulation, "PlayerEntityComponents");
        Transform presentationComponents = EnsureExistingOrChild(player.transform, presentation, "PlayerPresentationComponents");

        MoveNamedChild(player.transform, presentation, "CameraComponent");
        MoveNamedChild(player.transform, presentation, "Camera");
        MoveNamedChild(player.transform, presentation, "DeadCamera");
        MoveNamedChild(player.transform, sensors, "GroundChecker");
        MoveNamedChild(player.transform, sensors, "ItemChecker");
        MoveNamedChild(player.transform, rig, "Visual");
        MoveNamedChild(player.transform, rig, "HeldItemPoseTargets");

        PlayerMovement movement = player.GetComponentInChildren<PlayerMovement>(true);
        if (movement == null)
            throw new InvalidOperationException($"{player.name} has no {nameof(PlayerMovement)} component.");

        PlayerStamina stamina = MoveComponent<PlayerStamina>(player, locomotion.gameObject);
        if (stamina != null)
            SetObjectReference(movement, "_stamina", stamina);

        PlayerCameraPresentation cameraPresentation = EnsureComponent<PlayerCameraPresentation>(presentationComponents.gameObject);
        PlayerAnimationPresentation animationPresentation = EnsureComponent<PlayerAnimationPresentation>(presentationComponents.gameObject);
        EnsureComponent<PlayerNetworkPowerBridge>(presentationComponents.gameObject);
        MigratePlayerPresentationSettings(movement, cameraPresentation, animationPresentation);

        if (player.GetComponentInChildren<RagdollEntityComponent>(true) != null)
        {
            Transform ragdollServices = EnsureChild(entityComponents, "RagdollServices");
            EnsureComponent<RagdollRigPresentation>(EnsureChild(ragdollServices, "RigPresentation").gameObject);
            EnsureComponent<RagdollSurfaceBloodController>(EnsureChild(ragdollServices, "SurfaceBlood").gameObject);
        }

        if (networked)
        {
            if (player.GetComponent<NetworkObject>() == null)
                throw new InvalidOperationException($"Network player {player.name} must keep its NetworkObject on the root.");

            NetworkInventory inventory = MoveComponent<NetworkInventory>(player, inventoryRoot.gameObject);
            NetworkPlayerHidingComponent hiding = MoveComponent<NetworkPlayerHidingComponent>(player, hidingRoot.gameObject);
            MoveComponent<NetworkEmoteAudioPlayer>(player, emotesRoot.gameObject);

            PlayerInventoryInput inventoryInput = EnsureComponent<PlayerInventoryInput>(inventoryRoot.gameObject);
            InventoryHeldItemPresentation heldPresentation = EnsureComponent<InventoryHeldItemPresentation>(presentationComponents.gameObject);
            PlayerHidingPresentation hidingPresentation = EnsureComponent<PlayerHidingPresentation>(presentationComponents.gameObject);

            if (inventory != null)
            {
                SetObjectReference(inventory, "input", inventoryInput);
                SetObjectReference(inventory, "heldItemPresentation", heldPresentation);
                SetObjectReference(inventoryInput, "inventory", inventory);
                MigrateInventoryPresentationSettings(inventory, heldPresentation);
            }

            if (hiding != null)
            {
                SetObjectReference(hiding, "playerMovement", movement);
                SetObjectReference(hiding, "inventory", inventory);
                SetObjectReference(hiding, "itemHolder", player.GetComponentInChildren<NetworkPlayerItemHolder>(true));
                SetObjectReference(hiding, "presentation", hidingPresentation);
            }

            SetObjectReference(hidingPresentation, "playerMovement", movement);
            ValidateSingleRootNetworkObject(player);
        }

        entityRoot.InitializeComponents();
        EditorUtility.SetDirty(player);
    }

    public static void ApplyEnemyHierarchy(GameObject enemy)
    {
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));

        NetworkObject rootNetworkObject = enemy.GetComponent<NetworkObject>();
        if (rootNetworkObject == null)
            throw new InvalidOperationException($"Network enemy {enemy.name} must keep its NetworkObject on the root.");

        NetworkEntityRoot entityRoot = EnsureComponent<NetworkEntityRoot>(enemy);
        SetObjectReference(entityRoot, "ownerOverride", enemy);

        Transform services = EnsureChild(enemy.transform, "Services");
        Transform perceptionRoot = EnsureChild(services, "Perception");
        Transform navigationRoot = EnsureChild(services, "Navigation");
        Transform combatRoot = EnsureChild(services, "Combat");
        Transform presentation = EnsureChild(enemy.transform, "Presentation");
        Transform rig = EnsureChild(enemy.transform, "Rig");

        CSHEnemy coordinator = enemy.GetComponent<CSHEnemy>();
        if (coordinator == null)
            throw new InvalidOperationException($"{enemy.name} has no {nameof(CSHEnemy)} coordinator.");

        EnemyPerceptionComponent perception = EnsureComponent<EnemyPerceptionComponent>(perceptionRoot.gameObject);
        EnemyNavigationComponent navigation = EnsureComponent<EnemyNavigationComponent>(navigationRoot.gameObject);
        EnemyCombatComponent combat = EnsureComponent<EnemyCombatComponent>(combatRoot.gameObject);
        EnemyAnimationDriver animationDriver = MoveComponent<EnemyAnimationDriver>(enemy, presentation.gameObject);

        SetObjectReference(coordinator, "perceptionComponent", perception);
        SetObjectReference(coordinator, "navigationComponent", navigation);
        SetObjectReference(coordinator, "combatComponent", combat);
        if (animationDriver != null)
            SetObjectReference(coordinator, "animationDriver", animationDriver);

        MoveNamedChild(enemy.transform, rig, "HeadmanVisual");
        MoveNamedChild(enemy.transform, presentation, "HeadmanSound");

        coordinator.ConfigureLegacyComponents();
        perception.CommitLegacySettings();
        navigation.CommitLegacySettings();
        combat.CommitLegacySettings();
        EditorUtility.SetDirty(perception);
        EditorUtility.SetDirty(navigation);
        EditorUtility.SetDirty(combat);
        entityRoot.InitializeComponents();
        ValidateSingleRootNetworkObject(enemy);
        EditorUtility.SetDirty(enemy);
    }

    private static void MigratePrefab(string path, Action<GameObject> migrate)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
            throw new InvalidOperationException($"Could not load prefab contents at {path}.");

        try
        {
            migrate(root);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (saved == null)
                throw new InvalidOperationException($"Could not save migrated prefab at {path}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void MigratePlayerPresentationSettings(
        PlayerMovement movement,
        PlayerCameraPresentation cameraPresentation,
        PlayerAnimationPresentation animationPresentation)
    {
        SerializedObject movementData = new SerializedObject(movement);

        SerializedObject cameraData = new SerializedObject(cameraPresentation);
        cameraData.FindProperty("usePlayerMovementSettings").boolValue = false;
        cameraData.FindProperty("playerCamera").objectReferenceValue = movementData.FindProperty("_playerCamera").objectReferenceValue;
        cameraData.FindProperty("normalFOV").floatValue = movementData.FindProperty("_normalFOV").floatValue;
        cameraData.FindProperty("sprintFOV").floatValue = movementData.FindProperty("_sprintFOV").floatValue;
        cameraData.FindProperty("fovTransitionSpeed").floatValue = movementData.FindProperty("_fovTransitionSpeed").floatValue;
        cameraData.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject animationData = new SerializedObject(animationPresentation);
        animationData.FindProperty("usePlayerMovementSettings").boolValue = false;
        animationData.FindProperty("firstPersonAnimator").objectReferenceValue = movementData.FindProperty("_camAnimator").objectReferenceValue;
        animationData.FindProperty("visualAnimator").objectReferenceValue = movementData.FindProperty("_visualAnimator").objectReferenceValue;
        animationData.FindProperty("visualAnimatorController").objectReferenceValue = movementData.FindProperty("_visualAnimatorController").objectReferenceValue;
        animationData.FindProperty("visualCrossFadeTime").floatValue = movementData.FindProperty("_visualCrossFadeTime").floatValue;
        animationData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void MigrateInventoryPresentationSettings(
        NetworkInventory inventory,
        InventoryHeldItemPresentation heldPresentation)
    {
        SerializedObject inventoryData = new SerializedObject(inventory);

        SerializedObject presentationData = new SerializedObject(heldPresentation);
        presentationData.FindProperty("rightHandAnchor").objectReferenceValue = inventoryData.FindProperty("rightHandAnchor").objectReferenceValue;
        presentationData.FindProperty("firstPersonLocalPosition").vector3Value = inventoryData.FindProperty("firstPersonHeldLocalPosition").vector3Value;
        presentationData.FindProperty("firstPersonLocalEuler").vector3Value = inventoryData.FindProperty("firstPersonHeldLocalEuler").vector3Value;
        presentationData.FindProperty("thirdPersonLocalPosition").vector3Value = inventoryData.FindProperty("thirdPersonHeldLocalPosition").vector3Value;
        presentationData.FindProperty("thirdPersonLocalEuler").vector3Value = inventoryData.FindProperty("thirdPersonHeldLocalEuler").vector3Value;
        presentationData.FindProperty("useInventoryLegacySettings").boolValue = false;
        presentationData.ApplyModifiedPropertiesWithoutUndo();

        heldPresentation.CommitLegacySettings();
    }

    private static T MoveComponent<T>(GameObject entityRoot, GameObject target) where T : Component
    {
        T[] components = entityRoot.GetComponentsInChildren<T>(true);
        if (components.Length == 0)
            return null;

        T source = components[0];
        if (source.gameObject == target)
            return source;

        T existing = target.GetComponent<T>();
        if (existing != null)
        {
            ReplaceObjectReferences(entityRoot, source, existing);
            Object.DestroyImmediate(source, true);
            return existing;
        }

        if (!ComponentUtility.CopyComponent(source) || !ComponentUtility.PasteComponentAsNew(target))
            throw new InvalidOperationException($"Failed to move {typeof(T).Name} from {source.gameObject.name} to {target.name}.");

        T destination = target.GetComponent<T>();
        if (destination == null)
            throw new InvalidOperationException($"Moved {typeof(T).Name} could not be resolved on {target.name}.");

        ReplaceObjectReferences(entityRoot, source, destination);
        Object.DestroyImmediate(source, true);
        return destination;
    }

    private static void ReplaceObjectReferences(GameObject root, Object source, Object destination)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null || component == source)
                continue;

            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty iterator = serializedObject.GetIterator();
            bool changed = false;
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.objectReferenceValue != source)
                    continue;

                iterator.objectReferenceValue = destination;
                changed = true;
            }

            if (changed)
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static Transform EnsureExistingOrChild(Transform searchRoot, Transform parent, string name)
    {
        Transform child = FindChildByName(searchRoot, name) ?? EnsureChild(parent, name);
        if (child.parent != parent)
            child.SetParent(parent, false);

        ResetLocalTransform(child);
        return child;
    }

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
        }

        ResetLocalTransform(child);
        return child;
    }

    private static void MoveNamedChild(Transform searchRoot, Transform targetParent, string childName)
    {
        Transform child = FindChildByName(searchRoot, childName);
        if (child == null || child == targetParent || child.IsChildOf(targetParent))
            return;

        child.SetParent(targetParent, false);
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;

            Transform found = FindChildByName(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void ResetLocalTransform(Transform transform)
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private static void SetObjectReference(Object target, string propertyName, Object value)
    {
        if (target == null)
            return;

        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            return;

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ValidateSingleRootNetworkObject(GameObject root)
    {
        NetworkObject[] networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
        if (networkObjects.Length != 1 || networkObjects[0].gameObject != root)
            throw new InvalidOperationException($"{root.name} must contain exactly one NetworkObject on its root.");

        foreach (NetworkBehaviour behaviour in root.GetComponentsInChildren<NetworkBehaviour>(true))
        {
            if (behaviour.GetComponentInParent<NetworkObject>() != networkObjects[0])
                throw new InvalidOperationException($"{behaviour.GetType().Name} is outside {root.name}'s NetworkObject subtree.");
        }
    }
}
#endif
