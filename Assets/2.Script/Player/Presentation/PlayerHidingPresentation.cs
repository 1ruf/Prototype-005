using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerHidingPresentation : MonoBehaviour
{
    private const string HideStateName = "Hide";

    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private Transform visualRoot;

    private Transform owner;
    private Transform originalVisualParent;
    private Vector3 originalVisualLocalPosition;
    private Quaternion originalVisualLocalRotation;
    private Vector3 originalVisualLocalScale;
    private bool originalVisualPoseCached;
    private PlayerHidingPhase appliedPhase = PlayerHidingPhase.None;
    private bool localCameraOverrideApplied;
    private bool localCameraOverrideUsesCinemachine;
    private bool originalCharacterControllerEnabled;
    private bool physicalPresenceSuppressed;
    private Behaviour activeHidingVirtualCamera;
    private int originalHidingVirtualCameraPriority;
    private bool originalHidingVirtualCameraPriorityCached;
    private readonly List<Behaviour> disabledLocalCameraBehaviours = new();

    public void Initialize(GameObject entityOwner, PlayerMovement movement, Animator animator = null, Transform visual = null)
    {
        owner = (entityOwner != null ? entityOwner : gameObject).transform;
        playerMovement = movement != null ? movement : playerMovement;
        visualAnimator = animator != null ? animator : visualAnimator;
        visualRoot = visual != null ? visual : visualRoot;
        ResolveReferences();
    }

    public void Apply(
        PlayerHidingPhase phase,
        NetworkHidingSpot spot,
        NetworkPlayerItemHolder itemHolder,
        NetworkInventory inventory,
        bool force)
    {
        ResolveReferences();

        if (!force && appliedPhase == phase)
            return;

        if (phase == PlayerHidingPhase.None)
            RestoreVisiblePresentation(itemHolder, inventory);
        else
            ApplyHiddenPresentation(phase, spot, itemHolder, inventory);

        appliedPhase = phase;
    }

    public void TickLocalCamera(PlayerHidingPhase phase, NetworkHidingSpot spot)
    {
        if (!IsLocalPlayer())
            return;

        if (phase == PlayerHidingPhase.None)
        {
            SetLocalCameraOverride(false, null);
            return;
        }

        Transform cameraPose = spot != null ? spot.CameraPose : null;
        Camera camera = Camera.main;
        if (camera == null || cameraPose == null)
            return;

        SetLocalCameraOverride(true, spot);
        if (activeHidingVirtualCamera == null)
            camera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
    }

    public void ReleaseCameraOverride()
    {
        SetLocalCameraOverride(false, null);
    }

    private void OnDisable()
    {
        ReleaseCameraOverride();
    }

    private void ApplyHiddenPresentation(
        PlayerHidingPhase phase,
        NetworkHidingSpot spot,
        NetworkPlayerItemHolder itemHolder,
        NetworkInventory inventory)
    {
        if (spot == null)
            return;

        SetPhysicalPresence(false);
        CacheOriginalVisualPose();

        Transform target = spot.VisualPose;
        if (visualRoot != null && target != null)
        {
            visualRoot.SetParent(target, false);
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.identity;
        }

        if (visualAnimator != null)
            visualAnimator.CrossFade(HideStateName, 0.08f, 0);

        if (phase == PlayerHidingPhase.Entering)
            spot.PlayEnterAnimation();
        else if (phase == PlayerHidingPhase.Exiting)
            spot.PlayExitAnimation();

        itemHolder?.ForceStoreHeldItemForHiding();
        inventory?.ForceStoreHeldItemForHiding();
    }

    private void RestoreVisiblePresentation(NetworkPlayerItemHolder itemHolder, NetworkInventory inventory)
    {
        if (visualRoot != null && originalVisualPoseCached)
        {
            visualRoot.SetParent(originalVisualParent, false);
            visualRoot.localPosition = originalVisualLocalPosition;
            visualRoot.localRotation = originalVisualLocalRotation;
            visualRoot.localScale = originalVisualLocalScale;
        }

        originalVisualPoseCached = false;
        SetPhysicalPresence(true);
        SetLocalCameraOverride(false, null);
        itemHolder?.RestoreHeldItemAfterHiding();
        inventory?.RestoreHeldItemAfterHiding();

        if (IsLocalPlayer())
            playerMovement?.RefreshLocalCameraPresentation();
    }

    private void SetLocalCameraOverride(bool active, NetworkHidingSpot spot)
    {
        Behaviour requestedHidingCamera = active && spot != null ? spot.HidingVirtualCamera : null;
        bool useCinemachine = requestedHidingCamera != null;

        if (localCameraOverrideApplied == active
            && localCameraOverrideUsesCinemachine == useCinemachine
            && activeHidingVirtualCamera == requestedHidingCamera)
            return;

        if (localCameraOverrideApplied)
            RestoreLocalCameraOverride();

        if (active)
        {
            disabledLocalCameraBehaviours.Clear();
            if (useCinemachine)
            {
                SetCinemachineBrain(Camera.main != null ? Camera.main.gameObject : null, true);
                ActivateHidingVirtualCamera(requestedHidingCamera, spot);
            }
            else
            {
                DisableBehaviour(Camera.main != null ? GetCinemachineBrain(Camera.main.gameObject) : null);
                Type cameraType = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
                if (cameraType != null && owner != null)
                {
                    Component[] virtualCameras = owner.GetComponentsInChildren(cameraType, true);
                    foreach (Component virtualCamera in virtualCameras)
                        DisableBehaviour(virtualCamera as Behaviour);
                }
            }
        }

        localCameraOverrideApplied = active;
        localCameraOverrideUsesCinemachine = useCinemachine;
    }

    private void ActivateHidingVirtualCamera(Behaviour hidingCamera, NetworkHidingSpot spot)
    {
        if (hidingCamera == null)
            return;

        activeHidingVirtualCamera = hidingCamera;
        originalHidingVirtualCameraPriorityCached = TryGetCinemachineCameraPriority(hidingCamera, out originalHidingVirtualCameraPriority);

        Transform cameraPose = spot != null ? spot.CameraPose : null;
        if (cameraPose != null)
            hidingCamera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);

        hidingCamera.gameObject.SetActive(true);
        hidingCamera.enabled = true;
        SetCinemachineCameraPriority(hidingCamera, spot != null ? spot.HidingCameraPriority : 1000);
    }

    private void RestoreLocalCameraOverride()
    {
        foreach (Behaviour behaviour in disabledLocalCameraBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = true;
        }

        disabledLocalCameraBehaviours.Clear();
        if (activeHidingVirtualCamera != null)
        {
            if (originalHidingVirtualCameraPriorityCached)
                SetCinemachineCameraPriority(activeHidingVirtualCamera, originalHidingVirtualCameraPriority);

            activeHidingVirtualCamera.enabled = false;
            activeHidingVirtualCamera.gameObject.SetActive(false);
        }

        activeHidingVirtualCamera = null;
        originalHidingVirtualCameraPriority = 0;
        originalHidingVirtualCameraPriorityCached = false;
        localCameraOverrideApplied = false;
        localCameraOverrideUsesCinemachine = false;
    }

    private void DisableBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled)
            return;

        behaviour.enabled = false;
        disabledLocalCameraBehaviours.Add(behaviour);
    }

    private void CacheOriginalVisualPose()
    {
        if (originalVisualPoseCached || visualRoot == null)
            return;

        originalVisualParent = visualRoot.parent;
        originalVisualLocalPosition = visualRoot.localPosition;
        originalVisualLocalRotation = visualRoot.localRotation;
        originalVisualLocalScale = visualRoot.localScale;
        originalVisualPoseCached = true;
    }

    private void SetPhysicalPresence(bool active)
    {
        CharacterController characterController = owner != null ? owner.GetComponent<CharacterController>() : null;
        if (characterController == null)
            return;

        if (!active)
        {
            if (!physicalPresenceSuppressed)
            {
                originalCharacterControllerEnabled = characterController.enabled;
                physicalPresenceSuppressed = true;
            }

            characterController.enabled = false;
            return;
        }

        if (!physicalPresenceSuppressed)
            return;

        characterController.enabled = originalCharacterControllerEnabled;
        physicalPresenceSuppressed = false;
    }

    private bool IsLocalPlayer()
    {
        return playerMovement == null || playerMovement.IsLocalNetworkPlayer;
    }

    private void ResolveReferences()
    {
        if (owner == null)
            owner = transform.root;

        if (playerMovement == null && owner != null)
            playerMovement = owner.GetComponentInChildren<PlayerMovement>(true);

        if (visualRoot == null && owner != null)
            visualRoot = FindChildByName(owner, "Visual");

        if (visualAnimator == null)
            visualAnimator = visualRoot != null
                ? visualRoot.GetComponentInChildren<Animator>(true)
                : owner != null ? owner.GetComponentInChildren<Animator>(true) : null;
    }

    private static Behaviour GetCinemachineBrain(GameObject cameraObject)
    {
        Type brainType = Type.GetType("Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine");
        return brainType != null && cameraObject != null ? cameraObject.GetComponent(brainType) as Behaviour : null;
    }

    private static void SetCinemachineBrain(GameObject cameraObject, bool enabled)
    {
        Behaviour brain = GetCinemachineBrain(cameraObject);
        if (brain != null)
            brain.enabled = enabled;
    }

    private static bool TryGetCinemachineCameraPriority(Component virtualCamera, out int priority)
    {
        priority = 0;
        if (virtualCamera == null)
            return false;

        Type type = virtualCamera.GetType();
        object value = type.GetProperty("Priority")?.GetValue(virtualCamera)
                       ?? type.GetField("Priority")?.GetValue(virtualCamera);
        return TryExtractPriorityValue(value, out priority);
    }

    private static void SetCinemachineCameraPriority(Component virtualCamera, int priority)
    {
        if (virtualCamera == null)
            return;

        Type type = virtualCamera.GetType();
        bool prioritySet = false;
        System.Reflection.PropertyInfo property = type.GetProperty("Priority");
        if (property != null && property.CanWrite && TryBuildPriorityValue(property.PropertyType, priority, out object propertyValue))
        {
            property.SetValue(virtualCamera, propertyValue);
            prioritySet = true;
        }

        System.Reflection.FieldInfo field = type.GetField("Priority");
        if (!prioritySet && field != null && TryBuildPriorityValue(field.FieldType, priority, out object fieldValue))
            field.SetValue(virtualCamera, fieldValue);

        System.Reflection.FieldInfo legacyPriority = type.GetField(
            "m_LegacyPriority",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (legacyPriority != null && legacyPriority.FieldType == typeof(int))
            legacyPriority.SetValue(virtualCamera, priority);
    }

    private static bool TryExtractPriorityValue(object value, out int priority)
    {
        priority = 0;
        if (value is int intValue)
        {
            priority = intValue;
            return true;
        }

        if (value == null)
            return false;

        Type type = value.GetType();
        object nested = type.GetProperty("Value")?.GetValue(value)
                        ?? type.GetProperty("m_Value")?.GetValue(value)
                        ?? type.GetField("Value")?.GetValue(value)
                        ?? type.GetField("m_Value")?.GetValue(value);
        if (nested is not int nestedValue)
            return false;

        priority = nestedValue;
        return true;
    }

    private static bool TryBuildPriorityValue(Type priorityType, int priority, out object value)
    {
        value = null;
        if (priorityType == typeof(int))
        {
            value = priority;
            return true;
        }

        if (priorityType == null || !priorityType.IsValueType)
            return false;

        object boxed = Activator.CreateInstance(priorityType);
        System.Reflection.PropertyInfo enabledProperty = priorityType.GetProperty("Enabled") ?? priorityType.GetProperty("m_Enabled");
        System.Reflection.FieldInfo enabledField = priorityType.GetField("Enabled") ?? priorityType.GetField("m_Enabled");
        System.Reflection.PropertyInfo valueProperty = priorityType.GetProperty("Value") ?? priorityType.GetProperty("m_Value");
        System.Reflection.FieldInfo valueField = priorityType.GetField("Value") ?? priorityType.GetField("m_Value");

        if (enabledProperty != null && enabledProperty.CanWrite)
            enabledProperty.SetValue(boxed, true);
        else
            enabledField?.SetValue(boxed, true);

        if (valueProperty != null && valueProperty.CanWrite)
            valueProperty.SetValue(boxed, priority);
        else if (valueField != null)
            valueField.SetValue(boxed, priority);
        else
            return false;

        value = boxed;
        return true;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

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
}
