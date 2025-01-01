using UnityEngine;
using UnityEngine.Events;

public class Switch : MonoBehaviour, IInteractable
{
    public UnityEvent Triggered;
    public void Interact()
    {
        print("´­¸²YEPIIIIIIIIII");
        Triggered?.Invoke();
    }
}
