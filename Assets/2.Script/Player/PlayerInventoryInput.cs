using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerInventoryInput : MonoBehaviour
{
    [SerializeField] private NetworkInventory inventory;

    public void Initialize(NetworkInventory target)
    {
        inventory = target != null ? target : inventory;
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

        if (Input.GetKeyDown(KeyCode.Q) && inventory.HeldItemId != 0)
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
