using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class NetworkUnlockUseTarget : NetworkItemUseTarget
{
    private static readonly List<NetworkUnlockUseTarget> RegisteredTargets = new List<NetworkUnlockUseTarget>();

    [Tooltip("Components that implement ILockable. These are locked until this target is resolved.")]
    [SerializeField] private MonoBehaviour[] lockTargets;
    [Tooltip("Components that implement IUnlockable. These receive the unlock action when this target is resolved.")]
    [SerializeField] private MonoBehaviour[] unlockTargets;
    [SerializeField] private bool disableSelfWhenUnlocked = true;
    [SerializeField] private GameObject[] enableWhenUnlocked;
    [SerializeField] private GameObject[] disableWhenUnlocked;

    public UnityEvent Unlocked;

    private bool hasAppliedResolvedState;
    private bool lastAppliedResolvedState;
    private bool hasInvokedUnlocked;

    protected override void Awake()
    {
        base.Awake();
        RegisterTarget();
    }

    public override void Spawned()
    {
        RegisterTarget();
        base.Spawned();
    }

    private void OnDestroy()
    {
        RegisteredTargets.Remove(this);
    }

    protected override void ApplyResolvedState(bool resolved)
    {
        if (hasAppliedResolvedState && lastAppliedResolvedState == resolved)
            return;

        hasAppliedResolvedState = true;
        lastAppliedResolvedState = resolved;

        ApplyLockTargets(!resolved);

        if (resolved)
            ApplyUnlockTargets();

        if (enableWhenUnlocked != null)
        {
            foreach (GameObject target in enableWhenUnlocked)
            {
                if (target != null)
                    target.SetActive(resolved);
            }
        }

        if (disableWhenUnlocked != null)
        {
            foreach (GameObject target in disableWhenUnlocked)
            {
                if (target != null)
                    target.SetActive(!resolved);
            }
        }
    }

    protected override void OnResolvedByItem(PlayerItemSO usedItem, NetworkInventory inventory)
    {
        InvokeUnlockedOnce();
    }

    protected override void OnResolvedVisual()
    {
        InvokeUnlockedOnce();

        if (disableSelfWhenUnlocked)
            gameObject.SetActive(false);
    }

    private void ApplyLockTargets(bool locked)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        if (lockTargets == null)
            return;

        foreach (MonoBehaviour target in lockTargets)
        {
            if (target is ILockable lockable)
            {
                if (locked || CanUnlockTarget(target))
                    lockable.RequestSetLocked(locked);
            }
        }
    }

    private void ApplyUnlockTargets()
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        if (unlockTargets == null)
            return;

        foreach (MonoBehaviour target in unlockTargets)
        {
            if (target is IUnlockable unlockable)
            {
                if (CanUnlockTarget(target))
                    unlockable.RequestUnlock();
            }
        }
    }

    private void InvokeUnlockedOnce()
    {
        if (hasInvokedUnlocked)
            return;

        hasInvokedUnlocked = true;
        Unlocked?.Invoke();
    }

    private void RegisterTarget()
    {
        if (!RegisteredTargets.Contains(this))
            RegisteredTargets.Add(this);
    }

    private bool CanUnlockTarget(MonoBehaviour target)
    {
        if (target == null)
            return true;

        for (int i = RegisteredTargets.Count - 1; i >= 0; i--)
        {
            NetworkUnlockUseTarget registeredTarget = RegisteredTargets[i];
            if (registeredTarget == null)
            {
                RegisteredTargets.RemoveAt(i);
                continue;
            }

            if (registeredTarget == this || registeredTarget.IsResolved)
                continue;

            if (registeredTarget.ContainsLockTarget(target) || registeredTarget.ContainsUnlockTarget(target))
                return false;
        }

        return true;
    }

    private bool ContainsLockTarget(MonoBehaviour target)
    {
        return ContainsTarget(lockTargets, target);
    }

    private bool ContainsUnlockTarget(MonoBehaviour target)
    {
        return ContainsTarget(unlockTargets, target);
    }

    private static bool ContainsTarget(MonoBehaviour[] targets, MonoBehaviour target)
    {
        if (targets == null || target == null)
            return false;

        foreach (MonoBehaviour candidate in targets)
        {
            if (candidate == target)
                return true;
        }

        return false;
    }
}
