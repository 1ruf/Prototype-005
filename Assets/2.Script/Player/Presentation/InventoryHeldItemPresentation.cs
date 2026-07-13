using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class InventoryHeldItemPresentation : MonoBehaviour
{
    [SerializeField] private bool useInventoryLegacySettings = true;
    [SerializeField] private Transform rightHandAnchor;
    [SerializeField] private Vector3 firstPersonLocalPosition = new(0.26f, -0.46f, 0.62f);
    [SerializeField] private Vector3 firstPersonLocalEuler;
    [SerializeField] private Vector3 thirdPersonLocalPosition = new(0.03f, -0.12f, 0.06f);
    [SerializeField] private Vector3 thirdPersonLocalEuler;

    private GameObject owner;
    private GameObject heldVisualInstance;
    private int visibleHeldItemId = int.MinValue;

    public void Initialize(
        GameObject entityOwner,
        Transform handAnchor = null,
        Vector3? firstPersonPosition = null,
        Vector3? firstPersonEuler = null,
        Vector3? thirdPersonPosition = null,
        Vector3? thirdPersonEuler = null)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
        if (useInventoryLegacySettings)
        {
            rightHandAnchor = handAnchor != null ? handAnchor : rightHandAnchor;
            firstPersonLocalPosition = firstPersonPosition ?? firstPersonLocalPosition;
            firstPersonLocalEuler = firstPersonEuler ?? firstPersonLocalEuler;
            thirdPersonLocalPosition = thirdPersonPosition ?? thirdPersonLocalPosition;
            thirdPersonLocalEuler = thirdPersonEuler ?? thirdPersonLocalEuler;
        }

        ResolveRightHandAnchor();
    }

    public void CommitLegacySettings()
    {
        useInventoryLegacySettings = false;
    }

    public void Refresh(int heldItemId, bool locallySuppressed, bool isLocalPlayer, bool force)
    {
        int presentedItemId = locallySuppressed ? 0 : heldItemId;
        if (!force && visibleHeldItemId == presentedItemId)
            return;

        visibleHeldItemId = presentedItemId;
        ReleaseVisual();

        if (presentedItemId == 0 || !InventoryItemRegistry.TryGet(presentedItemId, out PlayerItemSO item))
            return;

        GameObject prefab = item.HeldVisualPrefab != null ? item.HeldVisualPrefab : item.ItemPrefab;
        if (prefab == null)
            return;

        ResolveRightHandAnchor();
        Transform anchor = isLocalPlayer ? ResolveFirstPersonAnchor() : rightHandAnchor;
        if (anchor == null)
            anchor = owner != null ? owner.transform : transform;

        heldVisualInstance = Instantiate(prefab, anchor);
        heldVisualInstance.name = prefab.name + "_HeldVisual";
        heldVisualInstance.transform.localScale = Vector3.one;
        StripGameplayComponents(heldVisualInstance);
        TickPose(heldItemId, isLocalPlayer);
    }

    public void TickPose(int heldItemId, bool isLocalPlayer)
    {
        if (heldVisualInstance == null || heldItemId == 0 || !InventoryItemRegistry.TryGet(heldItemId, out PlayerItemSO item))
            return;

        Vector3 basePosition = isLocalPlayer ? firstPersonLocalPosition : thirdPersonLocalPosition;
        Vector3 baseEuler = isLocalPlayer ? firstPersonLocalEuler : thirdPersonLocalEuler;
        heldVisualInstance.transform.localPosition = basePosition + item.LocalPosition;
        heldVisualInstance.transform.localRotation = Quaternion.Euler(baseEuler);
    }

    private void OnDestroy()
    {
        ReleaseVisual();
    }

    private void ReleaseVisual()
    {
        if (heldVisualInstance == null)
            return;

        Destroy(heldVisualInstance);
        heldVisualInstance = null;
    }

    private Transform ResolveFirstPersonAnchor()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera.transform;

        Camera ownerCamera = owner != null ? owner.GetComponentInChildren<Camera>(true) : null;
        return ownerCamera != null ? ownerCamera.transform : owner != null ? owner.transform : transform;
    }

    private void ResolveRightHandAnchor()
    {
        if (rightHandAnchor != null)
            return;

        Transform root = owner != null ? owner.transform : transform;
        rightHandAnchor = FindChildByName(root, "RightHand")
                          ?? FindChildByName(root, "Right Hand")
                          ?? FindChildByName(root, "Hand_R")
                          ?? FindChildByName(root, "mixamorig:RightHand")
                          ?? root;
    }

    private static void StripGameplayComponents(GameObject visual)
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
}
