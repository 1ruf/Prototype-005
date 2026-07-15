using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using UnityEngine;
using Behaviour = UnityEngine.Behaviour;

[DisallowMultipleComponent]
public sealed class PlayerCameraPresentation : MonoBehaviour
{
    private static PlayerCameraPresentation activeLocalPresentation;

    [Header("Compatibility")]
    [SerializeField] private bool usePlayerMovementSettings = true;

    [Header("Camera")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float normalFOV = 60f;
    [SerializeField] private float sprintFOV = 67f;
    [SerializeField] private float fovTransitionSpeed = 1f;

    private PlayerMovement playerMovement;
    private Behaviour[] localOnlyCameraBehaviours;
    private Component[] localCinemachineCameras;
    private bool localPresentationConfigured;
    private string lastCameraAuthorityLog;

    public bool IsConfigured => localPresentationConfigured;
    public Camera PlayerCamera => Camera.main;

    public void Initialize(
        PlayerMovement movement,
        Camera legacyPlayerCamera,
        float legacyNormalFOV,
        float legacySprintFOV,
        float legacyFovTransitionSpeed)
    {
        playerMovement = movement;

        if (!usePlayerMovementSettings)
            return;

        if (legacyPlayerCamera != null)
            playerCamera = legacyPlayerCamera;

        normalFOV = legacyNormalFOV;
        sprintFOV = legacySprintFOV;
        fovTransitionSpeed = legacyFovTransitionSpeed;
    }

    public void DisableUntilNetworkSpawned()
    {
        if (GetOwnerRoot().GetComponent<NetworkObject>() == null)
            return;

        SetOwnedCamerasInactive();

        Behaviour[] behaviours = FindLocalOnlyCameraBehaviours();
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour != null)
                behaviour.enabled = false;
        }
    }

    public void ConfigureLocalPresentation(bool local)
    {
        SetOwnedCamerasInactive();

        localOnlyCameraBehaviours = FindLocalOnlyCameraBehaviours();
        localCinemachineCameras = FindCinemachineCameras();
        foreach (Behaviour behaviour in localOnlyCameraBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = local;
        }

        if (local)
        {
            activeLocalPresentation = this;
            EnforceSingleLocalCamera();
        }
        else
        {
            activeLocalPresentation?.EnforceSingleLocalCamera();
        }

        localPresentationConfigured = true;

        NetworkObject networkObject = playerMovement != null ? playerMovement.Object : null;
        Debug.Log($"PlayerCameraPresentation: LocalPresentation local={local}, localPlayer={playerMovement?.Runner?.LocalPlayer}, inputAuthority={networkObject?.InputAuthority}, object={name}.");
    }

    public void EnsureLocalPresentation(bool local)
    {
        if (!localPresentationConfigured)
            ConfigureLocalPresentation(local);
    }

    public void RefreshLocalCameraPresentation()
    {
        if (playerMovement == null || !playerMovement.IsLocalNetworkPlayer)
            return;

        activeLocalPresentation = this;
        EnforceSingleLocalCamera();
    }

    public void TickLocalPresentation(bool local, bool inputBlocked, bool sprinting)
    {
        if (!local || inputBlocked)
            return;

        EnforceSingleLocalCamera();
        ApplySprintFOV(sprinting);
    }

    public void ApplySprintFOV(bool sprinting)
    {
        float targetFOV = sprinting ? sprintFOV : normalFOV;
        float currentFOV = GetCurrentLocalFOV();
        float nextFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime * fovTransitionSpeed);

        Camera targetCamera = Camera.main;
        if (targetCamera != null)
            targetCamera.fieldOfView = nextFOV;

        ApplyCinemachineFOV(nextFOV);
    }

    public void HandleOwnerDisabled()
    {
        localPresentationConfigured = false;
        if (activeLocalPresentation == this)
            activeLocalPresentation = null;
    }

    public void HandleNetworkDespawned()
    {
        HandleOwnerDisabled();
        DisableUntilNetworkSpawned();
    }

    private void EnforceSingleLocalCamera()
    {
        Camera sceneCamera = Camera.main != null ? Camera.main : FindSceneMainCamera();
        if (sceneCamera == null)
            return;

        sceneCamera.enabled = true;
        SetCameraTag(sceneCamera, true);

        AudioListener sceneListener = sceneCamera.GetComponent<AudioListener>();
        if (sceneListener != null)
            sceneListener.enabled = true;

        SetCinemachineBrain(sceneCamera.gameObject, true);
        int disabledPlayerCameras = DisableAllPlayerCameraComponents();
        int disabledRemoteVirtualCameras = SetPlayerVirtualCamerasForLocalOwner();

        NetworkObject networkObject = playerMovement != null ? playerMovement.Object : null;
        string cameraAuthorityLog = $"owner={name}, sceneCamera={GetHierarchyPath(sceneCamera.transform)}, localPlayer={playerMovement?.Runner?.LocalPlayer}, inputAuthority={networkObject?.InputAuthority}, disabledPlayerCameras={disabledPlayerCameras}, disabledRemoteVirtualCameras={disabledRemoteVirtualCameras}";
        if (lastCameraAuthorityLog == cameraAuthorityLog)
            return;

        lastCameraAuthorityLog = cameraAuthorityLog;
        Debug.Log($"PlayerCameraPresentation: CameraAuthority {cameraAuthorityLog}.");
    }

    private static Camera FindSceneMainCamera()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera != null && camera.name == "Main Camera" && camera.GetComponentInParent<PlayerMovement>() == null)
                return camera;
        }

        foreach (Camera camera in cameras)
        {
            if (camera != null && camera.GetComponentInParent<PlayerMovement>() == null)
                return camera;
        }

        return null;
    }

    private void SetOwnedCamerasInactive()
    {
        Camera[] cameras = GetOwnerRoot().GetComponentsInChildren<Camera>(true);
        foreach (Camera camera in cameras)
        {
            if (camera == null)
                continue;

            camera.enabled = false;
            SetCameraTag(camera, false);

            AudioListener[] listeners = camera.GetComponents<AudioListener>();
            foreach (AudioListener listener in listeners)
            {
                if (listener != null)
                    listener.enabled = false;
            }

            SetCinemachineBrain(camera.gameObject, false);
        }
    }

    private static int DisableAllPlayerCameraComponents()
    {
        int disabledCameras = 0;
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera == null || camera.GetComponentInParent<PlayerMovement>() == null)
                continue;

            if (camera.enabled)
                disabledCameras++;

            camera.enabled = false;
            SetCameraTag(camera, false);

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;

            SetCinemachineBrain(camera.gameObject, false);
        }

        return disabledCameras;
    }

    private static int SetPlayerVirtualCamerasForLocalOwner()
    {
        int disabledRemoteVirtualCameras = 0;
        Type cameraType = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
        if (cameraType == null)
            return 0;

        UnityEngine.Object[] virtualCameras = FindObjectsByType(cameraType, FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (UnityEngine.Object virtualCameraObject in virtualCameras)
        {
            Component virtualCamera = virtualCameraObject as Component;
            if (virtualCamera == null)
                continue;

            PlayerMovement owner = virtualCamera.GetComponentInParent<PlayerMovement>();
            if (owner == null)
                continue;

            PlayerCameraPresentation presentation = owner.GetComponentInChildren<PlayerCameraPresentation>(true);
            bool shouldEnable = presentation == activeLocalPresentation;
            if (virtualCamera is Behaviour behaviour)
            {
                if (behaviour.enabled && !shouldEnable)
                    disabledRemoteVirtualCameras++;

                behaviour.enabled = shouldEnable;
            }
        }

        return disabledRemoteVirtualCameras;
    }

    private static void SetCameraTag(Camera camera, bool mainCamera)
    {
        if (camera == null)
            return;

        if (mainCamera)
        {
            camera.tag = "MainCamera";
            return;
        }

        if (camera.CompareTag("MainCamera"))
            camera.tag = "Untagged";
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void SetCinemachineBrain(GameObject cameraObject, bool enabled)
    {
        Type brainType = Type.GetType("Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine");
        if (brainType == null || cameraObject == null)
            return;

        Component brain = cameraObject.GetComponent(brainType);
        if (brain is Behaviour behaviour)
            behaviour.enabled = enabled;
    }

    private float GetCurrentLocalFOV()
    {
        if (localCinemachineCameras != null)
        {
            foreach (Component virtualCamera in localCinemachineCameras)
            {
                if (TryGetCinemachineFOV(virtualCamera, out float fov))
                    return fov;
            }
        }

        Camera targetCamera = Camera.main;
        if (targetCamera != null)
            return targetCamera.fieldOfView;

        if (playerCamera != null)
            return playerCamera.fieldOfView;

        return normalFOV;
    }

    private void ApplyCinemachineFOV(float fov)
    {
        if (localCinemachineCameras == null || localCinemachineCameras.Length == 0)
            localCinemachineCameras = FindCinemachineCameras();

        if (localCinemachineCameras == null)
            return;

        foreach (Component virtualCamera in localCinemachineCameras)
            TrySetCinemachineFOV(virtualCamera, fov);
    }

    private Component[] FindCinemachineCameras()
    {
        Type cameraType = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
        if (cameraType == null)
            return Array.Empty<Component>();

        Component[] components = GetOwnerRoot().GetComponentsInChildren(cameraType, true);
        return components ?? Array.Empty<Component>();
    }

    private static bool TryGetCinemachineFOV(Component virtualCamera, out float fov)
    {
        fov = 0f;
        if (virtualCamera == null)
            return false;

        if (!TryGetCinemachineLens(virtualCamera, out object lens, out _, out _))
            return false;

        FieldInfo fieldOfView = lens.GetType().GetField("FieldOfView");
        if (fieldOfView == null || fieldOfView.GetValue(lens) is not float value)
            return false;

        fov = value;
        return true;
    }

    private static bool TrySetCinemachineFOV(Component virtualCamera, float fov)
    {
        if (virtualCamera == null)
            return false;

        if (!TryGetCinemachineLens(virtualCamera, out object lens, out PropertyInfo lensProperty, out FieldInfo lensField))
            return false;

        FieldInfo fieldOfView = lens.GetType().GetField("FieldOfView");
        if (fieldOfView == null)
            return false;

        fieldOfView.SetValue(lens, fov);

        if (lensProperty != null)
        {
            lensProperty.SetValue(virtualCamera, lens);
            return true;
        }

        if (lensField != null)
        {
            lensField.SetValue(virtualCamera, lens);
            return true;
        }

        return false;
    }

    private static bool TryGetCinemachineLens(Component virtualCamera, out object lens, out PropertyInfo lensProperty, out FieldInfo lensField)
    {
        lens = null;
        lensProperty = null;
        lensField = null;

        if (virtualCamera == null)
            return false;

        Type type = virtualCamera.GetType();
        lensProperty = type.GetProperty("Lens");
        if (lensProperty != null)
        {
            lens = lensProperty.GetValue(virtualCamera);
            return lens != null;
        }

        lensField = type.GetField("Lens");
        if (lensField == null)
            return false;

        lens = lensField.GetValue(virtualCamera);
        return lens != null;
    }

    private Behaviour[] FindLocalOnlyCameraBehaviours()
    {
        var behaviours = new List<Behaviour>();
        AddBehavioursByTypeName("Unity.Cinemachine.CinemachineCamera", behaviours);
        AddBehavioursByTypeName("Unity.Cinemachine.CinemachineBrain", behaviours);
        AddBehavioursByTypeName(nameof(MouseLookSystem), behaviours);
        AddBehavioursByTypeName(nameof(CameraBobbingController), behaviours);
        AddBehavioursByTypeName(nameof(CameraShakeController), behaviours);
        AddBehavioursByTypeName("FirstPersonEquipmentAim", behaviours);
        return behaviours.ToArray();
    }

    private void AddBehavioursByTypeName(string typeName, List<Behaviour> behaviours)
    {
        Type type = Type.GetType(typeName + ", Unity.Cinemachine") ??
                    Type.GetType(typeName + ", Assembly-CSharp");
        if (type == null)
            return;

        Component[] components = GetOwnerRoot().GetComponentsInChildren(type, true);
        foreach (Component component in components)
        {
            if (component is Behaviour behaviour)
                behaviours.Add(behaviour);
        }
    }

    private Transform GetOwnerRoot()
    {
        return playerMovement != null ? playerMovement.transform : transform.root;
    }
}
