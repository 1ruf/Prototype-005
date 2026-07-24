using UnityEngine;

[DisallowMultipleComponent]
public sealed class LeverPowerController : MonoBehaviour, ILeverControllable, ILeverToggleable, IInteractionFailureProvider
{
    [SerializeField] private string powerKey = PowerKeys.Floor1;
    [SerializeField, Tooltip("When enabled, the lever can only be switched on once during a session.")]
    private bool oneShot;
    [SerializeField] private string oneShotFailureMessage = "This lever has already been used.";

    private Lever lever;
    private bool hasSynchronizedState;
    private bool hasReceivedPowerState;
    private bool lastReceivedPowerState;

    private void Awake()
    {
        lever = GetComponent<Lever>();

        if (!NetworkPowerRuntime.HasPowerState(powerKey))
            NetworkPowerRuntime.InitializePowerIfUnknown(powerKey, lever != null && lever.IsOn);
    }

    private void OnEnable()
    {
        NetworkPowerRuntime.PowerStateChanged += HandlePowerStateChanged;
        hasSynchronizedState = false;
        hasReceivedPowerState = false;
    }

    private void OnDisable()
    {
        NetworkPowerRuntime.PowerStateChanged -= HandlePowerStateChanged;
    }

    private void Start()
    {
        if (!NetworkPowerRuntime.HasPowerState(powerKey))
            NetworkPowerRuntime.InitializePowerIfUnknown(powerKey, lever != null && lever.IsOn);

        // PoweredObject.OnEnable 초기화가 모두 끝난 뒤 실제 전력값으로 맞춘다.
        hasSynchronizedState = NetworkPowerRuntime.HasPowerState(powerKey);
        ApplyCurrentPowerState();
    }

    public void SetLeverState(bool isOn)
    {
        if (oneShot && NetworkPowerRuntime.GetPower(powerKey))
            return;

        NetworkPowerRuntime.RequestSetPower(powerKey, isOn);
    }

    public void ToggleLeverState()
    {
        if (oneShot)
        {
            if (!NetworkPowerRuntime.GetPower(powerKey))
                NetworkPowerRuntime.RequestSetPower(powerKey, true);

            return;
        }

        NetworkPowerRuntime.RequestToggle(powerKey);
    }

    public bool TryGetInteractionFailureMessage(PlayerMovement player, out string message)
    {
        if (oneShot && NetworkPowerRuntime.GetPower(powerKey))
        {
            message = oneShotFailureMessage;
            return true;
        }

        message = null;
        return false;
    }

    private void HandlePowerStateChanged(string changedPowerKey, bool isOn)
    {
        if (changedPowerKey != powerKey)
            return;

        if (hasReceivedPowerState && lastReceivedPowerState == isOn)
            return;

        hasReceivedPowerState = true;
        lastReceivedPowerState = isOn;
        lever?.SetStateFromSynchronizedSource(isOn, hasSynchronizedState);
        hasSynchronizedState = true;
    }

    private void ApplyCurrentPowerState()
    {
        lever?.SetStateFromSynchronizedSource(NetworkPowerRuntime.GetPower(powerKey), false, false);
    }
}
