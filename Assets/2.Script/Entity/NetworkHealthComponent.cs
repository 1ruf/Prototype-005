using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkHealthComponent : NetworkBehaviour, INetworkEntityComponent
{
    [SerializeField] private float maxHealth = 100f;

    [Header("Network Request Security")]
    [SerializeField] private ServerRequestValidationPolicy ownerRequestValidationPolicy = ServerRequestValidationPolicy.CreateOwnerRequestDefault();
    [SerializeField] private bool allowInputAuthorityReviveRequests;

    [Networked] public float CurrentHealth { get; private set; }
    [Networked] public NetworkBool IsDead { get; private set; }

    private GameObject owner;
    private bool deathNotified;

    private const int DamageRequestRateLimitScope = 301;
    private const int ReviveRequestRateLimitScope = 302;

    public event Action<NetworkHealthComponent> Died;
    public GameObject Owner => owner != null ? owner : gameObject;
    public float MaxHealth => maxHealth;

    private void Awake()
    {
        Initialize(ResolveOwner());
    }

    public override void Spawned()
    {
        Initialize(ResolveOwner());

        if (Object.HasStateAuthority && CurrentHealth <= 0f && !IsDead)
            CurrentHealth = maxHealth;

        NotifyDeathIfNeeded();
    }

    public override void Render()
    {
        NotifyDeathIfNeeded();
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner;
    }

    private GameObject ResolveOwner()
    {
        PlayerMovement player = GetComponentInParent<PlayerMovement>();
        return player != null ? player.gameObject : gameObject;
    }

    public void Damage(float amount)
    {
        if (!ServerRequestValidator.IsFinite(amount) || amount <= 0f || IsDead)
            return;

        if (Object == null || Object.HasStateAuthority)
        {
            ApplyDamage(amount);
            return;
        }

        RPC_RequestDamage(amount);
    }

    public void Kill()
    {
        Damage(maxHealth);
    }

    public void Revive()
    {
        if (Object == null || Object.HasStateAuthority)
        {
            SetAlive();
            return;
        }

        RPC_RequestRevive();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(float amount, RpcInfo info = default)
    {
        if (!TryValidateOwnerRequest(info, DamageRequestRateLimitScope)
            || !ServerRequestValidator.IsFinite(amount)
            || amount <= 0f)
            return;

        float boundedAmount = Mathf.Min(amount, Mathf.Max(0f, maxHealth));
        if (boundedAmount > 0f)
            ApplyDamage(boundedAmount);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestRevive(RpcInfo info = default)
    {
        if (!allowInputAuthorityReviveRequests
            || !TryValidateOwnerRequest(info, ReviveRequestRateLimitScope))
            return;

        SetAlive();
    }

    private bool TryValidateOwnerRequest(RpcInfo info, int rateLimitScope)
    {
        ownerRequestValidationPolicy ??= ServerRequestValidationPolicy.CreateOwnerRequestDefault();
        return ServerRequestValidator.TryValidateOwnerRequest(
            Runner,
            Object,
            Owner.transform,
            info,
            ownerRequestValidationPolicy,
            rateLimitScope,
            out _,
            out _);
    }

    private void ApplyDamage(float amount)
    {
        if (IsDead)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        if (CurrentHealth <= 0f)
            SetDead();
    }

    private void SetDead()
    {
        CurrentHealth = 0f;
        IsDead = true;
        NotifyDeathIfNeeded();
    }

    private void SetAlive()
    {
        CurrentHealth = maxHealth;
        IsDead = false;
        deathNotified = false;
    }

    private void NotifyDeathIfNeeded()
    {
        if (!IsDead || deathNotified)
            return;

        deathNotified = true;
        Died?.Invoke(this);
    }
}
