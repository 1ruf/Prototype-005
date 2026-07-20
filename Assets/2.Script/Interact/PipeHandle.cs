using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class PipeHandle : MonoBehaviour, IInteractable, IHoldInteractable, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [Header("Interaction")]
    [SerializeField] private string interactionText = "파이프 손잡이";
    [SerializeField] private string openActionText = "열기";
    [SerializeField] private string closeActionText = "닫기";
    [SerializeField, Min(0f)] private float requiredHoldTime = 5f;
    [SerializeField] private int interactionPriority = 10;

    [Header("State")]
    [Tooltip("A unique shared-state key. Every client receives changes made through this key.")]
    [SerializeField] private string networkStateKey = "CSHObunga.Pipes14.Open";
    [SerializeField] private bool isOpen;
    [SerializeField] private UnityEvent onOpened;
    [SerializeField] private UnityEvent onClosed;

    public bool IsOpen => isOpen;
    public float RequiredHoldTime => requiredHoldTime;
    public string InteractionText => interactionText;
    public string InteractionActionText => isOpen ? closeActionText : openActionText;
    public int InteractionPriority => interactionPriority;

    private void Awake()
    {
        InitializeNetworkState();
    }

    private void OnEnable()
    {
        NetworkPowerRuntime.PowerStateChanged += HandleNetworkStateChanged;
        InitializeNetworkState();
    }

    private void OnDisable()
    {
        NetworkPowerRuntime.PowerStateChanged -= HandleNetworkStateChanged;
    }

    /// <summary>
    /// Toggles the handle after the player has held the interaction key for
    /// <see cref="RequiredHoldTime"/> seconds.
    /// </summary>
    public void Interact()
    {
        SetOpen(!isOpen);
    }

    public void Open()
    {
        SetOpen(true);
    }

    public void Close()
    {
        SetOpen(false);
    }

    public void SetOpen(bool value)
    {
        if (string.IsNullOrWhiteSpace(networkStateKey))
        {
            ApplyOpenState(value);
            return;
        }

        // The local player's network bridge requests this from the server. The server
        // broadcasts the resulting state, which invokes the UnityEvents on every client.
        NetworkPowerRuntime.RequestSetPower(networkStateKey, value);
    }

    private void InitializeNetworkState()
    {
        if (string.IsNullOrWhiteSpace(networkStateKey))
            return;

        NetworkPowerRuntime.InitializePowerIfUnknown(networkStateKey, isOpen);
        ApplyOpenState(NetworkPowerRuntime.GetPower(networkStateKey));
    }

    private void HandleNetworkStateChanged(string key, bool value)
    {
        if (key == networkStateKey)
            ApplyOpenState(value);
    }

    private void ApplyOpenState(bool value)
    {
        if (isOpen == value)
            return;

        isOpen = value;

        if (isOpen)
            onOpened?.Invoke();
        else
            onClosed?.Invoke();
    }
}
