using UnityEngine;
using UnityEngine.InputSystem;

public class ScreenManager : MonoBehaviour
{
    [SerializeField] private float _interactableRadius;
    [SerializeField] private LayerMask _interactableLayerMask = ~0;
    [SerializeField] private LayerMask _interactionBlockerLayerMask = ~0;
    [SerializeField] private bool _debugInteractionRay;
    [SerializeField] private Texture2D _mouseCursor;
    [SerializeField] private GameObject _lockedMouseCursor;

    private PlayerMovement localPlayer;
    private Camera localCamera;
    private bool hasLocalPlayer;
    private float nextInteractionRayDebugTime;
    private Component holdTarget;
    private float holdElapsedTime;
    private bool holdInteractionTriggered;

    private struct InteractionCandidate
    {
        public Component TargetComponent;
        public IPlayerInteractable PlayerInteraction;
        public IInteractable Interaction;
        public RaycastHit HitInfo;
        public int Priority;
        public string TargetName;
        public string ActionText;
    }

    private void OnEnable()
    {
        MousepointerLock(true);
        SetCursor(_mouseCursor);
    }

    public void SetLocalPlayer(PlayerMovement player)
    {
        localPlayer = player;
        hasLocalPlayer = player != null;
        localCamera = Camera.main;

        if (localCamera != null)
            localCamera.enabled = true;
    }

    public void ClearLocalPlayer(PlayerMovement player)
    {
        if (localPlayer != player)
            return;

        localPlayer = null;
        localCamera = null;
        hasLocalPlayer = false;
        Highlight(false);
    }

