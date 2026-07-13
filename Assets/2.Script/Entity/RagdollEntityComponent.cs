using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class RagdollEntityComponent : NetworkBehaviour, INetworkEntityComponent, IKnockbackable
{
    private const int MaxSyncedRagdollParts = 32;
    private const float ProxyPoseFollowSpeed = 30f;

    // Legacy serialized configuration stays on the coordinator so existing prefabs retain data.
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
    [SerializeField] private BloodSplatterComponent bloodSplatter;
    [SerializeField] private int bloodSplatterCountOnDeath = 14;
    [SerializeField] private int bloodSplatterCountOnSurfaceContact = 1;
    [SerializeField] private float bloodSplatterSurfaceReentryDelay = 0.35f;
    [SerializeField] private float bloodSplatterSurfaceContactProbeRadius = 0.18f;
    [SerializeField] private LayerMask bloodSplatterSurfaceMask = (1 << 7) | (1 << 8);
    [SerializeField, Range(5f, 30f)] private float ragdollPoseSyncRate = 20f;

    // Do not reorder: this is the established Fusion state layout for the prefab.
    [Networked] public NetworkBool IsDead { get; private set; }
    [Networked] public NetworkBool IsRagdollEnabled { get; private set; }
    [Networked] private Vector3 KnockbackImpulse { get; set; }
    [Networked] private int KnockbackSequence { get; set; }
    [Networked] private int BloodSplatterSequence { get; set; }
    [Networked] private int BloodSplatterCount { get; set; }
    [Networked] private Vector3 BloodSplatterPosition { get; set; }
    [Networked] private Vector3 BloodSplatterDirection { get; set; }
    [Networked] private int SyncedRagdollPartCount { get; set; }
    [Networked, Capacity(MaxSyncedRagdollParts)] private NetworkArray<Vector3> SyncedRagdollPartPositions => default;
    [Networked, Capacity(MaxSyncedRagdollParts)] private NetworkArray<Quaternion> SyncedRagdollPartRotations => default;

    private GameObject owner;
    private bool appliedDeadState;
    private bool lastDeadState;
    private bool lastRagdollState;
    private int lastAppliedKnockbackSequence;
    private bool bloodSplatterSpawnedForDeath;
    private int lastAppliedBloodSplatterSequence;
    private RagdollRigPresentation rigPresentation;
    private RagdollSurfaceBloodController surfaceBloodController;
    private float nextPoseSyncTime;

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
        ApplyNetworkedBloodSplatterIfNeeded();
        ApplyNetworkedRagdollPoseIfNeeded(Time.deltaTime);
        DetectRagdollSurfaceContactsFromParts();
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.HasStateAuthority || !IsRagdollEnabled)
            return;

        float now = Runner != null ? Runner.SimulationTime : Time.time;
        if (now < nextPoseSyncTime)
            return;

        nextPoseSyncTime = now + 1f / Mathf.Max(5f, ragdollPoseSyncRate);
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
        rigPresentation.ResetVelocity();
    }

    public void RequestBloodSplatter(int count)
    {
        if (count <= 0)
            return;

        if (Object == null || Object.HasStateAuthority)
        {
            PublishBloodSplatterEvent(count);
            return;
        }

        RPC_RequestBloodSplatter(count);
    }

    public void NotifyRagdollSurfaceContact(
        RagdollPartComponent part,
        Vector3 contactPoint,
        Vector3 contactNormal,
        int surfaceLayer)
    {
        EnsureReferences();
        surfaceBloodController.NotifySurfaceContact(part, contactPoint, contactNormal, surfaceLayer);
    }

    public void NotifyRagdollSurfaceExit(RagdollPartComponent part, int surfaceLayer)
    {
        EnsureReferences();
        surfaceBloodController.NotifySurfaceExit(part, surfaceLayer);
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

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestBloodSplatter(int count)
    {
        PublishBloodSplatterEvent(count);
    }

    private void SetDeadState(bool dead, bool ragdollEnabled)
    {
        IsDead = dead;
        IsRagdollEnabled = ragdollEnabled;
        ApplyState(dead, ragdollEnabled, true);

        if (dead && ragdollEnabled)
            PublishBloodSplatter(bloodSplatterCountOnDeath);
    }

    private void ApplyState(bool dead, bool ragdollEnabled, bool applyPresentation)
    {
        EnsureReferences();
        rigPresentation.ApplyState(dead, ragdollEnabled, applyPresentation);

        if (applyPresentation && !dead)
        {
            bloodSplatterSpawnedForDeath = false;
            surfaceBloodController.ResetSurfaceState();
        }

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
        PublishBloodSplatter(bloodSplatterCountOnDeath);
        ApplyImpulseToParts(impulse);
        lastAppliedKnockbackSequence = KnockbackSequence;
    }

    private void PublishBloodSplatter(int count)
    {
        if (count <= 0 || bloodSplatterSpawnedForDeath)
            return;

        BloodSplatterCount = count;
        BloodSplatterPosition = Owner.transform.position;
        BloodSplatterDirection = KnockbackImpulse;
        BloodSplatterSequence++;
        bloodSplatterSpawnedForDeath = true;
        ApplyBloodSplatter(BloodSplatterCount);
        lastAppliedBloodSplatterSequence = BloodSplatterSequence;
    }

    private void PublishBloodSplatterEvent(int count)
    {
        PublishBloodSplatterEvent(count, Owner.transform.position, KnockbackImpulse);
    }

    private void PublishBloodSplatterEvent(int count, Vector3 sourcePosition, Vector3 impulseDirection)
    {
        if (count <= 0)
            return;

        BloodSplatterCount = count;
        BloodSplatterPosition = sourcePosition;
        BloodSplatterDirection = impulseDirection;
        BloodSplatterSequence++;
        ApplyBloodSplatter(BloodSplatterCount);
        lastAppliedBloodSplatterSequence = BloodSplatterSequence;
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

    private void ApplyNetworkedBloodSplatterIfNeeded()
    {
        if (BloodSplatterSequence == 0 || lastAppliedBloodSplatterSequence == BloodSplatterSequence)
            return;

        ApplyBloodSplatter(BloodSplatterCount);
        lastAppliedBloodSplatterSequence = BloodSplatterSequence;
    }

    private void ApplyBloodSplatter(int count)
    {
        if (count <= 0)
            return;

        EnsureReferences();
        Vector3 sourcePosition = BloodSplatterPosition;
        if (sourcePosition == Vector3.zero)
            sourcePosition = Owner.transform.position;

        surfaceBloodController.SpawnBlood(count, sourcePosition, BloodSplatterDirection);
    }

    private void DetectRagdollSurfaceContactsFromParts()
    {
        EnsureReferences();
        surfaceBloodController.TickSurfaceContacts();
    }

    private void CaptureNetworkedRagdollPose()
    {
        EnsureReferences();
        SyncedRagdollPartCount = rigPresentation.CapturePose(
            SyncedRagdollPartPositions,
            SyncedRagdollPartRotations,
            MaxSyncedRagdollParts);
    }

    private void ApplyNetworkedRagdollPoseIfNeeded(float deltaTime)
    {
        if (Object == null || Object.HasStateAuthority || !IsRagdollEnabled || SyncedRagdollPartCount <= 0)
            return;

        EnsureReferences();
        rigPresentation.ApplyProxyPose(
            SyncedRagdollPartCount,
            MaxSyncedRagdollParts,
            SyncedRagdollPartPositions,
            SyncedRagdollPartRotations,
            ProxyPoseFollowSpeed,
            deltaTime);
    }

    private void ApplyImpulseToParts(Vector3 impulse)
    {
        EnsureReferences();
        rigPresentation.ApplyImpulse(impulse);
    }

    private void EnsureReferences()
    {
        GameObject root = ResolveOwner();

        if (rigPresentation == null)
            rigPresentation = GetComponent<RagdollRigPresentation>()
                ?? root.GetComponentInChildren<RagdollRigPresentation>(true);

        if (rigPresentation == null)
            rigPresentation = gameObject.AddComponent<RagdollRigPresentation>();

        if (surfaceBloodController == null)
            surfaceBloodController = GetComponent<RagdollSurfaceBloodController>()
                ?? root.GetComponentInChildren<RagdollSurfaceBloodController>(true);

        if (surfaceBloodController == null)
            surfaceBloodController = gameObject.AddComponent<RagdollSurfaceBloodController>();

        rigPresentation.Configure(
            root,
            this,
            parts,
            animators,
            characterController,
            networkController,
            disableOnDeath,
            visualPoseDrivers,
            visualRenderers,
            autoDisableNonRagdollBehaviours,
            hideNonVisualRenderersOnDeath,
            deadCamera);

        surfaceBloodController.Configure(
            this,
            rigPresentation.Parts,
            bloodSplatter,
            bloodSplatterCountOnSurfaceContact,
            bloodSplatterSurfaceReentryDelay,
            bloodSplatterSurfaceContactProbeRadius,
            bloodSplatterSurfaceMask);
    }

    private GameObject ResolveOwner()
    {
        if (owner != null)
            return owner;

        PlayerMovement player = GetComponentInParent<PlayerMovement>();
        return player != null ? player.gameObject : gameObject;
    }
}
