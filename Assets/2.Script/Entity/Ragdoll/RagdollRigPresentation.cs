using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Owns ragdoll rig discovery, physics-part activation, death presentation, and proxy pose application.
/// It remains a plain MonoBehaviour; network state is supplied by RagdollEntityComponent.
/// </summary>
[DisallowMultipleComponent]
public sealed class RagdollRigPresentation : MonoBehaviour
{
    private static readonly RagdollPartComponent[] EmptyParts = System.Array.Empty<RagdollPartComponent>();

    private GameObject entityRoot;
    private RagdollEntityComponent coordinator;
    private RagdollPartComponent[] parts;
    private Animator[] animators;
    private CharacterController characterController;
    private NetworkCharacterController networkController;
    private UnityEngine.Behaviour[] disableOnDeath;
    private NetworkPlayerVisualPose[] visualPoseDrivers;
    private Renderer[] visualRenderers;
    private bool autoDisableNonRagdollBehaviours;
    private bool hideNonVisualRenderersOnDeath;
    private DeadCameraController deadCamera;

    private Transform visualRoot;
    private UnityEngine.Behaviour[] autoDisableOnDeath;
    private Renderer[] nonVisualRenderers;

    public RagdollPartComponent[] Parts => parts ?? EmptyParts;

    public void Configure(
        GameObject root,
        RagdollEntityComponent ownerCoordinator,
        RagdollPartComponent[] configuredParts,
        Animator[] configuredAnimators,
        CharacterController configuredCharacterController,
        NetworkCharacterController configuredNetworkController,
        UnityEngine.Behaviour[] configuredDisableOnDeath,
        NetworkPlayerVisualPose[] configuredVisualPoseDrivers,
        Renderer[] configuredVisualRenderers,
        bool shouldAutoDisableNonRagdollBehaviours,
        bool shouldHideNonVisualRenderersOnDeath,
        DeadCameraController configuredDeadCamera)
    {
        entityRoot = root != null ? root : gameObject;
        coordinator = ownerCoordinator;

        if (configuredParts != null && configuredParts.Length > 0)
            parts = configuredParts;
        if (configuredAnimators != null && configuredAnimators.Length > 0)
            animators = configuredAnimators;
        if (configuredCharacterController != null)
            characterController = configuredCharacterController;
        if (configuredNetworkController != null)
            networkController = configuredNetworkController;
        if (configuredDisableOnDeath != null && configuredDisableOnDeath.Length > 0)
            disableOnDeath = configuredDisableOnDeath;
        if (configuredVisualPoseDrivers != null && configuredVisualPoseDrivers.Length > 0)
            visualPoseDrivers = configuredVisualPoseDrivers;
        if (configuredVisualRenderers != null && configuredVisualRenderers.Length > 0)
            visualRenderers = configuredVisualRenderers;
        if (configuredDeadCamera != null)
            deadCamera = configuredDeadCamera;

        autoDisableNonRagdollBehaviours = shouldAutoDisableNonRagdollBehaviours;
        hideNonVisualRenderersOnDeath = shouldHideNonVisualRenderersOnDeath;
        ResolveReferences();
    }

    public void ApplyState(bool dead, bool ragdollEnabled, bool applyPresentation)
    {
        ResolveReferences();

        foreach (RagdollPartComponent part in Parts)
        {
            if (part != null)
                part.SetRagdollActive(ragdollEnabled);
        }

        if (applyPresentation)
        {
            if (dead && ragdollEnabled)
            {
                SetVisualActive(true);
                SetVisualRenderersVisible(true);
            }

            SetBehavioursEnabled(animators, !dead);
            SetBehavioursEnabled(disableOnDeath, !dead);
            SetBehavioursEnabled(visualPoseDrivers, !dead);

            if (autoDisableNonRagdollBehaviours)
                SetBehavioursEnabled(autoDisableOnDeath, !dead);

            if (hideNonVisualRenderersOnDeath)
                SetRenderersVisible(nonVisualRenderers, !dead);

            if (deadCamera != null)
                deadCamera.SetDeadCameraActive(dead && ragdollEnabled);
        }

        if (characterController != null)
            characterController.enabled = !dead;

        if (networkController != null)
            networkController.enabled = !dead;
    }

