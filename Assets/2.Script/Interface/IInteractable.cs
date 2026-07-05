using UnityEngine;

public interface IInteractable
{
    public void Interact();
}

public interface IHoldInteractable
{
    float RequiredHoldTime { get; }
}

public interface IInteractionPrompt
{
    string InteractionText { get; }
}

public interface IInteractionActionPrompt
{
    string InteractionActionText { get; }
}

public interface IInteractionPriority
{
    int InteractionPriority { get; }
}

public interface IInteractionFailureProvider
{
    bool TryGetInteractionFailureMessage(PlayerMovement player, out string message);
}
