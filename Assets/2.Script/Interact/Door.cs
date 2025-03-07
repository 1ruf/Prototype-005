using UnityEngine;
using DG.Tweening;

public class Door : MonoBehaviour, IInteractable
{
    private bool DoorOpened;
    public void Interact()
    {
        if (DoorOpened) Close();
        else Open();
        DoorOpened = !DoorOpened;
    }

    private void Close()
    {
        transform.DORotate(Vector3.zero, 3);
    }

    private void Open()
    {
        transform.DORotate(new Vector3(0,120,0), 3);
    }
}
