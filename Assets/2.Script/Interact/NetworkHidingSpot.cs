using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkHidingSpot : MonoBehaviour, IPlayerInteractable, IHoldInteractable, IInteractionFailureProvider, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    private static readonly Dictionary<int, NetworkHidingSpot> Spots = new();
    private static readonly Dictionary<int, uint> Occupants = new();
    private const int StableIdSeed = 486187739;

    [SerializeField] private string interactionText = "사물함";
    [SerializeField] private string actionText = "숨기";
    [SerializeField] private int interactionPriority = 30;
    [SerializeField] private int spotId = 1;
    [SerializeField] private Transform cameraPose;
    [SerializeField] private Transform visualPose;
    [SerializeField] private Transform playerHiddenPose;
    [SerializeField] private Transform exitPose;
    [SerializeField] private float requiredHoldTime;
    [SerializeField, Min(0.1f)] private float maxUseDistance = 3f;
    [Header("Camera")]
    [SerializeField] private Behaviour hidingVirtualCamera;
    [SerializeField] private int hidingCameraPriority = 1000;
    [Header("Object Animation")]
    [SerializeField] private Animator objectAnimator;
    [SerializeField] private string enterStateName = "Enter";
    [SerializeField] private string exitStateName = "Exit";
    [SerializeField] private float enterDuration = 1f;
    [SerializeField] private float exitDuration = 1f;

    private int effectiveSpotId;

    public int SpotId => effectiveSpotId > 0 ? effectiveSpotId : spotId;
    public Transform CameraPose => cameraPose != null ? cameraPose : transform;
    public Transform VisualPose => visualPose != null ? visualPose : transform;
    public Transform PlayerHiddenPose => playerHiddenPose != null ? playerHiddenPose : transform;
    public Transform ExitPose => exitPose != null ? exitPose : transform;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);
    public Behaviour HidingVirtualCamera => ResolveHidingVirtualCamera();
    public int HidingCameraPriority => hidingCameraPriority;
    public float EnterDuration => Mathf.Max(0f, enterDuration);
    public float ExitDuration => Mathf.Max(0f, exitDuration);
    public bool IsOccupied => Occupants.ContainsKey(SpotId);
    public string InteractionText => interactionText;
    public string InteractionActionText => actionText;
    public int InteractionPriority => interactionPriority;

    public bool IsWithinUseRange(Vector3 playerPosition, float extraDistance = 0f)
    {
        float distance = Mathf.Max(0.1f, maxUseDistance + Mathf.Max(0f, extraDistance));
        float distanceSqr = distance * distance;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider spotCollider in colliders)
        {
            if (spotCollider == null || !spotCollider.enabled)
                continue;

            Vector3 closestPoint = spotCollider.ClosestPoint(playerPosition);
            if ((closestPoint - playerPosition).sqrMagnitude <= distanceSqr)
                return true;
        }

        return (transform.position - playerPosition).sqrMagnitude <= distanceSqr;
    }

    public static NetworkHidingSpot Find(int id)
    {
        return id > 0 && Spots.TryGetValue(id, out NetworkHidingSpot spot) ? spot : null;
    }

    private void OnEnable()
    {
        effectiveSpotId = ResolveEffectiveSpotId();
        if (effectiveSpotId <= 0)
        {
            Debug.LogWarning($"{nameof(NetworkHidingSpot)} on {name} has invalid spot id {spotId}.", this);
            return;
        }

        if (effectiveSpotId != spotId)
            Debug.LogWarning($"{nameof(NetworkHidingSpot)} on {name} resolved duplicate/invalid spot id {spotId} to stable id {effectiveSpotId}.", this);

        Spots[effectiveSpotId] = this;
    }

    private void OnDisable()
    {
        int registeredSpotId = SpotId;
        if (registeredSpotId > 0 && Spots.TryGetValue(registeredSpotId, out NetworkHidingSpot existing) && existing == this)
            Spots.Remove(registeredSpotId);

        Occupants.Remove(registeredSpotId);
    }

    public void Interact(PlayerMovement player)
    {
        if (player == null)
            return;

        NetworkPlayerHidingComponent hiding = player.GetComponent<NetworkPlayerHidingComponent>();
        if (hiding == null)
            hiding = player.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        hiding?.RequestToggle(this);
    }

    public bool TryGetInteractionFailureMessage(PlayerMovement player, out string message)
    {
        if (player == null)
        {
            message = "플레이어를 찾을 수 없습니다.";
            return true;
        }

        if (IsOccupied)
        {
            message = "이미 다른 플레이어가 사용 중입니다.";
            return true;
        }

        NetworkPlayerHidingComponent hiding = player.GetComponent<NetworkPlayerHidingComponent>();
        if (hiding == null)
            hiding = player.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        if (hiding == null)
        {
            message = "여기에는 숨을 수 없습니다.";
            return true;
        }

        message = null;
        return false;
    }

    public bool TryReserve(NetworkPlayerHidingComponent player)
    {
        if (player == null || player.Object == null)
            return false;

        uint playerId = player.Object.Id.Raw;
        int id = SpotId;
        if (id <= 0)
            return false;

        if (Occupants.TryGetValue(id, out uint occupantId) && occupantId != playerId)
            return false;

        Occupants[id] = playerId;
        return true;
    }

    public bool TryReserveLocal(NetworkPlayerHidingComponent player)
    {
        return player != null && !IsOccupied;
    }

    public void Release(NetworkPlayerHidingComponent player)
    {
        if (player == null)
            return;

        int id = SpotId;
        if (player.Object == null)
        {
            Occupants.Remove(id);
            return;
        }

        uint playerId = player.Object.Id.Raw;
        if (Occupants.TryGetValue(id, out uint occupantId) && occupantId != playerId)
            return;

        Occupants.Remove(id);
    }

    public void PlayEnterAnimation()
    {
        PlayAnimation(enterStateName);
    }

    public void PlayExitAnimation()
    {
        PlayAnimation(exitStateName);
    }

    private void PlayAnimation(string stateName)
    {
        if (objectAnimator == null)
            objectAnimator = GetComponentInChildren<Animator>(true);

        if (objectAnimator == null || string.IsNullOrWhiteSpace(stateName))
            return;

        objectAnimator.CrossFade(stateName, 0.05f, 0);
    }

    private Behaviour ResolveHidingVirtualCamera()
    {
        if (hidingVirtualCamera != null)
            return hidingVirtualCamera;

        System.Type cameraType = System.Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
        if (cameraType == null)
            return null;

        Transform pose = CameraPose;
        Component camera = pose != null ? pose.GetComponent(cameraType) : null;
        if (camera == null)
            camera = GetComponentInChildren(cameraType, true);

        hidingVirtualCamera = camera as Behaviour;
        return hidingVirtualCamera;
    }

    private int ResolveEffectiveSpotId()
    {
        if (spotId > 0 && !HasDuplicateSerializedSpotId())
            return spotId;

        int stableId = BuildStableSceneSpotId();
        while (stableId <= 0 || Spots.ContainsKey(stableId))
            stableId++;

        return stableId;
    }

    private bool HasDuplicateSerializedSpotId()
    {
        if (spotId <= 0 || !gameObject.scene.IsValid())
            return spotId <= 0;

        NetworkHidingSpot[] sceneSpots = FindObjectsByType<NetworkHidingSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int matches = 0;
        foreach (NetworkHidingSpot sceneSpot in sceneSpots)
        {
            if (sceneSpot == null || sceneSpot.gameObject.scene != gameObject.scene)
                continue;

            if (sceneSpot.spotId != spotId)
                continue;

            matches++;
            if (matches > 1)
                return true;
        }

        return false;
    }

    private int BuildStableSceneSpotId()
    {
        unchecked
        {
            int hash = StableIdSeed;
            string sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : string.Empty;
            AppendHash(ref hash, sceneName);
            AppendHash(ref hash, GetHierarchyPath(transform));
            return Mathf.Abs(hash == int.MinValue ? int.MaxValue : hash);
        }
    }

    private static void AppendHash(ref int hash, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        for (int i = 0; i < value.Length; i++)
            hash = (hash * 31) ^ value[i];
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
