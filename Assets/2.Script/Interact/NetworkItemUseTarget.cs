using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public abstract class NetworkItemUseTarget : NetworkBehaviour, IInteractable, IPlayerInteractable, IHoldInteractable, IInteractionFailureProvider, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string interactionText;
    [SerializeField] private string actionText = "Use";
    [SerializeField] private int interactionPriority = 80;
    [SerializeField] private PlayerItemSO requiredItem;
    [SerializeField] private float useDistance = 3f;
    [SerializeField] private float requiredHoldTime;
    [SerializeField] private bool consumeItem = true;

    [Networked] public NetworkBool IsResolved { get; private set; }

    public PlayerItemSO RequiredItem => requiredItem;
    public int RequiredItemId => requiredItem != null ? requiredItem.itemId : 0;
    public float UseDistance => useDistance;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);
    public string InteractionText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(interactionText))
                return interactionText;

            return gameObject.name;
        }
    }
    public string InteractionActionText => actionText;
    public int InteractionPriority => interactionPriority;

    protected virtual void Awake()
    {
        InventoryItemRegistry.Register(requiredItem);
    }

    public override void Spawned()
    {
        InventoryItemRegistry.Register(requiredItem);
        ApplyResolvedState(IsResolved);

        if (IsResolved)
            OnResolvedVisual();
    }

    public override void Render()
    {
        ApplyResolvedState(IsResolved);
    }

    public void Interact()
    {
        Interact(FindLocalPlayer());
    }

    public void Interact(PlayerMovement player)
    {
        if (player == null)
            player = FindLocalPlayer();

        if (player == null || IsResolved)
            return;

        NetworkInventory inventory = player.GetComponent<NetworkInventory>();
        if (inventory == null)
            inventory = player.GetComponentInChildren<NetworkInventory>(true);

        inventory?.RequestUseOn(this);
    }

    public bool CanUse(NetworkInventory inventory)
    {
        if (inventory == null || IsResolved || RequiredItemId == 0)
            return false;

        if (Vector3.Distance(inventory.transform.position, transform.position) > useDistance)
            return false;

        return inventory.HasItem(RequiredItemId, 1);
    }

    public bool TryGetInteractionFailureMessage(PlayerMovement player, out string message)
    {
        if (IsResolved)
        {
            message = "This has already been resolved.";
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

        if (RequiredItemId == 0)
        {
            message = "Required item is not set.";
            return true;
        }

        if (Vector3.Distance(inventory.transform.position, transform.position) > useDistance)
        {
            message = "You are too far away.";
            return true;
        }

        if (!inventory.HasItem(RequiredItemId, 1))
        {
            string itemName = requiredItem != null && !string.IsNullOrWhiteSpace(requiredItem.ItemName)
                ? requiredItem.ItemName
                : "Required item";
            message = $"{itemName} is required.";
            return true;
        }

        message = null;
        return false;
    }

    public bool TryResolve(NetworkInventory inventory)
    {
        if (!Object.HasStateAuthority || !CanUse(inventory))
            return false;

        if (consumeItem && !inventory.TryRemoveItem(RequiredItemId, 1))
            return false;

        IsResolved = true;
        OnResolvedByItem(requiredItem, inventory);
        ApplyResolvedState(true);
        RPC_ApplyResolvedState(true);
        OnResolvedVisual();
        return true;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyResolvedState(NetworkBool resolved)
    {
        ApplyResolvedState(resolved);

        if (resolved)
            OnResolvedVisual();
    }

    protected virtual void ApplyResolvedState(bool resolved)
    {
    }

    protected virtual void OnResolvedVisual()
    {
    }

    protected abstract void OnResolvedByItem(PlayerItemSO usedItem, NetworkInventory inventory);

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
