using Fusion;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerHidingComponent : NetworkBehaviour, INetworkEntityComponent
{
    private const string HideStateName = "Hide";

    private enum HidingPhase
    {
        None = 0,
        Entering = 1,
        Hidden = 2,
        Exiting = 3
    }

    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private NetworkPlayerItemHolder itemHolder;
    [SerializeField] private NetworkInventory inventory;
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private Transform visualRoot;

    [Networked] public NetworkBool IsHiding { get; private set; }
    [Networked] public NetworkBool IsHidePending { get; private set; }
    [Networked] public NetworkBool IsHidingCompromised { get; private set; }
    [Networked] private int CurrentSpotId { get; set; }
    [Networked] private HidingPhase Phase { get; set; }
    [Networked] private float TransitionEndTime { get; set; }

    private NetworkHidingSpot localRequestedSpot;
    private Transform originalVisualParent;
    private Vector3 originalVisualLocalPosition;
    private Quaternion originalVisualLocalRotation;
    private Vector3 originalVisualLocalScale;
    private bool originalVisualPoseCached;
    private HidingPhase appliedPresentationPhase = HidingPhase.None;
    private bool localPendingRequest;
    private bool localCameraOverrideApplied;
    private bool localCameraOverrideUsesCinemachine;
    private bool originalCharacterControllerEnabled;
    private bool physicalPresenceSuppressed;
    private UnityEngine.Behaviour activeHidingVirtualCamera;
    private bool originalHidingVirtualCameraGameObjectActive;
    private bool originalHidingVirtualCameraEnabled;
    private int originalHidingVirtualCameraPriority;
    private bool originalHidingVirtualCameraPriorityCached;
    private readonly List<UnityEngine.Behaviour> disabledLocalCameraBehaviours = new();
    private GameObject owner;

    public GameObject Owner => owner != null ? owner : gameObject;
    public bool BlocksPlayerInput => Phase != HidingPhase.None || IsHiding || IsHidePending || localPendingRequest;
    public bool CanUseItems => !IsHiding && !IsHidePending && !localPendingRequest;
    public bool CanRequestExit => Phase == HidingPhase.Hidden && IsHiding && !IsHidePending && !localPendingRequest;
    public NetworkHidingSpot CurrentSpot => ResolveSpot(CurrentSpotId);

    private void Awake()
    {
        Initialize(ResolveOwner());
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
        ResolveReferences();
    }

    public override void Spawned()
    {
        ResolveReferences();
        ApplyPresentation(true);
    }

    public override void Render()
    {
        ApplyPresentation(false);
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        if ((Phase == HidingPhase.Entering || Phase == HidingPhase.Exiting) && Runner != null && Runner.SimulationTime >= TransitionEndTime)
            CompleteTransitionStateAuthority();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        SetLocalCameraOverride(false, null);
    }

    private void OnDisable()
    {
        SetLocalCameraOverride(false, null);
    }

    private void LateUpdate()
    {
        CompleteLocalTransitionIfNeeded();
        ApplyLocalCameraPose();
    }

    public void RequestToggle(NetworkHidingSpot spot)
    {
        if (Phase == HidingPhase.Entering || Phase == HidingPhase.Exiting || localPendingRequest)
            return;

        if (Phase == HidingPhase.Hidden || IsHiding)
        {
            RequestExit();
            return;
        }

        RequestEnter(spot);
    }

    public void RequestEnter(NetworkHidingSpot spot)
    {
        if (spot == null || Phase != HidingPhase.None || IsHiding || IsHidePending || localPendingRequest)
            return;

        if (Object == null)
        {
            if (spot.TryReserveLocal(this))
                AcceptEnter(spot, false);
            return;
        }

        localRequestedSpot = spot;

        if (Object.HasStateAuthority)
        {
            TryEnterStateAuthority(spot);
            return;
        }

        localPendingRequest = true;
        RPC_RequestEnter(spot.SpotId);
    }

    public void RequestExit()
    {
        if (Phase != HidingPhase.Hidden || !IsHiding || IsHidePending || localPendingRequest)
            return;

        if (Object == null)
        {
            ExitStateAuthority();
            return;
        }

        if (Object.HasStateAuthority)
        {
            ExitStateAuthority();
            return;
        }

        localPendingRequest = true;
        RPC_RequestExit();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestEnter(int spotId)
    {
        NetworkHidingSpot spot = ResolveSpot(spotId);
        TryEnterStateAuthority(spot);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestExit()
    {
        ExitStateAuthority();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_ApplyServerDecision(NetworkBool accepted)
    {
        localPendingRequest = false;
        if (!accepted)
            localRequestedSpot = null;
    }

    private void TryEnterStateAuthority(NetworkHidingSpot spot)
    {
        if (spot == null || IsHiding)
        {
            RPC_ApplyServerDecision(false);
            return;
        }

        ResolveReferences();
        if (!spot.TryReserve(this))
        {
            RPC_ApplyServerDecision(false);
            return;
        }

        bool compromised = CSHEnemy.HasEnemyDetectedPlayer(playerMovement);
        AcceptEnter(spot, compromised);
        RPC_ApplyServerDecision(true);
    }

    private void AcceptEnter(NetworkHidingSpot spot, bool compromised)
    {
        if (spot == null)
            return;

        CurrentSpotId = spot.SpotId;
        IsHiding = false;
        IsHidePending = true;
        IsHidingCompromised = compromised;
        Phase = HidingPhase.Entering;
        TransitionEndTime = GetTransitionEndTime(spot.EnterDuration);
        localPendingRequest = false;
        localRequestedSpot = null;

        Transform hiddenPose = spot.PlayerHiddenPose;
        if (hiddenPose != null)
            TeleportOwner(hiddenPose.position, hiddenPose.rotation);

        itemHolder?.ForceStoreHeldItemForHiding();
        inventory?.ForceStoreHeldItemForHiding();
        ApplyPresentation(true);
    }

    private void ExitStateAuthority()
    {
        NetworkHidingSpot spot = CurrentSpot;
        if (spot == null)
        {
            CompleteExitStateAuthority(null);
            return;
        }

        IsHiding = false;
        IsHidePending = true;
        Phase = HidingPhase.Exiting;
        TransitionEndTime = GetTransitionEndTime(spot.ExitDuration);
        localPendingRequest = false;
        ApplyPresentation(true);
        RPC_ApplyServerDecision(true);
    }

    private void CompleteTransitionStateAuthority()
    {
        if (Phase == HidingPhase.Entering)
        {
            IsHiding = true;
            IsHidePending = false;
            Phase = HidingPhase.Hidden;
            ApplyPresentation(true);
            return;
        }

        if (Phase == HidingPhase.Exiting)
            CompleteExitStateAuthority(CurrentSpot);
    }

    private void CompleteLocalTransitionIfNeeded()
    {
        if (Object != null)
            return;

        if ((Phase == HidingPhase.Entering || Phase == HidingPhase.Exiting) && Time.time >= TransitionEndTime)
            CompleteTransitionStateAuthority();
    }

    private void CompleteExitStateAuthority(NetworkHidingSpot spot)
    {
        if (spot != null)
        {
            Transform exitPose = spot.ExitPose;
            if (exitPose != null)
                TeleportOwner(exitPose.position, exitPose.rotation);

            spot.Release(this);
        }

        IsHiding = false;
        IsHidePending = false;
        IsHidingCompromised = false;
        Phase = HidingPhase.None;
        TransitionEndTime = 0f;
        localPendingRequest = false;
        CurrentSpotId = 0;
        ApplyPresentation(true);
    }

    private void ApplyPresentation(bool force)
    {
        ResolveReferences();

        if (Phase != HidingPhase.None)
            localPendingRequest = false;

        if (!force && appliedPresentationPhase == Phase)
            return;

        if (Phase == HidingPhase.None)
            RestoreVisiblePresentation();
        else
            ApplyHidingPresentation(Phase);

        appliedPresentationPhase = Phase;
    }

    private void ApplyHidingPresentation(HidingPhase phase)
    {
        NetworkHidingSpot spot = CurrentSpot;
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

        if (phase == HidingPhase.Entering)
            spot.PlayEnterAnimation();
        else if (phase == HidingPhase.Exiting)
            spot.PlayExitAnimation();

        itemHolder?.ForceStoreHeldItemForHiding();
        inventory?.ForceStoreHeldItemForHiding();
    }

    private void RestoreVisiblePresentation()
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

        if (IsLocalPlayer())
            playerMovement?.RefreshLocalCameraPresentation();
    }

    private void ApplyLocalCameraPose()
    {
        if (!IsLocalPlayer())
        {
            return;
        }

        if (Phase == HidingPhase.None)
        {
            SetLocalCameraOverride(false, null);
            return;
        }

        NetworkHidingSpot spot = CurrentSpot;
        Transform cameraPose = spot != null ? spot.CameraPose : null;
        Camera camera = Camera.main;
        if (camera == null || cameraPose == null)
            return;

        SetLocalCameraOverride(true, spot);

        if (activeHidingVirtualCamera != null)
            return;

        camera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
    }

    private void SetLocalCameraOverride(bool active, NetworkHidingSpot spot)
    {
        UnityEngine.Behaviour requestedHidingCamera = active && spot != null ? spot.HidingVirtualCamera : null;
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

                System.Type cameraType = System.Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
                if (cameraType != null)
                {
                    Component[] virtualCameras = Owner.GetComponentsInChildren(cameraType, true);
                    foreach (Component virtualCamera in virtualCameras)
                        DisableBehaviour(virtualCamera as UnityEngine.Behaviour);
                }
            }
        }
        localCameraOverrideApplied = active;
        localCameraOverrideUsesCinemachine = useCinemachine;
    }

    private void ActivateHidingVirtualCamera(UnityEngine.Behaviour hidingCamera, NetworkHidingSpot spot)
    {
        if (hidingCamera == null)
            return;

        activeHidingVirtualCamera = hidingCamera;
        originalHidingVirtualCameraGameObjectActive = hidingCamera.gameObject.activeSelf;
        originalHidingVirtualCameraEnabled = hidingCamera.enabled;
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
        foreach (UnityEngine.Behaviour behaviour in disabledLocalCameraBehaviours)
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
        originalHidingVirtualCameraGameObjectActive = false;
        originalHidingVirtualCameraEnabled = false;
        originalHidingVirtualCameraPriority = 0;
        originalHidingVirtualCameraPriorityCached = false;
        localCameraOverrideApplied = false;
        localCameraOverrideUsesCinemachine = false;
    }

    private void DisableBehaviour(UnityEngine.Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled)
            return;

        behaviour.enabled = false;
        disabledLocalCameraBehaviours.Add(behaviour);
    }

    private static UnityEngine.Behaviour GetCinemachineBrain(GameObject cameraObject)
    {
        System.Type brainType = System.Type.GetType("Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine");
        if (brainType == null || cameraObject == null)
            return null;

        return cameraObject.GetComponent(brainType) as UnityEngine.Behaviour;
    }

    private static void SetCinemachineBrain(GameObject cameraObject, bool enabled)
    {
        UnityEngine.Behaviour brain = GetCinemachineBrain(cameraObject);
        if (brain != null)
            brain.enabled = enabled;
    }

    private static bool TryGetCinemachineCameraPriority(Component virtualCamera, out int priority)
    {
        priority = 0;
        if (virtualCamera == null)
            return false;

        System.Type type = virtualCamera.GetType();
        System.Reflection.PropertyInfo property = type.GetProperty("Priority");
        if (property != null)
        {
            object value = property.GetValue(virtualCamera);
            if (TryExtractPriorityValue(value, out priority))
                return true;
        }

        System.Reflection.FieldInfo field = type.GetField("Priority");
        if (field != null)
        {
            object value = field.GetValue(virtualCamera);
            if (TryExtractPriorityValue(value, out priority))
                return true;
        }

        return false;
    }

    private static void SetCinemachineCameraPriority(Component virtualCamera, int priority)
    {
        if (virtualCamera == null)
            return;

        System.Type type = virtualCamera.GetType();
        bool prioritySet = false;
        System.Reflection.PropertyInfo property = type.GetProperty("Priority");
        if (property != null && TryBuildPriorityValue(property.PropertyType, priority, out object propertyValue))
        {
            property.SetValue(virtualCamera, propertyValue);
            prioritySet = true;
        }

        System.Reflection.FieldInfo field = type.GetField("Priority");
        if (!prioritySet && field != null && TryBuildPriorityValue(field.FieldType, priority, out object fieldValue))
            field.SetValue(virtualCamera, fieldValue);

        System.Reflection.FieldInfo legacyPriority = type.GetField("m_LegacyPriority", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
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

        System.Type type = value.GetType();
        System.Reflection.PropertyInfo valueProperty = type.GetProperty("Value") ?? type.GetProperty("m_Value");
        if (valueProperty != null && valueProperty.GetValue(value) is int propertyValue)
        {
            priority = propertyValue;
            return true;
        }

        System.Reflection.FieldInfo valueField = type.GetField("Value") ?? type.GetField("m_Value");
        if (valueField != null && valueField.GetValue(value) is int fieldValue)
        {
            priority = fieldValue;
            return true;
        }

        return false;
    }

    private static bool TryBuildPriorityValue(System.Type priorityType, int priority, out object value)
    {
        value = null;
        if (priorityType == typeof(int))
        {
            value = priority;
            return true;
        }

        if (priorityType == null || !priorityType.IsValueType)
            return false;

        object boxed = System.Activator.CreateInstance(priorityType);
        System.Reflection.PropertyInfo enabledProperty = priorityType.GetProperty("Enabled") ?? priorityType.GetProperty("m_Enabled");
        System.Reflection.FieldInfo enabledField = priorityType.GetField("Enabled") ?? priorityType.GetField("m_Enabled");
        System.Reflection.PropertyInfo valueProperty = priorityType.GetProperty("Value") ?? priorityType.GetProperty("m_Value");
        System.Reflection.FieldInfo valueField = priorityType.GetField("Value") ?? priorityType.GetField("m_Value");

        if (enabledProperty != null && enabledProperty.CanWrite)
            enabledProperty.SetValue(boxed, true);
        else if (enabledField != null)
            enabledField.SetValue(boxed, true);

        if (valueProperty != null && valueProperty.CanWrite)
            valueProperty.SetValue(boxed, priority);
        else if (valueField != null)
            valueField.SetValue(boxed, priority);
        else
            return false;

        value = boxed;
        return true;
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

    private void TeleportOwner(Vector3 position, Quaternion rotation)
    {
        CharacterController characterController = Owner.GetComponent<CharacterController>();
        bool restoreController = characterController != null && characterController.enabled;
        if (restoreController)
            characterController.enabled = false;

        Owner.transform.SetPositionAndRotation(position, rotation);

        if (restoreController)
            characterController.enabled = true;
    }

    private void SetPhysicalPresence(bool active)
    {
        CharacterController characterController = Owner.GetComponent<CharacterController>();
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

    private float GetTransitionEndTime(float duration)
    {
        float now = Runner != null ? Runner.SimulationTime : Time.time;
        return now + Mathf.Max(0f, duration);
    }

    private bool IsLocalPlayer()
    {
        return playerMovement == null || playerMovement.IsLocalNetworkPlayer;
    }

    private NetworkHidingSpot ResolveSpot(int spotId)
    {
        return NetworkHidingSpot.Find(spotId) ?? localRequestedSpot;
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>() ?? GetComponentInParent<PlayerMovement>();

        if (itemHolder == null)
            itemHolder = GetComponent<NetworkPlayerItemHolder>() ?? GetComponentInParent<NetworkPlayerItemHolder>();

        if (inventory == null)
            inventory = GetComponent<NetworkInventory>() ?? GetComponentInParent<NetworkInventory>();

        if (visualRoot == null)
        {
            Transform visual = FindChildByName(Owner.transform, "Visual");
            visualRoot = visual != null ? visual : ResolveVisualAnimator()?.transform;
        }

        if (visualAnimator == null)
            visualAnimator = ResolveVisualAnimator();
    }

    private Animator ResolveVisualAnimator()
    {
        Transform visual = FindChildByName(Owner.transform, "Visual");
        Animator animator = visual != null ? visual.GetComponentInChildren<Animator>(true) : null;
        return animator != null ? animator : Owner.GetComponentInChildren<Animator>(true);
    }

    private GameObject ResolveOwner()
    {
        PlayerMovement movement = playerMovement != null ? playerMovement : GetComponent<PlayerMovement>() ?? GetComponentInParent<PlayerMovement>();
        return movement != null ? movement.gameObject : gameObject;
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