    public void ResetVelocity()
    {
        ResolveReferences();

        foreach (RagdollPartComponent part in Parts)
        {
            Rigidbody body = part != null ? part.Rigidbody : null;
            if (body == null || body.isKinematic)
                continue;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    public void ApplyImpulse(Vector3 impulse)
    {
        ResolveReferences();

        foreach (RagdollPartComponent part in Parts)
        {
            Rigidbody body = part != null ? part.Rigidbody : null;
            if (body == null)
                continue;

            if (body.isKinematic)
                body.isKinematic = false;

            body.AddForce(impulse, ForceMode.Impulse);
        }
    }

    public int CapturePose(
        NetworkArray<Vector3> positions,
        NetworkArray<Quaternion> rotations,
        int maximumPartCount)
    {
        ResolveReferences();
        int count = Mathf.Min(Parts.Length, maximumPartCount);

        for (int i = 0; i < count; i++)
        {
            RagdollPartComponent part = Parts[i];
            if (part == null)
                continue;

            Transform partTransform = part.transform;
            positions.Set(i, partTransform.position);
            rotations.Set(i, partTransform.rotation);
        }

        return count;
    }

    public void ApplyProxyPose(
        int syncedPartCount,
        int maximumPartCount,
        NetworkArray<Vector3> positions,
        NetworkArray<Quaternion> rotations,
        float followSpeed,
        float deltaTime)
    {
        ResolveReferences();

        int count = Mathf.Min(Mathf.Min(Parts.Length, syncedPartCount), maximumPartCount);
        float follow = 1f - Mathf.Exp(-followSpeed * Mathf.Max(0f, deltaTime));

        for (int i = 0; i < count; i++)
        {
            RagdollPartComponent part = Parts[i];
            if (part == null)
                continue;

            Rigidbody body = part.Rigidbody;
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                body.detectCollisions = false;
            }

            Transform partTransform = part.transform;
            partTransform.SetPositionAndRotation(
                Vector3.Lerp(partTransform.position, positions[i], follow),
                Quaternion.Slerp(partTransform.rotation, rotations[i], follow));
        }
    }

    private void ResolveReferences()
    {
        GameObject root = entityRoot != null ? entityRoot : gameObject;

        if (parts == null || parts.Length == 0)
            parts = root.GetComponentsInChildren<RagdollPartComponent>(true);

        if (parts == null || parts.Length == 0)
            parts = CreateMissingPartComponents(root);

        if (animators == null || animators.Length == 0)
            animators = root.GetComponentsInChildren<Animator>(true);

        if (characterController == null)
            characterController = root.GetComponent<CharacterController>();

        if (networkController == null)
            networkController = root.GetComponent<NetworkCharacterController>();

        if (disableOnDeath == null || disableOnDeath.Length == 0)
        {
            disableOnDeath = new UnityEngine.Behaviour[]
            {
                root.GetComponent<PlayerMovement>(),
                root.GetComponentInChildren<MouseLookSystem>(true),
                root.GetComponentInChildren<CameraBobbingController>(true)
            };
        }

        if (visualPoseDrivers == null || visualPoseDrivers.Length == 0)
            visualPoseDrivers = root.GetComponentsInChildren<NetworkPlayerVisualPose>(true);

        if (visualRoot == null)
            visualRoot = FindChildByName(root.transform, "Visual");

        if (visualRenderers == null || visualRenderers.Length == 0)
        {
            visualRenderers = visualRoot != null
                ? visualRoot.GetComponentsInChildren<Renderer>(true)
                : root.GetComponentsInChildren<Renderer>(true);
        }

        if (deadCamera == null)
            deadCamera = root.GetComponentInChildren<DeadCameraController>(true);

        if (autoDisableOnDeath == null || autoDisableOnDeath.Length == 0)
            autoDisableOnDeath = CreateAutoDisableBehaviourList(root);

        if (nonVisualRenderers == null || nonVisualRenderers.Length == 0)
            nonVisualRenderers = CreateNonVisualRendererList(root);
    }

    private UnityEngine.Behaviour[] CreateAutoDisableBehaviourList(GameObject root)
    {
        UnityEngine.Behaviour[] behaviours = root.GetComponentsInChildren<UnityEngine.Behaviour>(true);
        var filtered = new List<UnityEngine.Behaviour>(behaviours.Length);

        foreach (UnityEngine.Behaviour behaviour in behaviours)
        {
            if (behaviour == null || IsProtectedDeathBehaviour(behaviour))
                continue;

            filtered.Add(behaviour);
        }

        return filtered.ToArray();
    }

    private bool IsProtectedDeathBehaviour(UnityEngine.Behaviour behaviour)
    {
        return behaviour == this
            || behaviour == coordinator
            || behaviour is RagdollEntityComponent
            || behaviour is RagdollRigPresentation
            || behaviour is RagdollSurfaceBloodController
            || behaviour is RagdollPartComponent
            || behaviour is NetworkHealthComponent
            || behaviour is NetworkDeathComponent
            || behaviour is BloodSplatterComponent
            || behaviour is DeadCameraController
            || IsVisualMainCameraBehaviour(behaviour);
    }

    private bool IsVisualMainCameraBehaviour(UnityEngine.Behaviour behaviour)
    {
        if (behaviour == null || behaviour.transform == null || !IsUnderVisualRoot(behaviour.transform))
            return false;

        return behaviour.GetComponent<Camera>() != null && behaviour.gameObject.name == "Main Camera";
    }

    private Renderer[] CreateNonVisualRendererList(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        var filtered = new List<Renderer>(renderers.Length);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || IsUnderVisualRoot(renderer.transform))
                continue;

            filtered.Add(renderer);
        }

