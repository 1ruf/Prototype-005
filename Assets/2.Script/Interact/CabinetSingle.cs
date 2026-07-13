using DG.Tweening;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class CabinetSingle : NetworkBehaviour, IInteractable, IPlayerInteractable, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string interactionText = "Cabinet";
    [SerializeField] private string openActionText = "Open";
    [SerializeField] private string closeActionText = "Close";
    [SerializeField] private int interactPriority;
    [SerializeField] private Vector3 targetPos;
    [SerializeField] private float tweenDuration = 1f;

    [Header("Network Request Security")]
    [SerializeField] private ServerRequestValidationPolicy requestValidationPolicy = ServerRequestValidationPolicy.CreateInteractionDefault();

    [Networked] private NetworkBool IsOpenState { get; set; }

    private Vector3 _originPos;

    private bool localIsOpened;
    private bool hasAppliedVisualState;

    private const int OpenRequestRateLimitScope = 201;

    public int InteractionPriority => interactPriority;
    public string InteractionText => interactionText;
    public string InteractionActionText => IsOpen ? closeActionText : openActionText;
    private bool IsOpen => Object != null && Object.IsValid ? IsOpenState : localIsOpened;

    private void Awake()
    {
        _originPos = transform.localPosition;
        localIsOpened = false;
    }

    public override void Spawned()
    {
        ApplyVisualState(IsOpenState, false);
    }

    public override void Render()
    {
        ApplyVisualState(IsOpenState, true);
    }

    public void Interact()
    {
        RequestSetOpen(!IsOpen);
    }

    public void Interact(PlayerMovement player)
    {
        RequestSetOpen(!IsOpen);
    }

    private void RequestSetOpen(bool open)
    {
        if (Object == null || !Object.IsValid)
        {
            ApplyVisualState(open, true);
            return;
        }

        if (Object.HasStateAuthority)
        {
            SetOpenStateAuthority(open);
            return;
        }

        RPC_RequestSetOpen(open);
    }

    [Rpc(RpcSources.Proxies, RpcTargets.StateAuthority)]
    private void RPC_RequestSetOpen(NetworkBool open, RpcInfo info = default)
    {
        requestValidationPolicy ??= ServerRequestValidationPolicy.CreateInteractionDefault();
        if (!ServerRequestValidator.TryValidate(
                Runner,
                Object,
                transform,
                info,
                requestValidationPolicy,
                OpenRequestRateLimitScope,
                out _,
                out _))
            return;

        SetOpenStateAuthority(open);
    }

    private void SetOpenStateAuthority(bool open)
    {
        if (IsOpenState == open)
            return;

        IsOpenState = open;
        ApplyVisualState(open, true);
    }

    private void ApplyVisualState(bool open, bool animate)
    {
        Vector3 destination = open ? targetPos : _originPos;
        if (hasAppliedVisualState && localIsOpened == open && Vector3.SqrMagnitude(transform.localPosition - destination) <= 0.0001f)
            return;

        hasAppliedVisualState = true;
        localIsOpened = open;
        transform.DOKill();

        if (animate && tweenDuration > 0f)
        {
            transform.DOLocalMove(destination, tweenDuration);
            return;
        }

        transform.localPosition = destination;
    }
}