    public void MousepointerLock(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            return;
        }
        Cursor.lockState = CursorLockMode.None;
    }
    public void SetCursor(Texture2D cursorTexture)
    {
        Cursor.SetCursor(cursorTexture, Vector2.zero, CursorMode.ForceSoftware);
    }

    private void Update()
    {
        ReverseRealCursorVisible();

        if (!ResolveLocalCamera())
        {
            Highlight(false);
            return;
        }

        if (HandleHiddenPlayerExitInput())
            return;

        if (IsLocalPlayerInteractionBlocked())
        {
            Highlight(false);
            return;
        }

        CheckInteract();
    }

    private bool HandleHiddenPlayerExitInput()
    {
        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
            return false;

        ResolveLocalPlayer();
        if (localPlayer == null)
            return false;

        NetworkPlayerHidingComponent hiding = ResolveLocalHidingComponent();
        if (hiding == null || !hiding.CanRequestExit)
            return false;

        ResetHoldInteraction(null, false);
        Highlight(false);
        hiding.RequestExit();
        return true;
    }

    private bool IsLocalPlayerInteractionBlocked()
    {
        ResolveLocalPlayer();
        NetworkPlayerHidingComponent hiding = ResolveLocalHidingComponent();
        return hiding != null && hiding.BlocksPlayerInput;
    }

    private NetworkPlayerHidingComponent ResolveLocalHidingComponent()
    {
        if (localPlayer == null)
            return null;

        NetworkPlayerHidingComponent hiding = localPlayer.GetComponent<NetworkPlayerHidingComponent>();
        if (hiding == null)
            hiding = localPlayer.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        return hiding;
    }

    private void CheckInteract()
    {
        ResolveLocalPlayer();

        Ray ray = new Ray(localCamera.transform.position, localCamera.transform.forward);
        int raycastMask = _interactableLayerMask | _interactionBlockerLayerMask;
        RaycastHit[] hits = Physics.RaycastAll(ray, _interactableRadius, raycastMask);

        if (hits.Length > 0)
        {
            float blockerDistance = FindNearestInteractionBlockerDistance(hits);
            if (TrySelectInteractionCandidate(hits, blockerDistance, out InteractionCandidate candidate))
            {
                DebugInteractionRay(ray, true, candidate.HitInfo);
                HandleInteraction(candidate.TargetComponent, candidate.PlayerInteraction, candidate.Interaction);
                Highlight(true, candidate.TargetName, candidate.ActionText);
                return;
            }

            DebugInteractionRay(ray, true, GetNearestHit(hits));
        }
        else
        {
            DebugInteractionRay(ray, false, default);
        }
        Highlight(false);
    }

    private bool TrySelectInteractionCandidate(RaycastHit[] hits, float blockerDistance, out InteractionCandidate selectedCandidate)
    {
        selectedCandidate = default;
        bool hasCandidate = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance > blockerDistance)
                continue;

            if (!TryBuildInteractionCandidate(hit, out InteractionCandidate candidate))
                continue;

            if (!CanInteractAtCurrentDistance(candidate.TargetComponent))
                continue;

            if (!hasCandidate || IsBetterInteractionCandidate(candidate, selectedCandidate))
            {
                selectedCandidate = candidate;
                hasCandidate = true;
            }
        }

        return hasCandidate;
    }

    private float FindNearestInteractionBlockerDistance(RaycastHit[] hits)
    {
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.transform;
            if (hitTransform == null)
                continue;

            if (!IsLayerInMask(hitTransform.gameObject.layer, _interactionBlockerLayerMask))
                continue;

            if (IsLocalPlayerHit(hitTransform))
                continue;

            if (TryBuildInteractionCandidate(hit, out _))
                continue;

            if (hit.distance < nearestDistance)
                nearestDistance = hit.distance;
        }

        return nearestDistance;
    }

    private bool TryBuildInteractionCandidate(RaycastHit hitInfo, out InteractionCandidate candidate)
    {
        candidate = default;
        Transform hitTransform = hitInfo.transform;
        if (hitTransform == null)
            return false;

        if (!IsLayerInMask(hitTransform.gameObject.layer, _interactableLayerMask))
            return false;

        IPlayerInteractable playerInteraction = hitTransform.GetComponentInParent<IPlayerInteractable>();
        if (playerInteraction != null)
        {
            Component targetComponent = playerInteraction as Component;
            if (targetComponent == null)
                return false;

            candidate = new InteractionCandidate
            {
                TargetComponent = targetComponent,
                PlayerInteraction = playerInteraction,
                Interaction = null,
                HitInfo = hitInfo,
                Priority = ResolveInteractionPriority(targetComponent),
                TargetName = ResolveInteractionTargetName(targetComponent),
                ActionText = ResolveInteractionActionText(targetComponent)
            };
            return true;
        }

        IInteractable interaction = hitTransform.GetComponentInParent<IInteractable>();
        if (interaction == null)
            return false;

        Component interactionComponent = interaction as Component;
        if (interactionComponent == null)
            return false;

        candidate = new InteractionCandidate
        {
            TargetComponent = interactionComponent,
            PlayerInteraction = null,
            Interaction = interaction,
            HitInfo = hitInfo,
            Priority = ResolveInteractionPriority(interactionComponent),
            TargetName = ResolveInteractionTargetName(interactionComponent),
            ActionText = ResolveInteractionActionText(interactionComponent)
        };
        return true;
    }

    private bool IsLocalPlayerHit(Transform hitTransform)
    {
        return localPlayer != null && hitTransform.IsChildOf(localPlayer.transform);
    }

    private static bool IsLayerInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private static bool IsBetterInteractionCandidate(InteractionCandidate candidate, InteractionCandidate current)
    {
        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;

        return candidate.HitInfo.distance < current.HitInfo.distance;
    }

    private static int ResolveInteractionPriority(Component targetComponent)
    {
        IInteractionPriority priority = targetComponent.GetComponentInParent<IInteractionPriority>();
        return priority != null ? priority.InteractionPriority : 0;
    }

    private static string ResolveInteractionTargetName(Component targetComponent)
    {
        IInteractionPrompt prompt = targetComponent.GetComponentInParent<IInteractionPrompt>();
        if (prompt != null && !string.IsNullOrWhiteSpace(prompt.InteractionText))
            return prompt.InteractionText;

        return targetComponent.gameObject.name;
    }

    private static string ResolveInteractionActionText(Component targetComponent)
    {
        IInteractionActionPrompt prompt = targetComponent.GetComponentInParent<IInteractionActionPrompt>();
        if (prompt != null && !string.IsNullOrWhiteSpace(prompt.InteractionActionText))
            return prompt.InteractionActionText;

        return "Interact";
    }

    private static RaycastHit GetNearestHit(RaycastHit[] hits)
    {
        RaycastHit nearest = hits[0];
        for (int i = 1; i < hits.Length; i++)
        {
            if (hits[i].distance < nearest.distance)
                nearest = hits[i];
        }

        return nearest;
    }

    private void HandleInteraction(Component targetComponent, IPlayerInteractable playerInteraction, IInteractable interaction)
    {
        if (targetComponent == null)
        {
            ResetHoldInteraction();
            return;
        }

        if (TryShowInteractionFailure(targetComponent))
        {
            ResetHoldInteraction(targetComponent, ResolveRequiredHoldTime(targetComponent) > 0f);
            return;
        }

        float requiredHoldTime = ResolveRequiredHoldTime(targetComponent);
        if (requiredHoldTime <= 0f)
        {
            ResetHoldInteraction(null, false);
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                InvokeInteraction(playerInteraction, interaction);
            return;
        }

        if (holdTarget != targetComponent)
            ResetHoldInteraction(targetComponent, true);
        else
            SetInteractProgressVisible(true);

        if (Keyboard.current == null || !Keyboard.current.eKey.isPressed)
        {
            ResetHoldInteraction(targetComponent, true);
            return;
        }

        if (holdInteractionTriggered)
        {
            SetInteractTime(1f);
            return;
        }

        holdElapsedTime += Time.deltaTime;
        float progress = Mathf.Clamp01(holdElapsedTime / requiredHoldTime);
        SetInteractTime(progress);

        if (progress < 1f)
            return;

        holdInteractionTriggered = true;
        InvokeInteraction(playerInteraction, interaction);
    }

    private bool TryShowInteractionFailure(Component targetComponent)
    {
        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
            return false;

        IInteractionFailureProvider failureProvider = targetComponent.GetComponentInParent<IInteractionFailureProvider>();
        if (failureProvider == null)
            return false;

        if (!failureProvider.TryGetInteractionFailureMessage(localPlayer, out string message))
            return false;

        MessageController.TryShowFailure(message);
        return true;
    }

    private static float ResolveRequiredHoldTime(Component targetComponent)
    {
        IHoldInteractable holdInteractable = targetComponent.GetComponentInParent<IHoldInteractable>();
        return holdInteractable != null ? holdInteractable.RequiredHoldTime : 0f;
    }

    private void InvokeInteraction(IPlayerInteractable playerInteraction, IInteractable interaction)
    {
        if (playerInteraction != null)
        {
            playerInteraction.Interact(localPlayer);
            return;
        }

        interaction?.Interact();
    }

    private bool CanInteractAtCurrentDistance(Component targetComponent)
    {
        if (targetComponent == null || localPlayer == null)
            return true;

        NetworkItemUseTarget itemUseTarget = targetComponent.GetComponentInParent<NetworkItemUseTarget>();
        if (itemUseTarget != null)
            return Vector3.Distance(localPlayer.transform.position, itemUseTarget.transform.position) <= itemUseTarget.UseDistance;

        NetworkInventoryItem inventoryItem = targetComponent.GetComponentInParent<NetworkInventoryItem>();
        if (inventoryItem != null)
            return Vector3.Distance(localPlayer.transform.position, inventoryItem.transform.position) <= inventoryItem.PickupDistance;

        return true;
    }

    private void DebugInteractionRay(Ray ray, bool hasHit, RaycastHit hitInfo)
    {
        if (!_debugInteractionRay)
            return;

        Color rayColor = hasHit ? Color.green : Color.red;
        Debug.DrawRay(ray.origin, ray.direction * _interactableRadius, rayColor);

        if (Time.unscaledTime < nextInteractionRayDebugTime)
            return;

        nextInteractionRayDebugTime = Time.unscaledTime + 0.25f;

        string cameraName = localCamera != null ? localCamera.name : "null";
        string playerName = localPlayer != null ? localPlayer.name : "null";
        if (!hasHit)
        {
            string unmaskedHit = TryGetUnmaskedHitDescription(ray);
            Debug.Log($"ScreenManager InteractionRay: no masked hit. camera={cameraName}, player={playerName}, origin={ray.origin}, direction={ray.direction}, distance={_interactableRadius}, mask={_interactableLayerMask.value}, firstUnmaskedHit={unmaskedHit}.");
            return;
        }

        Transform hitTransform = hitInfo.transform;
        IPlayerInteractable playerInteraction = hitTransform.GetComponentInParent<IPlayerInteractable>();
        IInteractable interaction = hitTransform.GetComponentInParent<IInteractable>();
        string unmaskedFirstHit = TryGetUnmaskedHitDescription(ray);
        Debug.Log($"ScreenManager InteractionRay: maskedHit={GetHitDescription(hitInfo)}, hasPlayerInteractable={playerInteraction != null}, hasInteractable={interaction != null}, camera={cameraName}, player={playerName}, mask={_interactableLayerMask.value}, firstUnmaskedHit={unmaskedFirstHit}.");
    }

    private string TryGetUnmaskedHitDescription(Ray ray)
    {
        if (!Physics.Raycast(ray, out RaycastHit anyHit, _interactableRadius, ~0))
            return "none";

        return GetHitDescription(anyHit);
    }

    private static string GetHitDescription(RaycastHit hitInfo)
    {
        Transform hitTransform = hitInfo.transform;
        if (hitTransform == null)
            return "null";

        return $"{GetHierarchyPath(hitTransform)}, root={hitTransform.root.name}, layer={LayerMask.LayerToName(hitTransform.gameObject.layer)}({hitTransform.gameObject.layer}), distance={hitInfo.distance:F2}";
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return "null";

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private void Highlight(bool value, string targetName = null, string actionText = null)
    {
        if (Manager.Instance == null || Manager.Instance.UIManager == null)
            return;

        Manager.Instance.UIManager.SetInteractUI(value, targetName, actionText);

        if (!value)
            ResetHoldInteraction();
    }

    private void SetInteractTime(float amount)
    {
        if (Manager.Instance == null || Manager.Instance.UIManager == null)
            return;

        Manager.Instance.UIManager.SetInteractTime(amount);
    }

    private void SetInteractProgressVisible(bool value)
    {
        if (Manager.Instance == null || Manager.Instance.UIManager == null)
            return;

        Manager.Instance.UIManager.SetInteractProgressVisible(value);
    }

    private void ResetHoldInteraction(Component nextTarget = null, bool showProgress = false)
    {
        holdTarget = nextTarget;
        holdElapsedTime = 0f;
        holdInteractionTriggered = false;
        SetInteractTime(0f);
        SetInteractProgressVisible(showProgress);
    }

    public void ReverseRealCursorVisible()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.visible = false;
            if (_lockedMouseCursor != null)
                _lockedMouseCursor.SetActive(true);
        }
        else
        {
            Cursor.visible = true;
            if (_lockedMouseCursor != null)
                _lockedMouseCursor.SetActive(false);
        }

    }

    private bool ResolveLocalCamera()
    {
        if (hasLocalPlayer && localPlayer == null)
        {
            localCamera = null;
            hasLocalPlayer = false;
        }

        if (localCamera != null && localCamera.isActiveAndEnabled)
            return true;

        if (localPlayer != null)
            localCamera = Camera.main;

        if (localCamera == null)
            localCamera = Camera.main;

        return localCamera != null && localCamera.isActiveAndEnabled;
    }

    private void ResolveLocalPlayer()
    {
        if (localPlayer != null)
            return;

        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.IsLocalNetworkPlayer)
            {
                SetLocalPlayer(player);
                return;
            }
        }
    }
}
