using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractionCooldown : MonoBehaviour, IInteractionCooldown
{
    [SerializeField, Min(0f)] private float cooldownDuration = 0.35f;

    private float cooldownEndTime;

    public bool IsInteractionOnCooldown => Time.unscaledTime < cooldownEndTime;

    public bool TryBeginInteractionCooldown()
    {
        if (IsInteractionOnCooldown)
            return false;

        cooldownEndTime = Time.unscaledTime + cooldownDuration;
        return true;
    }
}
