using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerInventoryInput : MonoBehaviour
{
    [SerializeField] private bool useInventoryLegacySettings = true;
    [SerializeField] private NetworkInventory inventory;
    [SerializeField] private KeyCode dropKey = KeyCode.G;

    public void Initialize(NetworkInventory target, KeyCode configuredDropKey)
    {
        inventory = target != null ? target : inventory;
        if (useInventoryLegacySettings)
            dropKey = configuredDropKey;
    }

    public void CommitLegacySettings()
    {
        useInventoryLegacySettings = false;
    }

    private void Update()
    {
        if (inventory == null)
            inventory = GetComponentInParent<NetworkInventory>()
                        ?? GetComponentInChildren<NetworkInventory>(true)
                        ?? GetComponentInParent<NetworkEntityRoot>()?.GetComponentInChildren<NetworkInventory>(true);

        if (inventory == null || !inventory.CanProcessLocalInput)
            return;

        inventory.MaintainPendingDropState();
        if (EmoteWheelController.IsBlockingGameplayInput || !inventory.CanUseInventoryItems())
            return;

        if (Input.GetKeyDown(dropKey) && inventory.HeldItemId != 0)
            inventory.RequestDropHeldItem();

        if (inventory.IsDropRequestPending())
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            inventory.RequestSetHeldSlot(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            inventory.RequestSetHeldSlot(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            inventory.RequestSetHeldSlot(2);
    }
}
