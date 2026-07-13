using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkInventory : NetworkEntityBehaviour
{
    public const int MaxSlots = 3;

    [SerializeField] private PlayerItemSO[] itemCatalog;
    [SerializeField, HideInInspector] private Transform rightHandAnchor;
    [SerializeField, HideInInspector] private KeyCode dropKey = KeyCode.G;
    [SerializeField] private float dropForwardOffset = 1.25f;
    [SerializeField] private float dropUpOffset = 0.35f;
    [SerializeField] private float dropGroundProbeHeight = 1.5f;
    [SerializeField] private float dropGroundProbeDistance = 4f;
    [SerializeField] private LayerMask dropGroundMask = ~0;
    [SerializeField] private float dropRequestTimeout = 1.25f;
    [SerializeField, HideInInspector] private Vector3 firstPersonHeldLocalPosition = new Vector3(0.26f, -0.46f, 0.62f);
    [SerializeField, HideInInspector] private Vector3 firstPersonHeldLocalEuler = new Vector3(0f, 0f, 0f);
    [SerializeField, HideInInspector] private Vector3 thirdPersonHeldLocalPosition = new Vector3(0.03f, -0.12f, 0.06f);
    [SerializeField, HideInInspector] private Vector3 thirdPersonHeldLocalEuler = new Vector3(0f, 0f, 0f);
    [SerializeField] private InventoryHeldItemPresentation heldItemPresentation;
    [SerializeField] private PlayerInventoryInput input;

    [Networked, Capacity(MaxSlots)] private NetworkArray<int> ItemIds => default;
    [Networked, Capacity(MaxSlots)] private NetworkArray<int> ItemCounts => default;
    [Networked] public int HeldItemId { get; private set; }
    [Networked] public int HeldSlotIndex { get; private set; }

    public event Action InventoryChanged;

    private int lastInventoryHash;
    private NetworkPlayerHidingComponent hidingComponent;
    private int pendingDropSlotIndex = -1;
    private int pendingDropItemId;
    private float pendingDropStartedAt;
    private bool heldSlotStoredForHiding;
    private int heldSlotBeforeHiding = -1;

    public int SlotCount => MaxSlots;
    public int HighlightedSlotIndex => IsHeldSlotLocallySuppressed() ? -1 : HeldSlotIndex;
    public bool CanProcessLocalInput => !IsNetworkObjectWaitingForSpawn() && IsLocalPlayer;

    private bool IsLocalPlayer => Object == null || Object.HasInputAuthority;

    private void Awake()
    {
        Initialize(EntityOwnerResolver.Resolve(this));
        InventoryItemRegistry.RegisterRange(itemCatalog);
    }

    public override void Initialize(GameObject entityOwner)
    {
        base.Initialize(entityOwner);
        ResolveSupportComponents();
    }

    public override void Spawned()
    {
        InventoryItemRegistry.RegisterRange(itemCatalog);
        ResolveSupportComponents();
        RefreshHeldVisual(true);
        lastInventoryHash = CalculateInventoryHash();
        InventoryChanged?.Invoke();
    }

    private void Update()
    {
        if (IsNetworkObjectWaitingForSpawn())
            return;

        if (Object != null)
            return;

        RefreshHeldVisual(false);
        CheckInventoryChanged();
        TickHeldVisualPresentation();
    }

    public override void Render()
    {
        if (IsNetworkObjectWaitingForSpawn())
            return;

        RefreshHeldVisual(false);
        CheckInventoryChanged();
        TickHeldVisualPresentation();

    }

    public bool HasItem(int itemId, int amount = 1)
    {
        return GetItemCount(itemId) >= amount;
    }

    public bool CanAddItem(int itemId, int amount = 1)
    {
        if (itemId == 0 || amount <= 0)
            return false;

        int emptySlots = 0;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] == 0 || ItemCounts[i] <= 0)
                emptySlots++;
        }

        return emptySlots >= amount;
    }

    public int GetItemCount(int itemId)
    {
        if (itemId == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] == itemId)
                count += Mathf.Max(0, ItemCounts[i]);
        }

        return count;
    }

    public bool TryGetSlot(int slotIndex, out int itemId, out int count)
    {
        itemId = 0;
        count = 0;

        if (slotIndex < 0 || slotIndex >= MaxSlots)
            return false;

        itemId = ItemIds[slotIndex];
        count = ItemCounts[slotIndex];
        return itemId != 0 && count > 0;
    }

    public bool TryAddItem(int itemId, int amount = 1)
    {
        if (!CanMutateInventory(itemId, amount))
            return false;

        if (!CanAddItem(itemId, amount))
            return false;

        int remaining = amount;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] != 0 && ItemCounts[i] > 0)
                continue;

            ItemIds.Set(i, itemId);
            ItemCounts.Set(i, 1);
            remaining--;

            if (remaining <= 0)
                break;
        }

        SetHeldIfEmpty(FindFirstSlotWithItem(itemId));
        NotifyInventoryChanged();
        return true;
    }

    public bool TryRemoveItem(int itemId, int amount = 1)
    {
        if (!CanMutateInventory(itemId, amount))
            return false;

        if (GetItemCount(itemId) < amount)
            return false;

        int remaining = amount;
        int removedSlot = -1;
        bool removedHeldSlot = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] != itemId)
                continue;

            ItemIds.Set(i, 0);
            ItemCounts.Set(i, 0);
            if (removedSlot < 0)
                removedSlot = i;
            removedHeldSlot |= i == HeldSlotIndex;
            remaining--;

            if (remaining <= 0)
                break;
        }

        if (remaining > 0)
            return false;

        CompactSlotsAndRefreshHeldSlot(removedSlot, removedHeldSlot);

        NotifyInventoryChanged();
        return true;
    }

    public void RequestPickup(NetworkInventoryItem worldItem)
    {
        if (!CanUseInventoryItems())
            return;

        if (IsDropRequestPending())
            return;

        if (worldItem == null || worldItem.Object == null)
            return;

        if (Object == null)
        {
            worldItem.TryCollect(this);
            return;
        }

        if (Object.HasStateAuthority)
        {
            worldItem.TryCollect(this);
            return;
        }

        RPC_RequestPickup(worldItem.Object.Id);
    }

    public void RequestUseOn(NetworkItemUseTarget target)
    {
        if (!CanUseInventoryItems())
            return;

        if (IsDropRequestPending())
            return;

        if (target == null || target.Object == null)
            return;

        if (Object == null)
        {
            target.TryResolve(this);
            return;
        }

        if (Object.HasStateAuthority)
        {
            target.TryResolve(this);
            return;
        }

        RPC_RequestUseOn(target.Object.Id);
    }

    public void RequestDropHeldItem()
    {
        RequestDropSlot(HeldSlotIndex);
    }

    public void RequestDropItem(int itemId)
    {
        if (!CanUseInventoryItems())
            return;

        int slotIndex = ResolveSlotIndexForItem(itemId);
        if (slotIndex < 0)
            return;

        RequestDropSlot(slotIndex);
    }

    public void RequestDropSlot(int slotIndex)
    {
        if (!CanUseInventoryItems())
            return;

        if (!IsOccupiedSlot(slotIndex))
            return;

        if (IsDropRequestPending())
            return;

        if (Object == null)
        {
            DropSlotStateAuthority(slotIndex);
            return;
        }

        if (Object.HasStateAuthority)
        {
            DropSlotStateAuthority(slotIndex);
            return;
        }

        SetPendingDrop(slotIndex);
        RPC_RequestDropSlot(slotIndex);
        RefreshHeldVisual(true);
        InventoryChanged?.Invoke();
    }

    public void RequestSetHeldItem(int itemId)
    {
        if (!CanUseInventoryItems())
            return;

        int slotIndex = itemId == 0 ? -1 : FindFirstSlotWithItem(itemId);
        if (itemId != 0 && slotIndex < 0)
            return;

        RequestSetHeldSlot(slotIndex);
    }

    public void RequestSetHeldSlot(int slotIndex)
    {
        if (!CanUseInventoryItems())
            return;

        if (IsDropRequestPending())
            return;

        if (slotIndex >= 0 && !IsOccupiedSlot(slotIndex))
            return;

        if (Object == null || Object.HasStateAuthority)
        {
            SetHeldSlotState(slotIndex);
            NotifyInventoryChanged();
            return;
        }

        RPC_RequestSetHeldSlot(slotIndex);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkId itemObjectId)
    {
        if (!CanUseInventoryItems())
            return;

        NetworkObject itemObject = Runner != null ? Runner.FindObject(itemObjectId) : null;
        NetworkInventoryItem item = itemObject != null ? itemObject.GetComponent<NetworkInventoryItem>() : null;
        item?.TryCollect(this);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestUseOn(NetworkId targetObjectId)
    {
        if (!CanUseInventoryItems())
            return;

        NetworkObject targetObject = Runner != null ? Runner.FindObject(targetObjectId) : null;
        NetworkItemUseTarget target = targetObject != null ? targetObject.GetComponent<NetworkItemUseTarget>() : null;
        target?.TryResolve(this);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestDropSlot(int slotIndex)
    {
        if (!CanUseInventoryItems())
            return;

        DropSlotStateAuthority(slotIndex);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestSetHeldSlot(int slotIndex)
    {
        if (!CanUseInventoryItems())
            return;

        if (slotIndex >= 0 && !IsOccupiedSlot(slotIndex))
            return;

        SetHeldSlotState(slotIndex);
        NotifyInventoryChanged();
    }

    private void DropSlotStateAuthority(int slotIndex)
    {
        if (!IsOccupiedSlot(slotIndex))
            return;

        int itemId = ItemIds[slotIndex];
        if (!InventoryItemRegistry.TryGet(itemId, out PlayerItemSO item))
        {
            Debug.LogWarning($"Cannot drop item {itemId}: item data is not registered.", this);
            return;
        }

        Vector3 position = ResolveDropPosition();
        Quaternion rotation = Quaternion.LookRotation(Owner.transform.forward, Vector3.up);

        if (!InventoryDropSpawnService.TrySpawnDroppedItem(item, Runner, position, rotation, this, out NetworkObject spawnedNetworkObject, out GameObject spawnedLocalObject))
            return;

        if (TryRemoveSlot(slotIndex, out _))
            return;

        InventoryDropSpawnService.RollbackSpawnedDrop(Runner, spawnedNetworkObject, spawnedLocalObject);
    }

    private bool CanMutateInventory(int itemId, int amount)
    {
        if (itemId == 0 || amount <= 0)
            return false;

        return Object == null || Object.HasStateAuthority;
    }

    private bool IsNetworkObjectWaitingForSpawn()
    {
        return GetComponentInParent<NetworkObject>() != null && (Object == null || !Object.IsValid);
    }

    private void SetHeldIfEmpty(int slotIndex)
    {
        if (HeldItemId == 0)
            SetHeldSlotState(slotIndex);
    }

    private bool TryRemoveSlot(int slotIndex, out int removedItemId)
    {
        removedItemId = 0;
        if (!CanMutateInventory(IsOccupiedSlot(slotIndex) ? ItemIds[slotIndex] : 0, 1))
            return false;

        if (!IsOccupiedSlot(slotIndex))
            return false;

        removedItemId = ItemIds[slotIndex];
        ItemIds.Set(slotIndex, 0);
        ItemCounts.Set(slotIndex, 0);
        CompactSlotsAndRefreshHeldSlot(slotIndex, slotIndex == HeldSlotIndex);
        NotifyInventoryChanged();
        return true;
    }

    private void CompactSlotsAndRefreshHeldSlot(int removedSlot, bool removedHeldSlot)
    {
        int previousHeldSlot = HeldSlotIndex;
        int previousHeldItemId = HeldItemId;

        int writeIndex = 0;
        for (int readIndex = 0; readIndex < MaxSlots; readIndex++)
        {
            int itemId = ItemIds[readIndex];
            int count = ItemCounts[readIndex];
            if (itemId == 0 || count <= 0)
                continue;

            if (writeIndex != readIndex)
            {
                ItemIds.Set(writeIndex, itemId);
                ItemCounts.Set(writeIndex, count);
                ItemIds.Set(readIndex, 0);
                ItemCounts.Set(readIndex, 0);
            }

            writeIndex++;
        }

        if (removedHeldSlot)
        {
            SetHeldSlotState(FindFirstOccupiedSlot());
            return;
        }

        if (previousHeldItemId == 0)
        {
            SetHeldSlotState(-1);
            return;
        }

        int adjustedHeldSlot = previousHeldSlot;
        if (removedSlot >= 0 && removedSlot < previousHeldSlot)
            adjustedHeldSlot--;

        if (IsOccupiedSlot(adjustedHeldSlot) && ItemIds[adjustedHeldSlot] == previousHeldItemId)
            SetHeldSlotState(adjustedHeldSlot);
        else
            SetHeldSlotState(FindFirstSlotWithItem(previousHeldItemId));
    }

    private void NotifyInventoryChanged()
    {
        lastInventoryHash = CalculateInventoryHash();
        InventoryChanged?.Invoke();
        RefreshHeldVisual(true);
    }

    private void CheckInventoryChanged()
    {
        int hash = CalculateInventoryHash();
        if (hash == lastInventoryHash)
            return;

        lastInventoryHash = hash;
        InventoryChanged?.Invoke();
    }

    private int CalculateInventoryHash()
    {
        unchecked
        {
            int hash = HeldItemId;
            hash = (hash * 397) ^ HeldSlotIndex;
            for (int i = 0; i < MaxSlots; i++)
            {
                hash = (hash * 397) ^ ItemIds[i];
                hash = (hash * 397) ^ ItemCounts[i];
            }

            return hash;
        }
    }

    private void RefreshHeldVisual(bool force)
    {
        ResolveSupportComponents();
        heldItemPresentation?.Refresh(HeldItemId, IsHeldSlotLocallySuppressed(), IsLocalPlayer, force);
    }

    private Vector3 ResolveDropPosition()
    {
        Transform ownerTransform = Owner.transform;
        Vector3 desiredPosition = ownerTransform.position + ownerTransform.forward * dropForwardOffset + Vector3.up * dropUpOffset;
        Vector3 probeOrigin = desiredPosition + Vector3.up * Mathf.Max(0f, dropGroundProbeHeight);
        float probeDistance = Mathf.Max(0.1f, dropGroundProbeHeight + dropGroundProbeDistance);

        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, probeDistance, dropGroundMask, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * 0.05f;

        return desiredPosition;
    }

    private void SetPendingDrop(int slotIndex)
    {
        pendingDropSlotIndex = slotIndex;
        pendingDropItemId = IsOccupiedSlot(slotIndex) ? ItemIds[slotIndex] : 0;
        pendingDropStartedAt = Time.unscaledTime;
    }

    internal void MaintainPendingDropState()
    {
        if (!IsDropRequestPending())
            return;

        if (!IsOccupiedSlot(pendingDropSlotIndex) || ItemIds[pendingDropSlotIndex] != pendingDropItemId)
        {
            ClearPendingDrop();
            return;
        }

        if (Time.unscaledTime - pendingDropStartedAt > Mathf.Max(0.1f, dropRequestTimeout))
            ClearPendingDrop();
    }

    internal bool IsDropRequestPending()
    {
        return pendingDropSlotIndex >= 0 && pendingDropItemId != 0;
    }

    private bool IsHeldSlotLocallySuppressed()
    {
        return IsDropRequestPending()
            && pendingDropSlotIndex == HeldSlotIndex
            && pendingDropItemId == HeldItemId;
    }

    private void ClearPendingDrop()
    {
        pendingDropSlotIndex = -1;
        pendingDropItemId = 0;
        pendingDropStartedAt = 0f;
    }

    private int ResolveSlotIndexForItem(int itemId)
    {
        if (itemId == 0)
            return -1;

        if (IsOccupiedSlot(HeldSlotIndex) && ItemIds[HeldSlotIndex] == itemId)
            return HeldSlotIndex;

        return FindFirstSlotWithItem(itemId);
    }

    private int FindFirstOccupiedSlot()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (IsOccupiedSlot(i))
                return i;
        }

        return -1;
    }

    private int FindFirstSlotWithItem(int itemId)
    {
        if (itemId == 0)
            return -1;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (IsOccupiedSlot(i) && ItemIds[i] == itemId)
                return i;
        }

        return -1;
    }

    private bool IsOccupiedSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < MaxSlots && ItemIds[slotIndex] != 0 && ItemCounts[slotIndex] > 0;
    }

    private void SetHeldSlotState(int slotIndex)
    {
        if (!IsOccupiedSlot(slotIndex))
        {
            HeldSlotIndex = -1;
            HeldItemId = 0;
            ClearPendingDrop();
            return;
        }

        HeldSlotIndex = slotIndex;
        HeldItemId = ItemIds[slotIndex];
    }

    private void TickHeldVisualPresentation()
    {
        heldItemPresentation?.TickPose(HeldItemId, IsLocalPlayer);
    }

    public void ForceStoreHeldItemForHiding()
    {
        StoreHeldSlotForHiding();

        if (Object == null || Object.HasStateAuthority)
        {
            SetHeldSlotState(-1);
            NotifyInventoryChanged();
            return;
        }

        if (Object.HasInputAuthority)
            RPC_RequestStoreHeldItemForHiding();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestStoreHeldItemForHiding()
    {
        StoreHeldSlotForHiding();
        SetHeldSlotState(-1);
        NotifyInventoryChanged();
    }

    public void RestoreHeldItemAfterHiding()
    {
        int restoreSlot = ResolveStoredHeldSlot();
        heldSlotStoredForHiding = false;
        heldSlotBeforeHiding = -1;

        if (restoreSlot < 0)
            return;

        if (Object == null || Object.HasStateAuthority)
        {
            SetHeldSlotState(restoreSlot);
            NotifyInventoryChanged();
            return;
        }

        if (Object.HasInputAuthority)
            RPC_RequestRestoreHeldItemAfterHiding(restoreSlot);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestRestoreHeldItemAfterHiding(int slotIndex)
    {
        if (!IsOccupiedSlot(slotIndex))
            return;

        SetHeldSlotState(slotIndex);
        NotifyInventoryChanged();
    }

    internal bool CanUseInventoryItems()
    {
        if (hidingComponent == null)
            hidingComponent = Owner.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        return hidingComponent == null || hidingComponent.CanUseItems;
    }

    private void ResolveSupportComponents()
    {
        GameObject entityOwner = Owner;

        if (hidingComponent == null)
            hidingComponent = entityOwner.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        if (heldItemPresentation == null)
            heldItemPresentation = entityOwner.GetComponentInChildren<InventoryHeldItemPresentation>(true);

        if (heldItemPresentation == null)
            heldItemPresentation = gameObject.AddComponent<InventoryHeldItemPresentation>();

        heldItemPresentation.Initialize(
            entityOwner,
            rightHandAnchor,
            firstPersonHeldLocalPosition,
            firstPersonHeldLocalEuler,
            thirdPersonHeldLocalPosition,
            thirdPersonHeldLocalEuler);

        if (input == null)
            input = entityOwner.GetComponentInChildren<PlayerInventoryInput>(true);

        if (input == null)
            input = gameObject.AddComponent<PlayerInventoryInput>();

        input.Initialize(this, dropKey);
    }

    private void StoreHeldSlotForHiding()
    {
        if (heldSlotStoredForHiding)
            return;

        heldSlotBeforeHiding = HeldSlotIndex;
        heldSlotStoredForHiding = true;
    }

    private int ResolveStoredHeldSlot()
    {
        if (heldSlotStoredForHiding && IsOccupiedSlot(heldSlotBeforeHiding))
            return heldSlotBeforeHiding;

        return FindFirstOccupiedSlot();
    }
}
