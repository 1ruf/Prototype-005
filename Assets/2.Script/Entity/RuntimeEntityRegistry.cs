using System.Collections.Generic;
using UnityEngine;

public sealed class RuntimeEntityRegistry<T> where T : Object
{
    private readonly List<T> items;

    public RuntimeEntityRegistry(int capacity)
    {
        items = new List<T>(Mathf.Max(1, capacity));
    }

    public IReadOnlyList<T> Items
    {
        get
        {
            RemoveDestroyedEntries();
            return items;
        }
    }

    public void Register(T item)
    {
        if (item != null && !items.Contains(item))
            items.Add(item);
    }

    public void Unregister(T item)
    {
        if (item != null)
            items.Remove(item);
    }

    public void Clear()
    {
        items.Clear();
    }

    private void RemoveDestroyedEntries()
    {
        items.RemoveAll(item => item == null);
    }
}
