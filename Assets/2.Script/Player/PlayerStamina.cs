using System;
using UnityEngine;

public readonly struct PlayerStaminaSnapshot
{
    public PlayerStaminaSnapshot(float current, float max, bool isSprinting, bool isExhausted)
    {
        Current = current;
        Max = max;
        IsSprinting = isSprinting;
        IsExhausted = isExhausted;
    }

    public float Current { get; }
    public float Max { get; }
    public bool IsSprinting { get; }
    public bool IsExhausted { get; }
    public float Normalized => Max <= 0f ? 0f : Mathf.Clamp01(Current / Max);
    public bool IsFull => Normalized >= 0.999f;
}

public interface IReadOnlyPlayerStamina
{
    event Action<PlayerStaminaSnapshot> Changed;

    float Current { get; }
    float Max { get; }
    float Normalized { get; }
    bool IsSprinting { get; }
    bool IsExhausted { get; }
    PlayerStaminaSnapshot Snapshot { get; }
}

public class PlayerStamina : MonoBehaviour, IReadOnlyPlayerStamina
{
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float drainPerSecond = 22f;
    [SerializeField] private float recoverPerSecond = 18f;
    [SerializeField] private float recoverDelay = 0.6f;
    [SerializeField, Range(0f, 1f)] private float exhaustedRecoveryThreshold = 0.2f;

    private float currentStamina;
    private float recoverDelayRemaining;
    private bool isSprinting;
    private bool isExhausted;
    private PlayerStaminaSnapshot lastPublishedSnapshot;

    public event Action<PlayerStaminaSnapshot> Changed;

    public float Current => currentStamina;
    public float Max => Mathf.Max(0.001f, maxStamina);
    public float Normalized => Mathf.Clamp01(currentStamina / Max);
    public bool IsSprinting => isSprinting;
    public bool IsExhausted => isExhausted;
    public PlayerStaminaSnapshot Snapshot => CreateSnapshot();

    private void Awake()
    {
        currentStamina = Max;
        lastPublishedSnapshot = CreateSnapshot();
    }

    private void OnEnable()
    {
        PublishIfChanged(true);
    }

    public bool Tick(float deltaTime, bool wantsSprint)
    {
        deltaTime = Mathf.Max(0f, deltaTime);
        bool canSprint = wantsSprint && CanStartOrContinueSprint();

        if (canSprint)
        {
            Drain(deltaTime);
        }
        else
        {
            Recover(deltaTime);
        }

        SetSprinting(canSprint && currentStamina > 0f);
        PublishIfChanged(false);
        return isSprinting;
    }

    private bool CanStartOrContinueSprint()
    {
        if (currentStamina <= 0f)
            return false;

        if (!isExhausted)
            return true;

        return Normalized >= exhaustedRecoveryThreshold;
    }

    private void Drain(float deltaTime)
    {
        recoverDelayRemaining = recoverDelay;
        currentStamina = Mathf.Max(0f, currentStamina - drainPerSecond * deltaTime);

        if (currentStamina <= 0f)
            isExhausted = true;
    }

    private void Recover(float deltaTime)
    {
        SetSprinting(false);

        if (recoverDelayRemaining > 0f)
        {
            recoverDelayRemaining = Mathf.Max(0f, recoverDelayRemaining - deltaTime);
            return;
        }

        currentStamina = Mathf.Min(Max, currentStamina + recoverPerSecond * deltaTime);

        if (isExhausted && Normalized >= exhaustedRecoveryThreshold)
            isExhausted = false;
    }

    private void SetSprinting(bool value)
    {
        isSprinting = value;
    }

    private void PublishIfChanged(bool force)
    {
        PlayerStaminaSnapshot snapshot = CreateSnapshot();
        if (!force && !HasMeaningfulChange(snapshot))
            return;

        lastPublishedSnapshot = snapshot;
        Changed?.Invoke(snapshot);
    }

    private bool HasMeaningfulChange(PlayerStaminaSnapshot snapshot)
    {
        return !Mathf.Approximately(snapshot.Normalized, lastPublishedSnapshot.Normalized)
            || snapshot.IsSprinting != lastPublishedSnapshot.IsSprinting
            || snapshot.IsExhausted != lastPublishedSnapshot.IsExhausted
            || snapshot.IsFull != lastPublishedSnapshot.IsFull;
    }

    private PlayerStaminaSnapshot CreateSnapshot()
    {
        return new PlayerStaminaSnapshot(currentStamina, Max, isSprinting, isExhausted);
    }
}
