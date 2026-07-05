using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkHidingSpot : MonoBehaviour, IPlayerInteractable, IHoldInteractable, IInteractionFailureProvider, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    private static readonly Dictionary<int, NetworkHidingSpot> Spots = new();
    private static readonly Dictionary<int, uint> Occupants = new();

    [SerializeField] private string interactionText = "Locker";
    [SerializeField] private string actionText = "Hide";
    [SerializeField] private int interactionPriority = 30;
    [SerializeField] private int spotId = 1;
    [SerializeField] private Transform cameraPose;
    [SerializeField] private Transform visualPose;
    [SerializeField] private Transform playerHiddenPose;
    [SerializeField] private Transform exitPose;
    [SerializeField] private float requiredHoldTime;
    [Header("Camera")]
    [SerializeField] private Behaviour hidingVirtualCamera;
    [SerializeField] private int hidingCameraPriority = 1000;
    [Header("Object Animation")]
    [SerializeField] private Animator objectAnimator;
    [SerializeField] private string enterStateName = "Enter";
    [SerializeField] private string exitStateName = "Exit";
    [SerializeField] private float enterDuration = 1f;
    [SerializeField] private float exitDuration = 1f;

    public int SpotId => spotId;
    public Transform CameraPose => cameraPose != null ? cameraPose : transform;
    public Transform VisualPose => visualPose != null ? visualPose : transform;
    public Transform PlayerHiddenPose => playerHiddenPose != null ? playerHiddenPose : transform;
    public Transform ExitPose => exitPose != null ? exitPose : transform;
    public float RequiredHoldTime => Mathf.Max(0f, requiredHoldTime);
    public Behaviour HidingVirtualCamera => ResolveHidingVirtualCamera();
    public int HidingCameraPriority => hidingCameraPriority;
    public float EnterDuration => Mathf.Max(0f, enterDuration);
    public float ExitDuration => Mathf.Max(0f, exitDuration);
    public bool IsOccupied => Occupants.ContainsKey(spotId);
    public string InteractionText => interactionText;
    public string InteractionActionText => actionText;
    public int InteractionPriority => interactionPriority;

    public static NetworkHidingSpot Find(int id)
    {
        return id > 0 && Spots.TryGetValue(id, out NetworkHidingSpot spot) ? spot : null;
    }

    private void OnEnable()
    {
        if (spotId <= 0)
        {
            Debug.LogWarning($"{nameof(NetworkHidingSpot)} on {name} has invalid spot id {spotId}.", this);
            return;
        }

        if (Spots.TryGetValue(spotId, out NetworkHidingSpot existing) && existing != null && existing != this)
            Debug.LogWarning($"Duplicate hiding spot id {spotId}: {existing.name} and {name}.", this);

        Spots[spotId] = this;
    }

    private void OnDisable()
    {
        if (spotId > 0 && Spots.TryGetValue(spotId, out NetworkHidingSpot existing) && existing == this)
            Spots.Remove(spotId);

        Occupants.Remove(spotId);
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
            message = "Player not found.";
            return true;
        }

        if (IsOccupied)
        {
            message = "This hiding spot is already occupied.";
            return true;
        }

        NetworkPlayerHidingComponent hiding = player.GetComponent<NetworkPlayerHidingComponent>();
        if (hiding == null)
            hiding = player.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        if (hiding == null)
        {
            message = "You cannot hide here.";
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
        if (Occupants.TryGetValue(spotId, out uint occupantId) && occupantId != playerId)
            return false;

        Occupants[spotId] = playerId;
        return true;
    }

    public bool TryReserveLocal(NetworkPlayerHidingComponent player)
    {
        return player != null && !IsOccupied;
    }

    public void Release(NetworkPlayerHidingComponent player)
    {
        if (player == null || player.Object == null)
            return;

        uint playerId = player.Object.Id.Raw;
        if (Occupants.TryGetValue(spotId, out uint occupantId) && occupantId != playerId)
            return;

        Occupants.Remove(spotId);
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
}
