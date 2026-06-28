using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkInventoryItem : NetworkBehaviour, IInteractable, IPlayerInteractable, IHoldInteractable
{
    [SerializeField] private PlayerItemSO item;
    [SerializeField] private float pickupDistance = 3f;
    [SerializeField] private float requiredHoldTime;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Collider[] colliders;

    [Networked] public NetworkBool IsCollected { get; private set; }

    public PlayerItemSO Item => item;
    public int ItemId => item != null ? item.itemId : 0;
    public float PickupDistance => pickupDistance;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);

    private void Awake()
    {
        InventoryItemRegistry.Register(item);
        EnsureReferences();
        ApplyCollectedState(false);
    }

    public override void Spawned()
    {
        InventoryItemRegistry.Register(item);
        EnsureReferences();
        ApplyCollectedState(IsCollected);
    }

    public override void Render()
    {
        ApplyCollectedState(IsCollected);
    }

    public void Interact()
    {
        Interact(FindLocalPlayer());
    }

    public void Interact(PlayerMovement player)
    {
        if (player == null)
            player = FindLocalPlayer();

        if (player == null || !IsNetworkReady() || IsCollected)
            return;

        NetworkInventory inventory = player.GetComponent<NetworkInventory>();
        if (inventory == null)
            inventory = player.GetComponentInChildren<NetworkInventory>(true);

        inventory?.RequestPickup(this);
    }

    public bool CanBePickedUpBy(NetworkInventory inventory)
    {
        if (inventory == null || !IsNetworkReady() || IsCollected || item == null || item.itemId == 0)
            return false;

        return Vector3.Distance(inventory.transform.position, transform.position) <= pickupDistance;
    }

    public bool TryCollect(NetworkInventory inventory)
    {
        if (!IsNetworkReady() || !Object.HasStateAuthority || !CanBePickedUpBy(inventory))
            return false;

        if (!inventory.TryAddItem(item.itemId, 1))
            return false;

        IsCollected = true;
        ApplyCollectedState(true);
        RPC_ApplyCollectedState(true);
        return true;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyCollectedState(NetworkBool collected)
    {
        ApplyCollectedState(collected);
    }

    private void EnsureReferences()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);
    }

    private void ApplyCollectedState(bool collected)
    {
        EnsureReferences();
        bool visible = !collected;

        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
                itemRenderer.enabled = visible;
        }

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider != null)
                itemCollider.enabled = visible;
        }
    }

    private bool IsNetworkReady()
    {
        return Object != null && Object.IsValid;
    }

    private static PlayerMovement FindLocalPlayer()
    {
        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.IsLocalNetworkPlayer)
                return player;
        }

        return null;
    }
}
