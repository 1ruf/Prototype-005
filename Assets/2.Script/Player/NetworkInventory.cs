using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkInventory : NetworkBehaviour, INetworkEntityComponent
{
    private const int MaxSlots = 16;

    [SerializeField] private PlayerItemSO[] itemCatalog;
    [SerializeField] private Transform rightHandAnchor;
    [SerializeField] private KeyCode dropKey = KeyCode.G;
    [SerializeField] private float dropForwardOffset = 1.25f;
    [SerializeField] private float dropUpOffset = 0.35f;
    [Header("Held Visual Pose")]
    [SerializeField] private Vector3 firstPersonHeldLocalPosition = new Vector3(0.26f, -0.46f, 0.62f);
    [SerializeField] private Vector3 firstPersonHeldLocalEuler = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 thirdPersonHeldLocalPosition = new Vector3(0.03f, -0.12f, 0.06f);
    [SerializeField] private Vector3 thirdPersonHeldLocalEuler = new Vector3(0f, 0f, 0f);

    [Networked, Capacity(MaxSlots)] private NetworkArray<int> ItemIds => default;
    [Networked, Capacity(MaxSlots)] private NetworkArray<int> ItemCounts => default;
    [Networked] public int HeldItemId { get; private set; }

    public event Action InventoryChanged;

    private GameObject heldVisualInstance;
    private int visibleHeldItemId = int.MinValue;
    private int lastInventoryHash;
    private NetworkPlayerHidingComponent hidingComponent;
    private GameObject owner;

    public GameObject Owner => owner != null ? owner : gameObject;

    private bool IsLocalPlayer => Object == null || Object.HasInputAuthority;

    private void Awake()
    {
        Initialize(gameObject);
        InventoryItemRegistry.RegisterRange(itemCatalog);
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
        ResolveRightHandAnchor();
    }

    public override void Spawned()
    {
        InventoryItemRegistry.RegisterRange(itemCatalog);
        ResolveRightHandAnchor();
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

        if (!EmoteWheelController.IsBlockingGameplayInput && CanUseInventoryItems() && Input.GetKeyDown(dropKey) && HeldItemId != 0)
            RequestDropHeldItem();

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

        if (!EmoteWheelController.IsBlockingGameplayInput && IsLocalPlayer && CanUseInventoryItems() && Input.GetKeyDown(dropKey) && HeldItemId != 0)
            RequestDropHeldItem();
    }

    public bool HasItem(int itemId, int amount = 1)
    {
        return GetItemCount(itemId) >= amount;
    }

    public bool CanAddItem(int itemId, int amount = 1)
    {
        if (itemId == 0 || amount <= 0)
            return false;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] == itemId || ItemIds[i] == 0)
                return true;
        }

        return false;
    }

    public int GetItemCount(int itemId)
    {
        if (itemId == 0)
            return 0;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] == itemId)
                return ItemCounts[i];
        }

        return 0;
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

        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] == itemId)
            {
                ItemCounts.Set(i, ItemCounts[i] + amount);
                SetHeldIfEmpty(itemId);
                NotifyInventoryChanged();
                return true;
            }
        }

        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] == 0)
            {
                ItemIds.Set(i, itemId);
                ItemCounts.Set(i, amount);
                SetHeldIfEmpty(itemId);
                NotifyInventoryChanged();
                return true;
            }
        }

        return false;
    }

    public bool TryRemoveItem(int itemId, int amount = 1)
    {
        if (!CanMutateInventory(itemId, amount))
            return false;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] != itemId)
                continue;

            int currentCount = ItemCounts[i];
            if (currentCount < amount)
                return false;

            int nextCount = currentCount - amount;
            ItemCounts.Set(i, nextCount);
            if (nextCount <= 0)
            {
                ItemIds.Set(i, 0);
                ItemCounts.Set(i, 0);

                if (HeldItemId == itemId)
                    HeldItemId = FindFirstItemId();
            }

            NotifyInventoryChanged();
            return true;
        }

        return false;
    }

    public void RequestPickup(NetworkInventoryItem worldItem)
    {
        if (!CanUseInventoryItems())
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
        RequestDropItem(HeldItemId);
    }

    public void RequestDropItem(int itemId)
    {
        if (!CanUseInventoryItems())
            return;

        if (itemId == 0)
            return;

        if (Object == null)
        {
            DropItemStateAuthority(itemId);
            return;
        }

        if (Object.HasStateAuthority)
        {
            DropItemStateAuthority(itemId);
            return;
        }

        RPC_RequestDropItem(itemId);
    }

    public void RequestSetHeldItem(int itemId)
    {
        if (!CanUseInventoryItems())
            return;

        if (itemId != 0 && !HasItem(itemId))
            return;

        if (Object == null || Object.HasStateAuthority)
        {
            HeldItemId = itemId;
            NotifyInventoryChanged();
            return;
        }

        RPC_RequestSetHeldItem(itemId);
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
    private void RPC_RequestDropItem(int itemId)
    {
        if (!CanUseInventoryItems())
            return;

        DropItemStateAuthority(itemId);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestSetHeldItem(int itemId)
    {
        if (!CanUseInventoryItems())
            return;

        if (itemId != 0 && !HasItem(itemId))
            return;

        HeldItemId = itemId;
        NotifyInventoryChanged();
    }

    private void DropItemStateAuthority(int itemId)
    {
        if (itemId == 0)
            return;

        if (!InventoryItemRegistry.TryGet(itemId, out PlayerItemSO item) || item.ItemPrefab == null)
            return;

        if (!TryRemoveItem(itemId, 1))
            return;

        Vector3 position = transform.position + transform.forward * dropForwardOffset + Vector3.up * dropUpOffset;
        Quaternion rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
        NetworkObject prefabNetworkObject = item.ItemPrefab.GetComponent<NetworkObject>();

        if (Runner != null && prefabNetworkObject != null)
        {
            Runner.Spawn(prefabNetworkObject, position, rotation);
            return;
        }

        Instantiate(item.ItemPrefab, position, rotation);
    }

    private bool CanMutateInventory(int itemId, int amount)
    {
        if (itemId == 0 || amount <= 0)
            return false;

        return Object == null || Object.HasStateAuthority;
    }

    private bool IsNetworkObjectWaitingForSpawn()
    {
        return GetComponent<NetworkObject>() != null && (Object == null || !Object.IsValid);
    }

    private void SetHeldIfEmpty(int itemId)
    {
        if (HeldItemId == 0)
            HeldItemId = itemId;
    }

    private int FindFirstItemId()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (ItemIds[i] != 0 && ItemCounts[i] > 0)
                return ItemIds[i];
        }

        return 0;
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
        if (!force && visibleHeldItemId == HeldItemId)
            return;

        visibleHeldItemId = HeldItemId;

        if (heldVisualInstance != null)
        {
            Destroy(heldVisualInstance);
            heldVisualInstance = null;
        }

        if (HeldItemId == 0 || !InventoryItemRegistry.TryGet(HeldItemId, out PlayerItemSO item))
            return;

        GameObject prefab = item.HeldVisualPrefab != null ? item.HeldVisualPrefab : item.ItemPrefab;
        if (prefab == null)
            return;

        ResolveRightHandAnchor();
        Transform anchor = rightHandAnchor != null ? rightHandAnchor : transform;
        if (IsLocalPlayer)
            anchor = ResolveFirstPersonHeldAnchor();

        heldVisualInstance = Instantiate(prefab, anchor);
        heldVisualInstance.name = prefab.name + "_HeldVisual";
        ApplyHeldVisualLocalPose(item);
        heldVisualInstance.transform.localScale = Vector3.one;
        StripHeldVisualGameplay(heldVisualInstance);
    }

    private void TickHeldVisualPresentation()
    {
        if (heldVisualInstance == null || HeldItemId == 0 || !InventoryItemRegistry.TryGet(HeldItemId, out PlayerItemSO item))
            return;

        ApplyHeldVisualLocalPose(item);
    }

    private void ApplyHeldVisualLocalPose(PlayerItemSO item)
    {
        if (heldVisualInstance == null)
            return;

        Vector3 itemOffset = item != null ? item.localPosition : Vector3.zero;
        Vector3 basePosition = IsLocalPlayer ? firstPersonHeldLocalPosition : thirdPersonHeldLocalPosition;
        Vector3 baseEuler = IsLocalPlayer ? firstPersonHeldLocalEuler : thirdPersonHeldLocalEuler;

        heldVisualInstance.transform.localPosition = basePosition + itemOffset;
        heldVisualInstance.transform.localRotation = Quaternion.Euler(baseEuler);
    }

    private Transform ResolveFirstPersonHeldAnchor()
    {
        Camera camera = Camera.main;
        if (camera != null)
            return camera.transform;

        Camera ownerCamera = Owner != null ? Owner.GetComponentInChildren<Camera>(true) : null;
        return ownerCamera != null ? ownerCamera.transform : transform;
    }

    private void StripHeldVisualGameplay(GameObject visual)
    {
        NetworkObject networkObject = visual.GetComponent<NetworkObject>();
        if (networkObject != null)
            networkObject.enabled = false;

        foreach (Collider itemCollider in visual.GetComponentsInChildren<Collider>(true))
            itemCollider.enabled = false;

        foreach (NetworkBehaviour networkBehaviour in visual.GetComponentsInChildren<NetworkBehaviour>(true))
            networkBehaviour.enabled = false;

        foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
    }

    private void ResolveRightHandAnchor()
    {
        if (hidingComponent == null)
            hidingComponent = GetComponent<NetworkPlayerHidingComponent>() ?? GetComponentInParent<NetworkPlayerHidingComponent>();

        if (rightHandAnchor != null)
            return;

        Transform visual = FindChildByName(transform, "RightHand") ??
                           FindChildByName(transform, "Right Hand") ??
                           FindChildByName(transform, "Hand_R") ??
                           FindChildByName(transform, "mixamorig:RightHand");

        rightHandAnchor = visual != null ? visual : transform;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    public void ForceStoreHeldItemForHiding()
    {
        if (Object == null || Object.HasStateAuthority)
        {
            HeldItemId = 0;
            NotifyInventoryChanged();
            return;
        }

        if (Object.HasInputAuthority)
            RPC_RequestStoreHeldItemForHiding();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestStoreHeldItemForHiding()
    {
        HeldItemId = 0;
        NotifyInventoryChanged();
    }

    private bool CanUseInventoryItems()
    {
        if (hidingComponent == null)
            hidingComponent = GetComponent<NetworkPlayerHidingComponent>() ?? GetComponentInParent<NetworkPlayerHidingComponent>();

        return hidingComponent == null || hidingComponent.CanUseItems;
    }
}
