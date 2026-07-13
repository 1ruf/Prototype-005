using Fusion;
using UnityEngine;

public abstract class EntityBehaviour : MonoBehaviour, IEntityComponent
{
    private GameObject owner;

    public GameObject Owner => owner != null ? owner : EntityOwnerResolver.Resolve(this);

    public virtual void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : EntityOwnerResolver.Resolve(this);
    }

    protected T GetEntityComponent<T>() where T : Component
    {
        return Owner != null ? Owner.GetComponentInChildren<T>(true) : null;
    }
}

public abstract class NetworkEntityBehaviour : NetworkBehaviour, INetworkEntityComponent
{
    private GameObject owner;

    public GameObject Owner => owner != null ? owner : EntityOwnerResolver.Resolve(this);

    public virtual void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : EntityOwnerResolver.Resolve(this);
    }

    protected T GetEntityComponent<T>() where T : Component
    {
        return Owner != null ? Owner.GetComponentInChildren<T>(true) : null;
    }
}
