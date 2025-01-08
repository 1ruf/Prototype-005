using UnityEngine;

public class Flashlight : MonoBehaviour, IItem, IInteractable
{
    [SerializeField] private GameObject _light;

    public void Interact()
    {
        Transform hand = Manager.Instance.PlayerManager.HandTransform;
        transform.rotation = hand.rotation;
        transform.position = hand.position;
        transform.parent = hand;
    }

    public void SetVisible(bool value)
    {
        SetLight(value);
    }

    private void SetLight(bool value)
    {
        _light.SetActive(value);
    }
}
