using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public abstract class NetworkItemUseTarget : NetworkBehaviour, IInteractable, IPlayerInteractable, IHoldInteractable, IInteractionFailureProvider, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string interactionText;
    [SerializeField] private string actionText = "사용";
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

        return inventory.IsHoldingItem(RequiredItemId);
    }

    public bool TryGetInteractionFailureMessage(PlayerMovement player, out string message)
    {
        if (IsResolved)
        {
            message = "이미 해결되었습니다.";
            return true;
        }

        if (player == null)
        {
            message = "플레이어를 찾을 수 없습니다.";
            return true;
        }

        NetworkInventory inventory = player.GetComponent<NetworkInventory>();
        if (inventory == null)
            inventory = player.GetComponentInChildren<NetworkInventory>(true);

        if (inventory == null)
        {
            message = "인벤토리를 찾을 수 없습니다.";
            return true;
        }

        if (RequiredItemId == 0)
        {
            message = "필요한 아이템이 설정되지 않았습니다.";
            return true;
        }

        if (Vector3.Distance(inventory.transform.position, transform.position) > useDistance)
        {
            message = "너무 멀리 있습니다.";
            return true;
        }

        if (!inventory.IsHoldingItem(RequiredItemId))
        {
            string itemName = requiredItem != null && !string.IsNullOrWhiteSpace(requiredItem.ItemName)
                ? requiredItem.ItemName
                : "필요한 아이템";
            message = $"{itemName}을(를) 손에 들어야 합니다.";
            return true;
        }

        message = null;
        return false;
    }

    public bool TryResolve(NetworkInventory inventory)
    {
        if (!Object.HasStateAuthority || !CanUse(inventory))
            return false;

        if (consumeItem && !inventory.TryConsumeHeldItem(RequiredItemId))
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
