using UnityEngine;

[DisallowMultipleComponent]
public sealed class LeverPowerController : MonoBehaviour, ILeverControllable, ILeverToggleable
{
    [SerializeField] private string powerKey = PowerKeys.Floor1;

    private Lever lever;
    private bool hasSynchronizedState;

    private void Awake()
    {
        lever = GetComponent<Lever>();
    }

    private void OnEnable()
    {
        NetworkPowerRuntime.PowerStateChanged += HandlePowerStateChanged;
        hasSynchronizedState = NetworkPowerRuntime.HasPowerState(powerKey);
        ApplyCurrentPowerState();
    }

    private void OnDisable()
    {
        NetworkPowerRuntime.PowerStateChanged -= HandlePowerStateChanged;
    }

    public void SetLeverState(bool isOn)
    {
        NetworkPowerRuntime.RequestSetPower(powerKey, isOn);
    }

    public void ToggleLeverState()
    {
        NetworkPowerRuntime.RequestToggle(powerKey);
    }

    private void HandlePowerStateChanged(string changedPowerKey, bool isOn)
    {
        if (changedPowerKey == powerKey)
        {
            lever?.SetStateFromSynchronizedSource(isOn, hasSynchronizedState);
            hasSynchronizedState = true;
        }
    }

    private void ApplyCurrentPowerState()
    {
        lever?.SetStateFromSynchronizedSource(NetworkPowerRuntime.GetPower(powerKey), false, false);
    }
}
