using Fusion;
using UnityEngine;
using DG.Tweening;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class Door : NetworkBehaviour, IInteractable, IPlayerInteractable, ILockable, IUnlockable, IHoldInteractable
{
    [SerializeField] private Vector3 closedRotation = Vector3.zero;
    [SerializeField] private Vector3 openRotation = new Vector3(0, 120, 0);
    [SerializeField] private float tweenDuration = 3f;
    [SerializeField] private float requiredHoldTime;
    [SerializeField] private Transform breakPhysicsTarget;
    [SerializeField] private float breakForce = 8f;
    [SerializeField] private float breakUpwardForce = 1.5f;

    [Networked] public NetworkBool IsOpenState { get; private set; }
    [Networked] public NetworkBool IsLockedState { get; private set; }
    [Networked] public NetworkBool IsBrokenState { get; private set; }
    [Networked] private Vector3 BreakImpulse { get; set; }

    private bool hasAppliedVisualState;
    private bool hasAppliedBrokenState;
    private bool visualOpen;
    private bool localLocked;
    private bool localBroken;
    private Collider[] cachedColliders;

    public bool IsOpen => Object != null ? IsOpenState : visualOpen;
    public bool IsLocked => Object != null ? IsLockedState : localLocked;
    public bool IsBroken => Object != null ? IsBrokenState : localBroken;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);

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
        Interact();
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
        if (IsBroken)
            return;

        if (Object == null)
        {
            if (localLocked && open)
                return;

            ApplyVisualState(open, true);
            return;
        }

        if (Object.HasStateAuthority)
        {
            SetOpenStateAuthority(open);
            return;
        }

        RPC_RequestSetOpen(open);
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

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetOpen(NetworkBool open)
    {
        SetOpenStateAuthority(open);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetLocked(NetworkBool locked)
    {
        SetLockedStateAuthority(locked);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestBreak(Vector3 direction)
    {
        BreakStateAuthority(direction);
    }

    private void SetOpenStateAuthority(bool open)
    {
        if (IsBrokenState)
            return;

        if (IsLockedState && open)
            return;

        if (IsOpenState == open)
            return;

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
            SetOpenStateAuthority(false);
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

        if (hasAppliedVisualState && visualOpen == open)
            return;

        hasAppliedVisualState = true;
        visualOpen = open;

        Vector3 targetRotation = open ? openRotation : closedRotation;
        transform.DOKill();

        if (animate && tweenDuration > 0f)
        {
            transform.DORotate(targetRotation, tweenDuration);
            return;
        }

        transform.eulerAngles = targetRotation;
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
        SetDoorCollidersEnabled(false);

        Transform physicsTarget = ResolveBreakPhysicsTarget();
        if (physicsTarget == null)
            return;

        Rigidbody body = physicsTarget.GetComponent<Rigidbody>();
        if (body == null)
            body = physicsTarget.gameObject.AddComponent<Rigidbody>();

        body.isKinematic = false;
        body.useGravity = true;
        body.AddForce(impulse, ForceMode.Impulse);
        body.AddTorque(Vector3.Cross(Vector3.up, impulse.normalized) * breakForce, ForceMode.Impulse);
    }

    private Transform ResolveBreakPhysicsTarget()
    {
        if (breakPhysicsTarget != null)
            return breakPhysicsTarget;

        return transform.childCount > 0 ? transform.GetChild(0) : transform;
    }

    private void SetDoorCollidersEnabled(bool enabled)
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider doorCollider in cachedColliders)
        {
            if (doorCollider != null)
                doorCollider.enabled = enabled;
        }
    }
}
