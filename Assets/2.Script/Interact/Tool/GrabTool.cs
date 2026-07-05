using UnityEngine;

public class GrabTool : MonoBehaviour, IInteractable, IHoldInteractable, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string interactionText = "Object";
    [SerializeField] private string actionText = "Grab";
    [SerializeField] private int interactionPriority = 50;
    [SerializeField] private Transform _Hand;
    [SerializeField] private float requiredHoldTime;

    private bool _isGrab;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);
    public string InteractionText => interactionText;
    public string InteractionActionText => actionText;
    public int InteractionPriority => interactionPriority;

    public void Interact()
    {
        _isGrab = true;
    }

    private void Update()
    {
        SetGrab(_isGrab);
    }

    private void SetGrab(bool value)
    {
        if(value) transform.position = _Hand.position;
    }
}
