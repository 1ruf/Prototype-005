using System.Collections.Generic;
using UnityEngine;

public sealed class GameObjectPool
{
    private readonly Stack<PooledObject> inactiveObjects;
    private readonly GameObject prefab;
    private readonly Transform root;
    private readonly int maxSize;
    private readonly bool allowGrowth;
    private readonly bool collectionCheck;

    private int totalCount;

    public GameObjectPool(GameObject prefab, Transform root, int prewarmCount, int maxSize, bool allowGrowth, bool collectionCheck)
    {
        this.prefab = prefab;
        this.root = root;
        this.maxSize = Mathf.Max(1, maxSize);
        this.allowGrowth = allowGrowth;
        this.collectionCheck = collectionCheck;
        inactiveObjects = new Stack<PooledObject>(Mathf.Max(0, prewarmCount));

        Prewarm(prewarmCount);
    }

    public GameObject Prefab => prefab;
    public int InactiveCount => inactiveObjects.Count;
    public int TotalCount => totalCount;

    public void Prewarm(int count)
    {
        if (prefab == null || count <= 0)
            return;

        int target = Mathf.Min(count, maxSize);
        while (totalCount < target)
        {
            PooledObject pooledObject = CreateInstance();
            DespawnInstance(pooledObject);
        }
    }

    public GameObject Get(Vector3 position, Quaternion rotation, Transform parent = null, bool worldPositionStays = true)
    {
        PooledObject pooledObject = inactiveObjects.Count > 0 ? inactiveObjects.Pop() : CreateOrFallback();
        if (pooledObject == null)
            return null;

        Transform instanceTransform = pooledObject.transform;
        instanceTransform.SetParent(parent, worldPositionStays);
        instanceTransform.SetPositionAndRotation(position, rotation);

        pooledObject.MarkSpawned();
        pooledObject.gameObject.SetActive(true);
        NotifySpawned(pooledObject, parent);
        return pooledObject.gameObject;
    }

    public T Get<T>(Vector3 position, Quaternion rotation, Transform parent = null, bool worldPositionStays = true) where T : Component
    {
        GameObject instance = Get(position, rotation, parent, worldPositionStays);
        return instance != null ? instance.GetComponent<T>() : null;
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
            return;

        if (!instance.TryGetComponent(out PooledObject pooledObject) || pooledObject.Prefab != prefab)
        {
            Object.Destroy(instance);
            return;
        }

        if (collectionCheck && pooledObject.IsInPool)
        {
            Debug.LogWarning($"Trying to release an already pooled object: {instance.name}", instance);
            return;
        }

        NotifyDespawned(pooledObject);

        if (inactiveObjects.Count >= maxSize)
        {
            totalCount--;
            Object.Destroy(instance);
            return;
        }

        DespawnInstance(pooledObject);
    }

    public void Clear()
    {
        while (inactiveObjects.Count > 0)
        {
            PooledObject pooledObject = inactiveObjects.Pop();
            if (pooledObject != null)
                Object.Destroy(pooledObject.gameObject);
        }

        totalCount = 0;
    }

    private PooledObject CreateOrFallback()
    {
        if (!allowGrowth && totalCount >= maxSize)
            return null;

        return CreateInstance();
    }

    private PooledObject CreateInstance()
    {
        GameObject instance = Object.Instantiate(prefab, root);
        instance.name = prefab.name;

        if (!instance.TryGetComponent(out PooledObject pooledObject))
            pooledObject = instance.AddComponent<PooledObject>();

        pooledObject.Bind(prefab, this);
        totalCount++;
        return pooledObject;
    }

    private void DespawnInstance(PooledObject pooledObject)
    {
        pooledObject.MarkDespawned();
        pooledObject.transform.SetParent(root, false);
        pooledObject.gameObject.SetActive(false);
        inactiveObjects.Push(pooledObject);
    }

    private void NotifySpawned(PooledObject pooledObject, Transform parent)
    {
        PoolSpawnContext context = new PoolSpawnContext(prefab, pooledObject.gameObject, parent);
        IPoolable[] poolables = pooledObject.GetComponentsInChildren<IPoolable>(true);
        for (int i = 0; i < poolables.Length; i++)
            poolables[i].OnSpawnedFromPool(context);
    }

    private void NotifyDespawned(PooledObject pooledObject)
    {
        IPoolable[] poolables = pooledObject.GetComponentsInChildren<IPoolable>(true);
        for (int i = 0; i < poolables.Length; i++)
            poolables[i].OnDespawnedToPool();
    }
}
