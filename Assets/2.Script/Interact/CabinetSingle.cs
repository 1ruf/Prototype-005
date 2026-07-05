using DG.Tweening;
using UnityEngine;

public class CabinetSingle : MonoBehaviour, IInteractable, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [SerializeField] private string interactionText = "Cabinet";
    [SerializeField] private string openActionText = "Open";
    [SerializeField] private string closeActionText = "Close";
    [SerializeField] private int interactPriority;
    [SerializeField] private Vector3 targetPos;

    private Vector3 _originPos;

    private bool _isOpened;
    private bool _canMove;

    public int InteractionPriority => interactPriority;
    public string InteractionText => interactionText;
    public string InteractionActionText => _isOpened ? closeActionText : openActionText;

    private void Awake()
    {
        _originPos = transform.localPosition;
        _isOpened = false;
        _canMove = true;
    }
    public void Interact()
    {
        if (_canMove == false)
            return;

        _canMove = false;
        _isOpened = !_isOpened;
        MoveSequence(_isOpened);
    }

    private void MoveSequence(bool open)
    {
        transform.DOLocalMove(open ? targetPos : _originPos, 1f).OnComplete(() => _canMove = true);
    }
}
