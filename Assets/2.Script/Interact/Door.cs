using Fusion;
using UnityEngine;
using DG.Tweening;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class Door : NetworkBehaviour, IInteractable, IPlayerInteractable, ILockable, IUnlockable, IHoldInteractable, IInteractionFailureProvider, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string interactionText = "Door";
    [SerializeField] private string openActionText = "Open";
    [SerializeField] private string closeActionText = "Close";
    [SerializeField] private int interactionPriority = 10;
    [SerializeField] private Vector3 closedRotation = Vector3.zero;
    [SerializeField] private Vector3 openRotation = new Vector3(0, 120, 0);
    [SerializeField] private bool invertPlayerSideOpenDirection;
    [SerializeField] private float tweenDuration = 3f;
    [SerializeField] private float requiredHoldTime;
    [SerializeField] private Transform breakPhysicsTarget;
    [SerializeField] private float breakForce = 2f;
    [SerializeField] private float breakUpwardForce = 0.35f;
    [SerializeField] private float brokenDestroyDelay = 10f;

    [Header("Network Request Security")]
    [SerializeField] private ServerRequestValidationPolicy requestValidationPolicy = ServerRequestValidationPolicy.CreateInteractionDefault();
    [SerializeField] private bool allowClientLockStateRequests;
    [SerializeField] private bool allowClientBreakRequests;

    [Networked] public NetworkBool IsOpenState { get; private set; }
    [Networked] public NetworkBool IsLockedState { get; private set; }
    [Networked] public NetworkBool IsBrokenState { get; private set; }
    [Networked] private Vector3 NetworkOpenRotation { get; set; }
    [Networked] private Vector3 BreakImpulse { get; set; }

    private bool hasAppliedVisualState;
    private bool hasAppliedBrokenState;
    private bool visualOpen;
    private Vector3 visualOpenRotation;
    private Vector3 localOpenRotation;
    private bool localLocked;
    private bool localBroken;
    private Collider[] cachedColliders;

    private const int OpenRequestRateLimitScope = 101;
    private const int LockRequestRateLimitScope = 102;
    private const int BreakRequestRateLimitScope = 103;

    public bool IsOpen => Object != null ? IsOpenState : visualOpen;
    public bool IsLocked => Object != null ? IsLockedState : localLocked;
    public bool IsBroken => Object != null ? IsBrokenState : localBroken;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);
    public string InteractionText => interactionText;
    public string InteractionActionText => IsOpen ? closeActionText : openActionText;
    public int InteractionPriority => interactionPriority;

    public override void Spawned()
    {
        ApplyLockedState(IsLockedState);
        ApplyBrokenState(IsBrokenState, BreakImpulse);
        ApplyVisualState(IsOpenState, false);
    }

    public override void Render()
    {
        ApplyLockedState(IsLockedState);
        ApplyBrokenState(IsBrokenState, BreakImpulse);
        ApplyVisualState(IsOpenState, true);
    }

    public void Interact()
    {
        if (IsLocked || IsBroken)
            return;

        RequestSetOpen(!IsOpen);
    }

    public void Interact(PlayerMovement player)
    {
        if (IsLocked || IsBroken)
            return;

        RequestSetOpen(!IsOpen, player);
    }

    public bool TryGetInteractionFailureMessage(PlayerMovement player, out string message)
    {
        if (IsBroken)
        {
            message = "This door is broken.";
            return true;
        }

        if (IsLocked)
        {
            message = "This door is locked.";
            return true;
        }

        message = null;
        return false;
    }

    public void Close()
    {
        RequestSetOpen(false);
    }

    public void Open()
    {
        RequestSetOpen(true);
    }

    public void SetOpen(bool open, bool animate = true)
    {
        RequestSetOpen(open);
    }

    public void RequestSetOpen(bool open)
    {
        RequestSetOpen(open, null);
    }

    private void RequestSetOpen(bool open, PlayerMovement player)
    {
        if (IsBroken)
            return;

        if (Object == null)
        {
            if (localLocked && open)
                return;

            if (open)
                localOpenRotation = ResolveOpenRotation(player);

            ApplyVisualState(open, true);
            return;
        }

        Vector3 requestedOpenRotation = open ? ResolveOpenRotation(player) : GetActiveOpenRotation();
        if (Object.HasStateAuthority)
        {
            SetOpenStateAuthority(open, requestedOpenRotation);
            return;
        }

        RPC_RequestSetOpen(open, requestedOpenRotation);
    }

    public void RequestSetLocked(bool locked)
    {
        if (IsBroken)
            return;

        if (Object == null)
        {
            ApplyLockedState(locked);
            return;
        }

        if (Object.HasStateAuthority)
        {
            SetLockedStateAuthority(locked);
            return;
        }

        RPC_RequestSetLocked(locked);
    }

    public void RequestUnlock()
    {
        RequestSetLocked(false);
        RequestSetOpen(true);
    }

    public void RequestBreak(Vector3 direction)
    {
        if (IsBroken || IsLocked || IsOpen)
            return;

        if (Object == null)
        {
            ApplyBrokenLocal(direction);
            return;
        }

        if (Object.HasStateAuthority)
        {
            BreakStateAuthority(direction);
            return;
        }

        RPC_RequestBreak(direction);
    }

    [Rpc(RpcSources.Proxies, RpcTargets.StateAuthority)]
    private void RPC_RequestSetOpen(NetworkBool open, Vector3 requestedOpenRotation, RpcInfo info = default)
    {
        if (!TryValidateClientRequest(info, OpenRequestRateLimitScope, out ServerRequestContext context))
            return;

        Vector3 authoritativeOpenRotation = GetActiveOpenRotation();
        if (open)
        {
            PlayerMovement requester = ResolveRequesterPlayer(context.PlayerObject);
            if (requester != null)
            {
                authoritativeOpenRotation = ResolveOpenRotation(requester);
            }
            else if (context.IsServerRequest && ServerRequestValidator.IsFinite(requestedOpenRotation))
            {
                authoritativeOpenRotation = requestedOpenRotation;
            }
            else
            {
                return;
            }
        }

        SetOpenStateAuthority(open, authoritativeOpenRotation);
    }

    [Rpc(RpcSources.Proxies, RpcTargets.StateAuthority)]
    private void RPC_RequestSetLocked(NetworkBool locked, RpcInfo info = default)
    {
        if (!TryValidateClientRequest(info, LockRequestRateLimitScope, out _)
            || !allowClientLockStateRequests)
            return;

        SetLockedStateAuthority(locked);
    }

    [Rpc(RpcSources.Proxies, RpcTargets.StateAuthority)]
    private void RPC_RequestBreak(Vector3 direction, RpcInfo info = default)
    {
        if (!TryValidateClientRequest(info, BreakRequestRateLimitScope, out ServerRequestContext context)
            || !allowClientBreakRequests)
            return;

        Vector3 authoritativeDirection;
        if (context.PlayerObject != null)
        {
            authoritativeDirection = transform.position - context.PlayerObject.transform.position;
        }
        else if (context.IsServerRequest && ServerRequestValidator.IsFinite(direction))
        {
            authoritativeDirection = direction;
        }
        else
        {
            return;
        }

        BreakStateAuthority(authoritativeDirection);
    }

    private bool TryValidateClientRequest(RpcInfo info, int rateLimitScope, out ServerRequestContext context)
    {
        requestValidationPolicy ??= ServerRequestValidationPolicy.CreateInteractionDefault();
        return ServerRequestValidator.TryValidate(
            Runner,
            Object,
            transform,
            info,
            requestValidationPolicy,
            rateLimitScope,
            out context,
            out _);
    }

    private static PlayerMovement ResolveRequesterPlayer(NetworkObject playerObject)
    {
        if (playerObject == null)
            return null;

        return playerObject.GetComponent<PlayerMovement>()
            ?? playerObject.GetComponentInChildren<PlayerMovement>(true);
    }

    private void SetOpenStateAuthority(bool open, Vector3 requestedOpenRotation)
    {
        if (IsBrokenState)
            return;

        if (IsLockedState && open)
            return;

        if (IsOpenState == open)
            return;

        if (open)
            NetworkOpenRotation = requestedOpenRotation;

        IsOpenState = open;
        ApplyVisualState(open, true);
    }

    private void SetLockedStateAuthority(bool locked)
    {
        if (IsBrokenState)
            return;

        if (IsLockedState == locked)
            return;

        IsLockedState = locked;
        ApplyLockedState(locked);

        if (locked)
            SetOpenStateAuthority(false, GetActiveOpenRotation());
    }

    private void ApplyLockedState(bool locked)
    {
        localLocked = locked;
    }

    private void BreakStateAuthority(Vector3 direction)
    {
        if (IsBrokenState || IsLockedState || IsOpenState)
            return;

        Vector3 impulse = ResolveBreakImpulse(direction);
        IsBrokenState = true;
        BreakImpulse = impulse;
        ApplyBrokenState(true, impulse);
    }

    private void ApplyVisualState(bool open, bool animate)
    {
        if (IsBroken)
            return;

        Vector3 targetRotation = open ? GetActiveOpenRotation() : closedRotation;
        if (hasAppliedVisualState && visualOpen == open && (!open || ApproximatelyEuler(visualOpenRotation, targetRotation)))
            return;

        hasAppliedVisualState = true;
        visualOpen = open;
        if (open)
            visualOpenRotation = targetRotation;

        transform.DOKill();

        if (animate && tweenDuration > 0f)
        {
            transform.DOLocalRotate(targetRotation, tweenDuration);
            return;
        }

        transform.localEulerAngles = targetRotation;
    }

    private Vector3 ResolveOpenRotation(PlayerMovement player)
    {
        if (player == null)
            return openRotation;

        Vector3 closedForward = ResolveClosedWorldForward();
        Vector3 playerOffset = player.transform.position - transform.position;
        playerOffset.y = 0f;

        if (playerOffset.sqrMagnitude <= 0.0001f)
            return openRotation;

        float side = Vector3.Dot(closedForward, playerOffset.normalized);
        if (Mathf.Abs(side) <= 0.0001f)
            return openRotation;

        float direction = side > 0f ? -1f : 1f;
        if (invertPlayerSideOpenDirection)
            direction *= -1f;

        Vector3 resolvedRotation = openRotation;
        resolvedRotation.y = Mathf.Abs(openRotation.y) * direction;
        return resolvedRotation;
    }

    private Vector3 ResolveClosedWorldForward()
    {
        Quaternion closedLocalRotation = Quaternion.Euler(closedRotation);
        Vector3 localForward = closedLocalRotation * Vector3.forward;
        Vector3 worldForward = transform.parent != null ? transform.parent.TransformDirection(localForward) : localForward;
        worldForward.y = 0f;

        if (worldForward.sqrMagnitude <= 0.0001f)
            worldForward = transform.forward;

        worldForward.y = 0f;
        return worldForward.sqrMagnitude > 0.0001f ? worldForward.normalized : Vector3.forward;
    }

    private Vector3 GetActiveOpenRotation()
    {
        if (Object != null)
            return NetworkOpenRotation.sqrMagnitude > 0.0001f ? NetworkOpenRotation : openRotation;

        return localOpenRotation.sqrMagnitude > 0.0001f ? localOpenRotation : openRotation;
    }

    private static bool ApproximatelyEuler(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) <= 0.01f
            && Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)) <= 0.01f
            && Mathf.Abs(Mathf.DeltaAngle(a.z, b.z)) <= 0.01f;
    }

    private void ApplyBrokenLocal(Vector3 direction)
    {
        Vector3 impulse = ResolveBreakImpulse(direction);
        ApplyBrokenState(true, impulse);
    }

    private Vector3 ResolveBreakImpulse(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = transform.forward;

        direction.Normalize();
        return direction * Mathf.Max(0f, breakForce) + Vector3.up * Mathf.Max(0f, breakUpwardForce);
    }

    private void ApplyBrokenState(bool broken, Vector3 impulse)
    {
        localBroken = broken;

        if (!broken || hasAppliedBrokenState)
            return;

        hasAppliedBrokenState = true;
        transform.DOKill();

        Transform physicsTarget = ResolveBreakPhysicsTarget();
        if (physicsTarget == null)
            return;

        SetBlockingCollidersEnabled(false, physicsTarget);

        Rigidbody body = physicsTarget.GetComponent<Rigidbody>();
        if (body == null)
            body = physicsTarget.gameObject.AddComponent<Rigidbody>();

        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.AddForce(impulse, ForceMode.Impulse);
        body.AddTorque(Vector3.Cross(Vector3.up, impulse.normalized) * breakForce, ForceMode.Impulse);

        if (brokenDestroyDelay > 0f && physicsTarget != transform)
            Destroy(physicsTarget.gameObject, brokenDestroyDelay);
    }

    private Transform ResolveBreakPhysicsTarget()
    {
        if (breakPhysicsTarget != null)
            return breakPhysicsTarget;

        return transform.childCount > 0 ? transform.GetChild(0) : transform;
    }

    private void SetBlockingCollidersEnabled(bool enabled, Transform physicsTarget)
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider doorCollider in cachedColliders)
        {
            if (doorCollider == null)
                continue;

            if (physicsTarget != null && doorCollider.transform.IsChildOf(physicsTarget))
                continue;

            doorCollider.enabled = enabled;
        }
    }
}
