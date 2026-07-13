using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ElectronicDoorLock : MonoBehaviour, IPowerable
{
    [SerializeField] private string powerKey = PowerKeys.LightFloor1;
    [SerializeField] private bool initialPowerState = true;
    [SerializeField] private bool lockWhenPowered = true;
    [Tooltip("Components that implement ILockable. These are locked while the configured power key is active.")]
    [SerializeField] private MonoBehaviour[] lockTargets;

    [Header("Visual")]
    [SerializeField] private MeshRenderer lightMR;
    [SerializeField] private Material enableMat;
    [SerializeField] private Material disableMat;

    private bool powered;

    public string PowerKey => powerKey;
    public bool Powered => powered;

    private void OnEnable()
    {
        NetworkPowerRuntime.RegisterPowerable(powerKey, this);
        NetworkPowerRuntime.InitializePowerIfUnknown(powerKey, initialPowerState);
        PowerSupply(NetworkPowerRuntime.GetPower(powerKey));
    }

    private void OnDisable()
    {
        NetworkPowerRuntime.UnregisterPowerable(powerKey, this);
    }

    public void PowerSupply(bool value)
    {
        powered = value;
        StartCoroutine(ApplyLightEnable(value));
    }

    public void PowerOriginLost()
    {
        powered = false;
        ApplyLightEnable(false);
        ApplyLockState(false);
    }

    private IEnumerator ApplyLightEnable(bool value)
    {
        lightMR.material = value ? enableMat : disableMat;
        yield return new WaitForSeconds(0.1f);
        lightMR.material = value ? disableMat : enableMat;
        yield return new WaitForSeconds(0.2f);
        lightMR.material = value ? enableMat : disableMat;
        ApplyLockState(lockWhenPowered ? value : !value);
    }

    private void ApplyLockState(bool locked)
    {
        if (lockTargets == null)
            return;

        foreach (MonoBehaviour target in lockTargets)
        {
            if (target is not ILockable lockable)
                continue;

            if (!CanControlTarget(target))
                continue;

            lockable.RequestSetLocked(locked);
        }
    }

    private static bool CanControlTarget(MonoBehaviour target)
    {
        if (target == null)
            return false;

        Fusion.NetworkBehaviour networkBehaviour = target as Fusion.NetworkBehaviour;
        return networkBehaviour == null ||
               networkBehaviour.Object == null ||
               networkBehaviour.Object.HasStateAuthority;
    }
}
