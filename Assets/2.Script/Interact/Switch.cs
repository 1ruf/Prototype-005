using UnityEngine;
using UnityEngine.Events;

public class Switch : MonoBehaviour, IInteractable, IHoldInteractable
{
    [SerializeField] private string powerKey = PowerKeys.LightFloor1;
    [SerializeField] private float requiredHoldTime;
    [SerializeField] private bool togglePower = true;
    [SerializeField] private bool targetPowerState = true;
    [SerializeField] private bool invokeTriggeredLocally;

    public UnityEvent Triggered;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);

    public void Interact()
    {
        if (togglePower)
            NetworkPowerRuntime.RequestToggle(powerKey);
        else
            NetworkPowerRuntime.RequestSetPower(powerKey, targetPowerState);

        if (invokeTriggeredLocally)
            Triggered?.Invoke();
    }
}
