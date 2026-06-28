using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameObjectPoolManager : MonoBehaviour
{
    private const string RuntimeManagerName = "GameObjectPoolManager";

    [SerializeField] private PoolSettings settings;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool createPoolsLazily = true;
    [SerializeField] private int defaultPrewarmCount;
    [SerializeField, Min(1)] private int defaultMaxSize = 64;
    [SerializeField] private bool defaultAllowGrowth = true;
    [SerializeField] private bool defaultCollectionCheck = true;

    private readonly Dictionary<GameObject, GameObjectPool> poolsByPrefab = new Dictionary<GameObject, GameObjectPool>();
    private readonly Dictionary<GameObject, GameObject> prefabByInstance = new Dictionary<GameObject, GameObject>();
    private Transform poolRoot;

    public static GameObjectPoolManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        EnsureRoot();
        InitializeFromSettings();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null, bool worldPositionStays = true)
    {
        if (prefab == null)
            return null;

        GameObjectPoolManager manager = GetOrCreate();
        return manager.SpawnInternal(prefab, position, rotation, parent, worldPositionStays);
    }

    public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null, bool worldPositionStays = true) where T : Component
    {
        if (prefab == null)
            return null;

        GameObject instance = Spawn(prefab.gameObject, position, rotation, parent, worldPositionStays);
        return instance != null ? instance.GetComponent<T>() : null;
    }

    public static void Despawn(GameObject instance)
    {
        if (instance == null)
            return;

        GameObjectPoolManager manager = Instance;
        if (manager == null || !manager.DespawnInternal(instance))
            Destroy(instance);
    }

    public static void Despawn(Component instance)
    {
        if (instance != null)
            Despawn(instance.gameObject);
    }

    public void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null)
            return;

        GameObjectPool pool = GetOrCreatePool(prefab, count, defaultMaxSize, defaultAllowGrowth, defaultCollectionCheck);
        if (pool != null)
            pool.Prewarm(count);
    }

    public void ClearInactive()
    {
        foreach (GameObjectPool pool in poolsByPrefab.Values)
            pool.Clear();

        prefabByInstance.Clear();
    }

    private static GameObjectPoolManager GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        GameObject managerObject = new GameObject(RuntimeManagerName);
        return managerObject.AddComponent<GameObjectPoolManager>();
    }

    private GameObject SpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, bool worldPositionStays)
    {
        GameObjectPool pool = GetOrCreatePool(prefab, defaultPrewarmCount, defaultMaxSize, defaultAllowGrowth, defaultCollectionCheck);
        if (pool == null)
            return null;

        GameObject instance = pool.Get(position, rotation, parent, worldPositionStays);
        if (instance != null)
            prefabByInstance[instance] = prefab;

        return instance;
    }

    private bool DespawnInternal(GameObject instance)
    {
        if (instance == null)
            return true;

        if (!prefabByInstance.TryGetValue(instance, out GameObject prefab))
        {
            if (instance.TryGetComponent(out PooledObject pooledObject) && pooledObject.Prefab != null)
                prefab = pooledObject.Prefab;
        }

        if (prefab == null || !poolsByPrefab.TryGetValue(prefab, out GameObjectPool pool))
            return false;

        prefabByInstance.Remove(instance);
        pool.Release(instance);
        return true;
    }

    private GameObjectPool GetOrCreatePool(GameObject prefab, int prewarmCount, int maxSize, bool allowGrowth, bool collectionCheck)
    {
        if (poolsByPrefab.TryGetValue(prefab, out GameObjectPool existingPool))
            return existingPool;

        if (!createPoolsLazily && settings != null)
        {
            Debug.LogWarning($"Pool for {prefab.name} is not registered.", prefab);
            return null;
        }

        Transform parent = CreatePoolParent(prefab.name);
        GameObjectPool pool = new GameObjectPool(prefab, parent, prewarmCount, maxSize, allowGrowth, collectionCheck);
        poolsByPrefab.Add(prefab, pool);
        return pool;
    }

    private void InitializeFromSettings()
    {
        if (settings == null)
            return;

        IReadOnlyList<PoolEntry> entries = settings.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            PoolEntry entry = entries[i];
            if (entry == null || entry.Prefab == null || poolsByPrefab.ContainsKey(entry.Prefab))
                continue;

            Transform parent = CreatePoolParent(entry.Key);
            GameObjectPool pool = new GameObjectPool(entry.Prefab, parent, entry.PrewarmCount, entry.MaxSize, entry.AllowGrowth, entry.CollectionCheck);
            poolsByPrefab.Add(entry.Prefab, pool);
        }
    }

    private Transform CreatePoolParent(string poolName)
    {
        EnsureRoot();
        GameObject parentObject = new GameObject(string.IsNullOrWhiteSpace(poolName) ? "Pool" : poolName);
        parentObject.transform.SetParent(poolRoot, false);
        return parentObject.transform;
    }

    private void EnsureRoot()
    {
        if (poolRoot != null)
            return;

        GameObject rootObject = new GameObject("Pools");
        rootObject.transform.SetParent(transform, false);
        poolRoot = rootObject.transform;
    }
}
