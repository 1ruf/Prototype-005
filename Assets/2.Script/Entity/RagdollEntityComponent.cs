using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class RagdollEntityComponent : NetworkBehaviour, INetworkEntityComponent, IKnockbackable
{
    private const int MaxSyncedRagdollParts = 32;
    private const float ProxyPoseFollowSpeed = 30f;

    [SerializeField] private RagdollPartComponent[] parts;
    [SerializeField] private Animator[] animators;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private NetworkCharacterController networkController;
    [SerializeField] private UnityEngine.Behaviour[] disableOnDeath;
    [SerializeField] private NetworkPlayerVisualPose[] visualPoseDrivers;
    [SerializeField] private Renderer[] visualRenderers;
    [SerializeField] private bool autoDisableNonRagdollBehaviours = true;
    [SerializeField] private bool hideNonVisualRenderersOnDeath = true;
    [SerializeField] private DeadCameraController deadCamera;

    [Networked] public NetworkBool IsDead { get; private set; }
    [Networked] public NetworkBool IsRagdollEnabled { get; private set; }
    [Networked] private Vector3 KnockbackImpulse { get; set; }
    [Networked] private int KnockbackSequence { get; set; }
    [Networked] private int SyncedRagdollPartCount { get; set; }
    [Networked, Capacity(MaxSyncedRagdollParts)] private NetworkArray<Vector3> SyncedRagdollPartPositions => default;
    [Networked, Capacity(MaxSyncedRagdollParts)] private NetworkArray<Quaternion> SyncedRagdollPartRotations => default;

    private GameObject owner;
    private bool appliedDeadState;
    private bool lastDeadState;
    private bool lastRagdollState;
    private int lastAppliedKnockbackSequence;
    private Transform visualRoot;
    private UnityEngine.Behaviour[] autoDisableOnDeath;
    private Renderer[] nonVisualRenderers;

    public GameObject Owner => owner != null ? owner : gameObject;

    private void Awake()
    {
        Initialize(ResolveOwner());
        ApplyState(false, false, true);
    }

    public override void Spawned()
    {
        Initialize(ResolveOwner());
        ApplyState(IsDead, IsRagdollEnabled, true);
    }

    public override void Render()
    {
        if (!appliedDeadState || lastDeadState != IsDead || lastRagdollState != IsRagdollEnabled)
            ApplyState(IsDead, IsRagdollEnabled, true);

        ApplyNetworkedKnockbackIfNeeded();
        ApplyNetworkedRagdollPoseIfNeeded(Time.deltaTime);
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.HasStateAuthority || !IsRagdollEnabled)
            return;

        CaptureNetworkedRagdollPose();
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner;
        EnsureReferences();
    }

    public void Kill()
    {
        if (Object == null || Object.HasStateAuthority)
        {
            SetDeadState(true, true);
            return;
        }

        RPC_RequestDeath();
    }

    public void Revive()
    {
        if (Object == null || Object.HasStateAuthority)
        {
            SetDeadState(false, false);
            return;
        }

        RPC_RequestRevive();
    }

    public void ApplyKnockbackFrom(Vector3 sourcePosition, float force, float upwardForce)
    {
        Vector3 direction = Owner.transform.position - sourcePosition;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Owner.transform.forward;

        ApplyKnockback(direction, force, upwardForce);
    }

    public void ApplyKnockback(Vector3 direction, float force, float upwardForce)
    {
        if (direction.sqrMagnitude <= 0.0001f || force <= 0f)
            return;

        if (Object == null || Object.HasStateAuthority)
        {
            ApplyKnockbackInternal(direction, force, upwardForce);
            return;
        }

        RPC_RequestKnockback(direction, force, upwardForce);
    }

    public void ResetRagdollVelocity()
    {
        EnsureReferences();

        foreach (RagdollPartComponent part in parts)
        {
            Rigidbody body = part != null ? part.Rigidbody : null;
            if (body == null || body.isKinematic)
                continue;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDeath()
    {
        SetDeadState(true, true);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRevive()
    {
        SetDeadState(false, false);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestKnockback(Vector3 direction, float force, float upwardForce)
    {
        ApplyKnockbackInternal(direction, force, upwardForce);
    }

    private void SetDeadState(bool dead, bool ragdollEnabled)
    {
        IsDead = dead;
        IsRagdollEnabled = ragdollEnabled;
        ApplyState(dead, ragdollEnabled, true);
    }

    private void ApplyState(bool dead, bool ragdollEnabled, bool applyPresentation)
    {
        EnsureReferences();

        foreach (RagdollPartComponent part in parts)
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

            foreach (Animator animator in animators)
            {
                if (animator != null)
                    animator.enabled = !dead;
            }

            foreach (UnityEngine.Behaviour behaviour in disableOnDeath)
            {
                if (behaviour != null)
                    behaviour.enabled = !dead;
            }

            foreach (NetworkPlayerVisualPose visualPoseDriver in visualPoseDrivers)
            {
                if (visualPoseDriver != null)
                    visualPoseDriver.enabled = !dead;
            }

            if (autoDisableNonRagdollBehaviours)
                SetAutoDisableBehavioursEnabled(!dead);

            if (hideNonVisualRenderersOnDeath)
                SetNonVisualRenderersVisible(!dead);

            if (deadCamera != null)
                deadCamera.SetDeadCameraActive(dead && ragdollEnabled);
        }

        if (characterController != null)
            characterController.enabled = !dead;

        if (networkController != null)
            networkController.enabled = !dead;

        appliedDeadState = true;
        lastDeadState = dead;
        lastRagdollState = ragdollEnabled;
    }

    private void ApplyKnockbackInternal(Vector3 direction, float force, float upwardForce)
    {
        Vector3 flatDirection = direction;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude <= 0.0001f)
            flatDirection = Owner.transform.forward;

        Vector3 impulse = flatDirection.normalized * force + Vector3.up * Mathf.Max(0f, upwardForce);

        if (!IsDead || !IsRagdollEnabled)
            SetDeadState(true, true);

        KnockbackImpulse = impulse;
        KnockbackSequence++;
        ApplyImpulseToParts(impulse);
        lastAppliedKnockbackSequence = KnockbackSequence;
    }

    private void ApplyNetworkedKnockbackIfNeeded()
    {
        if (KnockbackSequence == 0 || lastAppliedKnockbackSequence == KnockbackSequence)
            return;

        if (Object != null && Object.HasStateAuthority)
        {
            lastAppliedKnockbackSequence = KnockbackSequence;
            return;
        }

        ApplyImpulseToParts(KnockbackImpulse);
        lastAppliedKnockbackSequence = KnockbackSequence;
    }

    private void CaptureNetworkedRagdollPose()
    {
        EnsureReferences();

        int count = Mathf.Min(parts != null ? parts.Length : 0, MaxSyncedRagdollParts);
        SyncedRagdollPartCount = count;

        for (int i = 0; i < count; i++)
        {
            if (parts[i] == null)
                continue;

            Transform partTransform = parts[i].transform;
            SyncedRagdollPartPositions.Set(i, partTransform.position);
            SyncedRagdollPartRotations.Set(i, partTransform.rotation);
        }
    }

    private void ApplyNetworkedRagdollPoseIfNeeded(float deltaTime)
    {
        if (Object == null || Object.HasStateAuthority || !IsRagdollEnabled || SyncedRagdollPartCount <= 0)
            return;

        EnsureReferences();

        int count = Mathf.Min(parts != null ? parts.Length : 0, SyncedRagdollPartCount, MaxSyncedRagdollParts);
        float follow = 1f - Mathf.Exp(-ProxyPoseFollowSpeed * Mathf.Max(0f, deltaTime));

        for (int i = 0; i < count; i++)
        {
            RagdollPartComponent part = parts[i];
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
            Vector3 targetPosition = SyncedRagdollPartPositions[i];
            Quaternion targetRotation = SyncedRagdollPartRotations[i];
            partTransform.SetPositionAndRotation(
                Vector3.Lerp(partTransform.position, targetPosition, follow),
                Quaternion.Slerp(partTransform.rotation, targetRotation, follow));
        }
    }

    private void ApplyImpulseToParts(Vector3 impulse)
    {
        EnsureReferences();

        foreach (RagdollPartComponent part in parts)
        {
            Rigidbody body = part != null ? part.Rigidbody : null;
            if (body == null)
                continue;

            if (body.isKinematic)
                body.isKinematic = false;

            body.AddForce(impulse, ForceMode.Impulse);
        }
    }

    private void EnsureReferences()
    {
        GameObject root = ResolveOwner();

        if (parts == null || parts.Length == 0)
            parts = root.GetComponentsInChildren<RagdollPartComponent>(true);

        if (parts == null || parts.Length == 0)
            parts = CreateMissingPartComponents();

        if (animators == null || animators.Length == 0)
            animators = root.GetComponentsInChildren<Animator>(true);

        if (characterController == null)
            characterController = root.GetComponent<CharacterController>();

        if (networkController == null)
            networkController = root.GetComponent<NetworkCharacterController>();

        if (disableOnDeath == null || disableOnDeath.Length == 0)
            disableOnDeath = new UnityEngine.Behaviour[]
            {
                root.GetComponent<PlayerMovement>(),
                root.GetComponentInChildren<MouseLookSystem>(true),
                root.GetComponentInChildren<CameraBobbingController>(true)
            };

        if (visualPoseDrivers == null || visualPoseDrivers.Length == 0)
            visualPoseDrivers = root.GetComponentsInChildren<NetworkPlayerVisualPose>(true);

        if (visualRenderers == null || visualRenderers.Length == 0)
        {
            visualRoot = FindChildByName(root.transform, "Visual");
            visualRenderers = visualRoot != null ? visualRoot.GetComponentsInChildren<Renderer>(true) : root.GetComponentsInChildren<Renderer>(true);
        }

        if (visualRoot == null)
            visualRoot = FindChildByName(root.transform, "Visual");

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
        var filtered = new System.Collections.Generic.List<UnityEngine.Behaviour>(behaviours.Length);

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
            || behaviour is RagdollEntityComponent
            || behaviour is RagdollPartComponent
            || behaviour is NetworkHealthComponent
            || behaviour is NetworkDeathComponent
            || behaviour is DeadCameraController
            || IsVisualMainCameraBehaviour(behaviour);
    }

    private bool IsVisualMainCameraBehaviour(UnityEngine.Behaviour behaviour)
    {
        if (behaviour == null || behaviour.transform == null)
            return false;

        if (!IsUnderVisualRoot(behaviour.transform))
            return false;

        return behaviour.GetComponent<Camera>() != null && behaviour.gameObject.name == "Main Camera";
    }

    private Renderer[] CreateNonVisualRendererList(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        var filtered = new System.Collections.Generic.List<Renderer>(renderers.Length);

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
        return visualRoot != null && candidate != null && (candidate == visualRoot || candidate.IsChildOf(visualRoot));
    }

    private void SetVisualActive(bool active)
    {
        if (visualRoot != null && visualRoot.gameObject.activeSelf != active)
            visualRoot.gameObject.SetActive(active);
    }

    private void SetAutoDisableBehavioursEnabled(bool enabled)
    {
        EnsureReferences();

        if (autoDisableOnDeath == null)
            return;

        foreach (UnityEngine.Behaviour behaviour in autoDisableOnDeath)
        {
            if (behaviour != null)
                behaviour.enabled = enabled;
        }
    }

    private void SetNonVisualRenderersVisible(bool visible)
    {
        EnsureReferences();

        if (nonVisualRenderers == null)
            return;

        foreach (Renderer nonVisualRenderer in nonVisualRenderers)
        {
            if (nonVisualRenderer != null)
                nonVisualRenderer.enabled = visible;
        }
    }

    private void SetVisualRenderersVisible(bool visible)
    {
        EnsureReferences();

        if (visualRenderers == null)
            return;

        foreach (Renderer visualRenderer in visualRenderers)
        {
            if (visualRenderer != null)
                visualRenderer.enabled = visible;
        }
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

    private RagdollPartComponent[] CreateMissingPartComponents()
    {
        Rigidbody[] rigidbodies = ResolveOwner().GetComponentsInChildren<Rigidbody>(true);
        var createdParts = new System.Collections.Generic.List<RagdollPartComponent>(rigidbodies.Length);

        foreach (Rigidbody body in rigidbodies)
        {
            if (body == null || body.transform == transform)
                continue;

            RagdollPartComponent part = body.GetComponent<RagdollPartComponent>();
            if (part == null)
                part = body.gameObject.AddComponent<RagdollPartComponent>();

            part.CacheComponents();
            createdParts.Add(part);
        }

        return createdParts.ToArray();
    }

    private GameObject ResolveOwner()
    {
        if (owner != null)
            return owner;

        PlayerMovement player = GetComponentInParent<PlayerMovement>();
        return player != null ? player.gameObject : gameObject;
    }
}
