using UnityEngine;
using UnityEngine.Events;

public class Switch : MonoBehaviour, IInteractable
{
    public UnityEvent Triggered;
    public void Interact()
    {
        print("����YEPIIIIIIIIII");
        Triggered?.Invoke();
    }
}
