using System;
using UnityEngine;

[Serializable]
public sealed class PoolEntry
{
    [SerializeField] private string key;
    [SerializeField] private GameObject prefab;
    [SerializeField, Min(0)] private int prewarmCount = 8;
    [SerializeField, Min(1)] private int maxSize = 64;
    [SerializeField] private bool allowGrowth = true;
    [SerializeField] private bool collectionCheck = true;

    public string Key => string.IsNullOrWhiteSpace(key) && prefab != null ? prefab.name : key;
    public GameObject Prefab => prefab;
    public int PrewarmCount => Mathf.Max(0, prewarmCount);
    public int MaxSize => Mathf.Max(1, maxSize);
    public bool AllowGrowth => allowGrowth;
    public bool CollectionCheck => collectionCheck;
}
