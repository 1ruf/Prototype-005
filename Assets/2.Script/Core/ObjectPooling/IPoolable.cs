using UnityEngine;

public interface IPoolable
{
    void OnSpawnedFromPool(in PoolSpawnContext context);
    void OnDespawnedToPool();
}

public readonly struct PoolSpawnContext
{
    public PoolSpawnContext(GameObject prefab, GameObject instance, Transform parent)
    {
        Prefab = prefab;
        Instance = instance;
        Parent = parent;
    }

    public GameObject Prefab { get; }
    public GameObject Instance { get; }
    public Transform Parent { get; }
}