        return filtered.ToArray();
    }

    private bool IsUnderVisualRoot(Transform candidate)
    {
        return visualRoot != null && candidate != null
            && (candidate == visualRoot || candidate.IsChildOf(visualRoot));
    }

    private void SetVisualActive(bool active)
    {
        if (visualRoot != null && visualRoot.gameObject.activeSelf != active)
            visualRoot.gameObject.SetActive(active);
    }

    private void SetVisualRenderersVisible(bool visible)
    {
        ResolveReferences();
        SetRenderersVisible(visualRenderers, visible);
    }

    private static void SetBehavioursEnabled<T>(T[] behaviours, bool enabled) where T : UnityEngine.Behaviour
    {
        if (behaviours == null)
            return;

        foreach (T behaviour in behaviours)
        {
            if (behaviour != null)
                behaviour.enabled = enabled;
        }
    }

    private static void SetRenderersVisible(Renderer[] renderers, bool visible)
    {
        if (renderers == null)
            return;

        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer != null)
                targetRenderer.enabled = visible;
        }
    }

    private RagdollPartComponent[] CreateMissingPartComponents(GameObject root)
    {
        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        var createdParts = new List<RagdollPartComponent>(rigidbodies.Length);
        Transform coordinatorTransform = coordinator != null ? coordinator.transform : transform;

        foreach (Rigidbody body in rigidbodies)
        {
            if (body == null || body.transform == coordinatorTransform)
                continue;

            RagdollPartComponent part = body.GetComponent<RagdollPartComponent>();
            if (part == null)
                part = body.gameObject.AddComponent<RagdollPartComponent>();

            part.CacheComponents();
            createdParts.Add(part);
        }

        return createdParts.ToArray();
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
