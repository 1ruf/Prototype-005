using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkInventoryItem : NetworkBehaviour, IInteractable, IPlayerInteractable, IHoldInteractable, IInteractionFailureProvider, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string fallbackInteractionText = "Item";
    [SerializeField] private string actionText = "Pick Up";
    [SerializeField] private int interactionPriority = 100;
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
    public string InteractionText => item != null && !string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemName : fallbackInteractionText;
    public string InteractionActionText => actionText;
    public int InteractionPriority => interactionPriority;

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

    public bool TryGetInteractionFailureMessage(PlayerMovement player, out string message)
    {
        if (!IsNetworkReady())
        {
            message = "This item cannot be picked up yet.";
            return true;
        }

        if (IsCollected)
        {
            message = "This item has already been collected.";
            return true;
        }

        if (item == null || item.itemId == 0)
        {
            message = "Item data is missing.";
            return true;
        }

        if (player == null)
        {
            message = "Player not found.";
            return true;
        }

        NetworkInventory inventory = player.GetComponent<NetworkInventory>();
        if (inventory == null)
            inventory = player.GetComponentInChildren<NetworkInventory>(true);

        if (inventory == null)
        {
            message = "Inventory not found.";
            return true;
        }

        if (Vector3.Distance(inventory.transform.position, transform.position) > pickupDistance)
        {
            message = "You are too far away.";
            return true;
        }

        if (!inventory.CanAddItem(item.itemId, 1))
        {
            message = "Not enough inventory space.";
            return true;
        }

        message = null;
        return false;
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
