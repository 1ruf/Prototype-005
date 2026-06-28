using System.Collections.Generic;
using UnityEngine;

public static class InventoryItemRegistry
{
    private static readonly Dictionary<int, PlayerItemSO> ItemsById = new Dictionary<int, PlayerItemSO>();

    public static void Register(PlayerItemSO item)
    {
        if (item == null || item.itemId == 0)
            return;

        if (ItemsById.TryGetValue(item.itemId, out PlayerItemSO existing) && existing != null && existing != item)
        {
            Debug.LogWarning($"Duplicate inventory item id {item.itemId}: {existing.name} and {item.name}.");
            return;
        }

        ItemsById[item.itemId] = item;
    }

    public static void RegisterRange(IEnumerable<PlayerItemSO> items)
    {
        if (items == null)
            return;

        foreach (PlayerItemSO item in items)
            Register(item);
    }

    public static bool TryGet(int itemId, out PlayerItemSO item)
    {
        return ItemsById.TryGetValue(itemId, out item) && item != null;
    }
}
