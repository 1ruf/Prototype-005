using UnityEngine;

public class Eye : MonoBehaviour, ICollectable
{
    [SerializeField] private FacePart currentFacePart;
    public void Get()
    {
        CSH_Manager.Instance.AddFacePart(currentFacePart);
        Destroy(transform.parent.gameObject);
    }
}
