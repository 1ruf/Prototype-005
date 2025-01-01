using UnityEngine;

public class GrabTool : MonoBehaviour, IInteractable
{
    [SerializeField] private Transform _Hand;

    private bool _isGrab;
    public void Interact()
    {
        _isGrab = true;
    }

    private void Update()
    {
        SetGrab(true);
    }

    private void SetGrab(bool value)
    {
        if(value) transform.position = _Hand.position;
    }
}
