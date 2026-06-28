using System;
using UnityEngine;

public abstract class PoweredObject : MonoBehaviour, IPowerable
{
    [SerializeField] private string powerKey = PowerKeys.LightFloor1;

    public bool Powered { get; private set; }
    public string PowerKey => powerKey;

    protected virtual void OnEnable()
    {
        NetworkPowerRuntime.RegisterPowerable(powerKey, this);
        NetworkPowerRuntime.InitializePowerIfUnknown(powerKey, GetInitialPowerState());
    }

    protected virtual void OnDisable()
    {
        NetworkPowerRuntime.UnregisterPowerable(powerKey, this);
    }

    public void PowerSupply(bool value)
    {
        Powered = value;
        SupplyPower(value);
    }

    protected abstract void SupplyPower(bool value);
    protected virtual bool GetInitialPowerState() => Powered;

    public virtual void PowerOriginLost() { }
}
