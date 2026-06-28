using UnityEngine;

public interface IInteractable
{
    public void Interact();
}

public interface IHoldInteractable
{
    float RequiredHoldTime { get; }
}
