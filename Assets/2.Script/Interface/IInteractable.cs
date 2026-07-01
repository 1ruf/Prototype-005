using UnityEngine;

public interface IInteractable
{
    public void Interact();
}

public interface IHoldInteractable
{
    float RequiredHoldTime { get; }
}

public interface IInteractionFailureProvider
{
    bool TryGetInteractionFailureMessage(PlayerMovement player, out string message);
}
