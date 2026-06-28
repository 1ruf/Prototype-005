using UnityEngine;

public class GrabTool : MonoBehaviour, IInteractable, IHoldInteractable
{
    [SerializeField] private Transform _Hand;
    [SerializeField] private float requiredHoldTime;

    private bool _isGrab;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);

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
