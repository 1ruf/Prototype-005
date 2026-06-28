using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledObject : MonoBehaviour
{
    private GameObject prefab;
    private GameObjectPool pool;

    public GameObject Prefab => prefab;
    public bool IsInPool { get; private set; }

    internal void Bind(GameObject sourcePrefab, GameObjectPool ownerPool)
    {
        prefab = sourcePrefab;
        pool = ownerPool;
    }

    internal void MarkSpawned()
    {
        IsInPool = false;
    }

    internal void MarkDespawned()
    {
        IsInPool = true;
    }

    public void Despawn()
    {
        if (pool != null)
            pool.Release(gameObject);
        else
            GameObjectPoolManager.Despawn(gameObject);
    }
}
