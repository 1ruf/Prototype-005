using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEngine.Rendering.DebugUI;

public class PowerController : GameControllerBase
{
    private Dictionary<string, PowerContainer> _container = new();
    public override GameControllerBase Init()
    {
        return this;
    }

    public PowerContainer RegistContainer(string key)
    {
        if (_container.ContainsKey(key) == true)
            return null;

        PowerContainer newContainer = new();
        _container.Add(key, newContainer);

        return newContainer;
    }

    public void RemovePowerObject(string key)
    {
        if (_container.TryGetValue(key, out PowerContainer registedContainer) == false)
            return;
        registedContainer.RemoveSequence();
        _container.Remove(key);
    }

    public PowerContainer GetContainer(string key)
    {
        PowerContainer registedContainer;
        if (_container.TryGetValue(key, out registedContainer) == false)
        {
            registedContainer = RegistContainer(key);
            Debug.LogWarning($"there is no registed powerContainer [{key}]. new container [{key}] created.");
        }

        return registedContainer;
    }

    public void ChangePower(PowerContainer target, bool value)
    {
        foreach (var container in _container.Values)
        {
            container.SupplyPowers(value);
        }
    }
}
public class PowerContainer
{
    private HashSet<IPowerable> _powerables = new();

    public void RegistPowerObject(IPowerable powerable)
    {
        if (_powerables.Contains(powerable))
            return;

        _powerables.Add(powerable);
    }

    public void RemovePowerObject(IPowerable powerable)
    {
        if (_powerables.Contains(powerable) == false)
            return;

        _powerables.Remove(powerable);
    }

    public void SupplyPowers(bool value)
    {
        foreach (var power in _powerables)
        {
            power.PowerSupply(value);
        }
    }

    public void RemoveSequence()
    {
        foreach (var power in _powerables)
        {
            power.PowerOriginLost();
        }
        _powerables.Clear();
    }
}