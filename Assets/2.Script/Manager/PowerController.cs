using System;
using System.Collections.Generic;
using UnityEngine;

public class PowerController : GameControllerBase
{
    private readonly Dictionary<string, PowerContainer> _container = new Dictionary<string, PowerContainer>();
    private readonly Dictionary<string, bool> _states = new Dictionary<string, bool>();

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

        if (_states.TryGetValue(key, out bool state))
            newContainer.SupplyPowers(state);

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

    public bool TryGetContainer(string key, out PowerContainer container)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            container = null;
            return false;
        }

        return _container.TryGetValue(key, out container);
    }

    public void ChangePower(PowerContainer target, bool value)
    {
        foreach (var container in _container.Values)
        {
            container.SupplyPowers(value);
        }
    }

    public void SetPower(string key, bool value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        _states[key] = value;
        GetContainer(key).SupplyPowers(value);
    }

    public void TogglePower(string key)
    {
        SetPower(key, !GetPower(key));
    }

    public bool GetPower(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && _states.TryGetValue(key, out bool value) && value;
    }

    public bool HasPowerState(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && _states.ContainsKey(key);
    }

    public void SetInitialPower(string key, bool value)
    {
        if (string.IsNullOrWhiteSpace(key) || _states.ContainsKey(key))
            return;

        SetPower(key, value);
    }

    public void ForEachState(Action<string, bool> callback)
    {
        if (callback == null)
            return;

        foreach (KeyValuePair<string, bool> state in _states)
            callback(state.Key, state.Value);
    }
}
public class PowerContainer
{
    private readonly HashSet<IPowerable> _powerables = new HashSet<IPowerable>();

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
