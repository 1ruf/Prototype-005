using UnityEngine;
using DG.Tweening;

[DisallowMultipleComponent]
public sealed class Lever : MonoBehaviour, IInteractable, IHoldInteractable, IInteractionPrompt, IInteractionActionPrompt, IInteractionPriority
{
    [Header("Interaction")]
    [SerializeField] private string interactionText = "Lever";
    [SerializeField] private string actionText = "Use";
    [SerializeField] private int interactionPriority = 20;
    [SerializeField, Min(0f)] private float requiredHoldTime = 0.2f;

    [Header("State")]
    [SerializeField] private bool isOn;
    [SerializeField] private Component[] controllableTargets;

    [Header("Visual")]
    [SerializeField] private Transform handlePivot;
    [SerializeField, Min(0f)] private float handleTweenDuration = 0.2f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip turnOnClip;
    [SerializeField] private AudioClip turnOffClip;

    public bool IsOn => isOn;
    public float RequiredHoldTime => requiredHoldTime;
    public string InteractionText => interactionText;
    public string InteractionActionText => actionText;
    public int InteractionPriority => interactionPriority;

    private void Awake()
    {
        ApplyVisualState(false);
    }

    public void Interact()
    {
        if (TryToggleTargets())
            return;

        SetState(!isOn, true);
    }

    public void SetState(bool value)
    {
        SetState(value, false);
    }

    public void SetStateFromSynchronizedSource(bool value, bool playSound, bool animate = true)
    {
        if (isOn == value)
            return;

        isOn = value;
        ApplyVisualState(animate);

        if (playSound)
            PlayToggleSound();
    }

    private void SetState(bool value, bool playSound)
    {
        if (isOn == value)
            return;

        isOn = value;
        ApplyVisualState(true);
        NotifyTargets();

        if (playSound)
            PlayToggleSound();
    }

    private void ApplyVisualState(bool animate)
    {
        if (handlePivot == null)
            return;

        Vector3 rotation = handlePivot.localEulerAngles;
        rotation.x = isOn ? 180f : 0f;
        handlePivot.DOKill();

        if (!animate || handleTweenDuration <= 0f)
        {
            handlePivot.localEulerAngles = rotation;
            return;
        }

        handlePivot.DOLocalRotate(rotation, handleTweenDuration, RotateMode.Fast)
            .SetEase(Ease.OutExpo);
    }

    private bool TryToggleTargets()
    {
        if (controllableTargets == null)
            return false;

        bool toggled = false;
        foreach (Component target in controllableTargets)
        {
            if (target is not ILeverToggleable toggleable)
                continue;

            toggleable.ToggleLeverState();
            toggled = true;
        }

        return toggled;
    }

    private void NotifyTargets()
    {
        if (controllableTargets == null)
            return;

        foreach (Component target in controllableTargets)
        {
            if (target is ILeverControllable controllable)
                controllable.SetLeverState(isOn);
        }
    }

    private void PlayToggleSound()
    {
        if (audioSource == null)
            return;

        AudioClip clip = isOn ? turnOnClip : turnOffClip;
        if (clip != null)
            audioSource.PlayOneShot(clip);
    }
}
