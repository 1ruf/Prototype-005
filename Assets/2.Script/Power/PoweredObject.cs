using System;
using UnityEngine;

public abstract class PoweredObject : MonoBehaviour, IPowerable
{
    public bool Powered { get; private set; }

    public void PowerSupply(bool value)
    {
        Powered = value;
    }

    protected abstract void SupplyPower(bool value);

    public virtual void PowerOriginLost() { }
}
