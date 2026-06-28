using UnityEngine;

public class Eye : MonoBehaviour, ICollectable
{
    [SerializeField] private FacePart currentFacePart;
    public void Get()
    {
        CSH_Manager.Instance.AddFacePart(currentFacePart);
        GameObject target = transform.parent != null ? transform.parent.gameObject : gameObject;
        GameObjectPoolManager.Despawn(target);
    }
}
