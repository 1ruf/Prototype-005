using Fusion;
using UnityEngine;

public static class InventoryDropSpawnService
{
    public static bool TrySpawnDroppedItem(
        PlayerItemSO item,
        NetworkRunner runner,
        Vector3 position,
        Quaternion rotation,
        Object logContext,
        out NetworkObject spawnedNetworkObject,
        out GameObject spawnedLocalObject)
    {
        spawnedNetworkObject = null;
        spawnedLocalObject = null;

        if (!TryValidateDropPrefab(item, runner, logContext, out GameObject itemPrefab, out NetworkObject networkPrefab))
            return false;

        if (runner != null)
        {
            spawnedNetworkObject = runner.Spawn(networkPrefab, position, rotation);
            if (spawnedNetworkObject == null)
            {
                Debug.LogWarning($"Cannot drop item {item.itemId}: Runner.Spawn failed for {itemPrefab.name}.", logContext);
                return false;
            }

            NetworkInventoryItem droppedItem = spawnedNetworkObject.GetComponent<NetworkInventoryItem>();
            droppedItem?.ResetDroppedState();
            return true;
        }

        spawnedLocalObject = Object.Instantiate(itemPrefab, position, rotation);
        NetworkInventoryItem localDroppedItem = spawnedLocalObject.GetComponent<NetworkInventoryItem>();
        localDroppedItem?.ResetDroppedState();
        return true;
    }

    public static void RollbackSpawnedDrop(NetworkRunner runner, NetworkObject spawnedNetworkObject, GameObject spawnedLocalObject)
    {
        if (spawnedNetworkObject != null && runner != null)
        {
            runner.Despawn(spawnedNetworkObject);
            return;
        }

        if (spawnedLocalObject != null)
            Object.Destroy(spawnedLocalObject);
    }

    private static bool TryValidateDropPrefab(
        PlayerItemSO item,
        NetworkRunner runner,
        Object logContext,
        out GameObject itemPrefab,
        out NetworkObject networkPrefab)
    {
        itemPrefab = null;
        networkPrefab = null;

        if (item == null || item.ItemPrefab == null)
        {
            Debug.LogWarning("Cannot drop item: item data or ItemPrefab is missing.", logContext);
            return false;
        }

        itemPrefab = item.ItemPrefab;
        NetworkInventoryItem inventoryItemPrefab = itemPrefab.GetComponent<NetworkInventoryItem>();
        if (inventoryItemPrefab == null)
        {
            Debug.LogWarning($"Cannot drop item {item.itemId}: {itemPrefab.name} has no {nameof(NetworkInventoryItem)} component.", logContext);
            return false;
        }

        networkPrefab = itemPrefab.GetComponent<NetworkObject>();
        if (runner != null && networkPrefab == null)
        {
            Debug.LogWarning($"Cannot drop item {item.itemId}: {itemPrefab.name} has no NetworkObject, so it cannot be synchronized.", logContext);
            return false;
        }

        return true;
    }
}
