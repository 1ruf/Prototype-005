using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkPlayerItemHolder : NetworkBehaviour
{
    [SerializeField] private NetworkHeldItem startingItemPrefab;
    [SerializeField] private Transform firstPersonAnchor;
    [SerializeField] private Transform thirdPersonAnchor;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private float followSpeed = 28f;
    [SerializeField] private KeyCode toggleKey = KeyCode.Mouse0;

    [Networked] private NetworkBool HeldItemVisible { get; set; }
    [Networked] private NetworkBool HeldItemActive { get; set; }

    private NetworkHeldItem itemInstance;
    private bool networkSpawned;
    private bool localHeldItemVisible = true;
    private bool localHeldItemActive;
    private bool localPresentationConfigured;
    private bool lastVisible;
    private bool lastActive;
    private bool thirdPersonPresentationSuppressed;

    private bool IsLocalPlayer => Object == null || Object.HasInputAuthority;
    public bool IsLocalPlayerForPresentation => IsLocalPlayer;
    public bool IsHoldingItemForVisualPose => GetHeldItemVisible();
    public bool IsHeldItemActiveForVisualPose => GetHeldItemActive();
    public Transform HeldItemTransform => itemInstance != null ? itemInstance.transform : null;

    private void Awake()
    {
        ResolveReferences();
        EnsureItemInstance();
    }

    public override void Spawned()
    {
        networkSpawned = true;
        ResolveReferences();
        EnsureItemInstance();

        if (Object.HasStateAuthority)
        {
            HeldItemVisible = true;
            HeldItemActive = localHeldItemActive;
        }

        ConfigurePresentation(true);
        ApplyNetworkItemState(true);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        networkSpawned = false;
    }

    private void Update()
    {
        if (Object != null)
            return;

        if (Input.GetKeyDown(toggleKey))
            localHeldItemActive = !localHeldItemActive;

        ConfigurePresentation(false);
        ApplyNetworkItemState(false);
        TickItemPresentation();
    }

    public override void Render()
    {
        ConfigurePresentation(false);
        ApplyNetworkItemState(false);
        TickItemPresentation();

        if (IsLocalPlayer && Input.GetKeyDown(toggleKey))
            RequestToggleActive();
    }

    public void RequestToggleActive()
    {
        if (Object == null || !networkSpawned)
        {
            localHeldItemActive = !localHeldItemActive;
            ApplyNetworkItemState(false);
            return;
        }

        if (Object.HasStateAuthority)
        {
            HeldItemActive = !HeldItemActive;
            RPC_ApplyActiveState(HeldItemActive);
            ApplyNetworkItemState(false);
            return;
        }

        RPC_RequestSetActive(!GetHeldItemActive());
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestSetActive(NetworkBool active)
    {
        HeldItemActive = active;
        RPC_ApplyActiveState(active);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyActiveState(NetworkBool active)
    {
        localHeldItemActive = active;
        EnsureItemInstance();

        if (itemInstance != null)
            itemInstance.SetActiveState(active, true);

        lastActive = active;
    }

    private void ConfigurePresentation(bool force)
    {
        if (!force && localPresentationConfigured)
            return;

        EnsureItemInstance();
        if (itemInstance == null)
            return;

        Transform anchor = IsLocalPlayer ? firstPersonAnchor : thirdPersonAnchor;
        if (anchor == null)
            anchor = GetOwnerTransform();

        itemInstance.transform.SetParent(anchor, false);
        itemInstance.SetFirstPerson(IsLocalPlayer);
        itemInstance.transform.localPosition = IsLocalPlayer ? itemInstance.FirstPersonPosition : itemInstance.ThirdPersonPosition;
        itemInstance.transform.localRotation = IsLocalPlayer ? itemInstance.FirstPersonRotation : itemInstance.ThirdPersonRotation;
        itemInstance.transform.localScale = Vector3.one;
        localPresentationConfigured = true;
    }

    private void TickItemPresentation()
    {
        if (itemInstance == null)
            return;

        Transform anchor = IsLocalPlayer ? firstPersonAnchor : thirdPersonAnchor;
        if (anchor == null)
            return;

        Vector3 targetPosition = IsLocalPlayer ? itemInstance.FirstPersonPosition : itemInstance.ThirdPersonPosition;
        float follow = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        itemInstance.transform.localPosition = Vector3.Lerp(itemInstance.transform.localPosition, targetPosition, follow);

        bool moving = playerMovement != null && playerMovement.IsMoving;
        bool sprinting = playerMovement != null && playerMovement.IsSprinting;
        float speed = playerMovement != null ? Mathf.Max(0.01f, playerMovement.VisualAnimationSpeed) : 1f;
        itemInstance.TickPresentation(Time.deltaTime, moving, sprinting, moving ? speed : 0f, GetLookPitch());

        if (!IsLocalPlayer)
        {
            itemInstance.transform.localRotation = itemInstance.ThirdPersonRotation;
            itemInstance.transform.rotation = GetViewRotation() * itemInstance.ThirdPersonRotation;
        }
    }

    public Quaternion GetViewRotation()
    {
        return GetOwnerTransform().rotation * Quaternion.Euler(GetLookPitch(), 0f, 0f);
    }

    public bool TryGetHeldItemWorldPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (itemInstance == null)
            return false;

        position = itemInstance.transform.position;
        rotation = GetViewRotation() * itemInstance.ThirdPersonRotation;
        return true;
    }

    public void SetThirdPersonPresentationSuppressed(bool suppressed)
    {
        if (thirdPersonPresentationSuppressed == suppressed)
            return;

        thirdPersonPresentationSuppressed = suppressed;
        ApplyNetworkItemState(true);
    }

    private void ApplyNetworkItemState(bool force)
    {
        EnsureItemInstance();
        if (itemInstance == null)
            return;

        bool visible = GetHeldItemVisible();
        bool active = GetHeldItemActive();
        bool itemVisible = visible && !(thirdPersonPresentationSuppressed && !IsLocalPlayer);

        if (force || lastVisible != itemVisible)
        {
            itemInstance.SetVisible(itemVisible);
            lastVisible = itemVisible;
        }

        if (force || lastActive != active)
        {
            itemInstance.SetActiveState(active, true);
            lastActive = active;
        }
    }

    private void EnsureItemInstance()
    {
        if (itemInstance != null)
            return;

        NetworkHeldItem existingItem = GetOwnerTransform().GetComponentInChildren<NetworkHeldItem>(true);
        if (existingItem != null && existingItem.gameObject != gameObject)
        {
            itemInstance = existingItem;
            itemInstance.Initialize(this);
            return;
        }

        if (startingItemPrefab == null)
            return;

        itemInstance = Instantiate(startingItemPrefab, transform);
        itemInstance.name = startingItemPrefab.name;
        itemInstance.Initialize(this);
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>() ?? GetComponentInParent<PlayerMovement>();

        if (firstPersonAnchor == null)
        {
            Camera camera = GetOwnerTransform().GetComponentInChildren<Camera>(true);
            if (camera != null)
                firstPersonAnchor = camera.transform;
        }

        if (thirdPersonAnchor == null)
            thirdPersonAnchor = GetOwnerTransform();
    }

    private Transform GetOwnerTransform()
    {
        return playerMovement != null ? playerMovement.transform : transform;
    }

    private bool GetHeldItemVisible()
    {
        return Object != null && networkSpawned ? HeldItemVisible : localHeldItemVisible;
    }

    private bool GetHeldItemActive()
    {
        return Object != null && networkSpawned ? HeldItemActive : localHeldItemActive;
    }

    private float GetLookPitch()
    {
        if (playerMovement == null)
            return 0f;

        try
        {
            return playerMovement.CameraPitch;
        }
        catch (System.InvalidOperationException)
        {
            return 0f;
        }
    }
}
