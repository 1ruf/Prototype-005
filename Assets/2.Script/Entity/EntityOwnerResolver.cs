using Fusion;
using UnityEngine;

public static class EntityOwnerResolver
{
    public static GameObject Resolve(Component component)
    {
        if (component == null)
            return null;

        NetworkEntityRoot entityRoot = component.GetComponentInParent<NetworkEntityRoot>(true);
        if (entityRoot != null)
            return entityRoot.Owner;

        NetworkObject networkObject = component.GetComponentInParent<NetworkObject>(true);
        if (networkObject != null)
            return networkObject.gameObject;

        return component.transform.root != null ? component.transform.root.gameObject : component.gameObject;
    }
}
