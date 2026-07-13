using Fusion;
using UnityEngine;

public enum PlayerHidingPhase
{
    None = 0,
    Entering = 1,
    Hidden = 2,
    Exiting = 3
}

[DisallowMultipleComponent]
public class NetworkPlayerHidingComponent : NetworkEntityBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private NetworkPlayerItemHolder itemHolder;
    [SerializeField] private NetworkInventory inventory;
    [SerializeField] private PlayerHidingPresentation presentation;

    [Networked] public NetworkBool IsHiding { get; private set; }
    [Networked] public NetworkBool IsHidePending { get; private set; }
    [Networked] public NetworkBool IsHidingCompromised { get; private set; }
    [Networked] private int CurrentSpotId { get; set; }
    [Networked] private PlayerHidingPhase Phase { get; set; }
    [Networked] private float TransitionEndTime { get; set; }

    private NetworkHidingSpot localRequestedSpot;
    private bool localPendingRequest;
    public PlayerHidingPhase CurrentPhase => Phase;
    public bool BlocksPlayerInput => Phase != PlayerHidingPhase.None || IsHiding || IsHidePending || localPendingRequest;
    public bool CanUseItems => !IsHiding && !IsHidePending && !localPendingRequest;
    public bool CanRequestExit => Phase == PlayerHidingPhase.Hidden && IsHiding && !IsHidePending && !localPendingRequest;
    public NetworkHidingSpot CurrentSpot => ResolveSpot(CurrentSpotId);

    private void Awake()
    {
        Initialize(EntityOwnerResolver.Resolve(this));
    }

    public override void Initialize(GameObject entityOwner)
    {
        base.Initialize(entityOwner);
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

        if ((Phase == PlayerHidingPhase.Entering || Phase == PlayerHidingPhase.Exiting)
            && Runner != null
            && Runner.SimulationTime >= TransitionEndTime)
            CompleteTransitionStateAuthority();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState)
            CurrentSpot?.Release(this);

        presentation?.ReleaseCameraOverride();
    }

    private void OnDisable()
    {
        presentation?.ReleaseCameraOverride();
    }

    private void LateUpdate()
    {
        CompleteLocalTransitionIfNeeded();
        presentation?.TickLocalCamera(Phase, CurrentSpot);
    }

    public void RequestToggle(NetworkHidingSpot spot)
    {
        if (Phase == PlayerHidingPhase.Entering || Phase == PlayerHidingPhase.Exiting || localPendingRequest)
            return;

        if (Phase == PlayerHidingPhase.Hidden || IsHiding)
        {
            RequestExit();
            return;
        }

        RequestEnter(spot);
    }

    public void RequestEnter(NetworkHidingSpot spot)
    {
        if (spot == null || Phase != PlayerHidingPhase.None || IsHiding || IsHidePending || localPendingRequest)
            return;

        if (!spot.IsWithinUseRange(Owner.transform.position))
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
        if (Phase != PlayerHidingPhase.Hidden || !IsHiding || IsHidePending || localPendingRequest)
            return;

        if (Object == null || Object.HasStateAuthority)
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
        TryEnterStateAuthority(ResolveSpot(spotId));
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
        if (spot == null || IsHiding || !spot.IsWithinUseRange(Owner.transform.position))
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
        Phase = PlayerHidingPhase.Entering;
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
        Phase = PlayerHidingPhase.Exiting;
        TransitionEndTime = GetTransitionEndTime(spot.ExitDuration);
        localPendingRequest = false;
        ApplyPresentation(true);
        if (Object != null)
            RPC_ApplyServerDecision(true);
    }

    private void CompleteTransitionStateAuthority()
    {
        if (Phase == PlayerHidingPhase.Entering)
        {
            IsHiding = true;
            IsHidePending = false;
            Phase = PlayerHidingPhase.Hidden;
            ApplyPresentation(true);
            return;
        }

        if (Phase == PlayerHidingPhase.Exiting)
            CompleteExitStateAuthority(CurrentSpot);
    }

    private void CompleteLocalTransitionIfNeeded()
    {
        if (Object != null)
            return;

        if ((Phase == PlayerHidingPhase.Entering || Phase == PlayerHidingPhase.Exiting)
            && Time.time >= TransitionEndTime)
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
        Phase = PlayerHidingPhase.None;
        TransitionEndTime = 0f;
        localPendingRequest = false;
        CurrentSpotId = 0;
        ApplyPresentation(true);
    }

    private void ApplyPresentation(bool force)
    {
        ResolveReferences();
        if (Phase != PlayerHidingPhase.None)
            localPendingRequest = false;

        presentation?.Apply(Phase, CurrentSpot, itemHolder, inventory, force);
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

    private float GetTransitionEndTime(float duration)
    {
        float now = Runner != null ? Runner.SimulationTime : Time.time;
        return now + Mathf.Max(0f, duration);
    }

    private NetworkHidingSpot ResolveSpot(int spotId)
    {
        return NetworkHidingSpot.Find(spotId) ?? localRequestedSpot;
    }

    private void ResolveReferences()
    {
        GameObject entityOwner = Owner;
        if (playerMovement == null)
            playerMovement = entityOwner.GetComponentInChildren<PlayerMovement>(true);

        if (itemHolder == null)
            itemHolder = entityOwner.GetComponentInChildren<NetworkPlayerItemHolder>(true);

        if (inventory == null)
            inventory = entityOwner.GetComponentInChildren<NetworkInventory>(true);

        if (presentation == null)
            presentation = entityOwner.GetComponentInChildren<PlayerHidingPresentation>(true);

        if (presentation == null)
            presentation = gameObject.AddComponent<PlayerHidingPresentation>();

        presentation.Initialize(entityOwner, playerMovement);
    }

}
