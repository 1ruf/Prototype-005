using Fusion;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class NetworkDeathComponent : NetworkBehaviour, INetworkEntityComponent
{
    [SerializeField] private NetworkHealthComponent health;
    [SerializeField] private RagdollEntityComponent ragdoll;
    [SerializeField] private UnityEvent onDeath = new UnityEvent();

    private GameObject owner;
    private bool deathPublished;

    public GameObject Owner => owner != null ? owner : gameObject;
    public UnityEvent OnDeath => onDeath;

    private void Awake()
    {
        Initialize(ResolveOwner());
    }

    private void OnEnable()
    {
        SubscribeHealth();
    }

    private void OnDisable()
    {
        if (health != null)
            health.Died -= HandleDeath;
    }

    public override void Spawned()
    {
        Initialize(ResolveOwner());
        SubscribeHealth();
        PublishDeathIfNeeded();
    }

    public override void Render()
    {
        PublishDeathIfNeeded();
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner;

        if (health == null)
            health = GetComponent<NetworkHealthComponent>() ?? GetComponentInParent<NetworkHealthComponent>() ?? entityOwner.GetComponentInChildren<NetworkHealthComponent>(true);

        if (ragdoll == null)
            ragdoll = GetComponent<RagdollEntityComponent>() ?? GetComponentInParent<RagdollEntityComponent>() ?? entityOwner.GetComponentInChildren<RagdollEntityComponent>(true);
    }

    private void SubscribeHealth()
    {
        if (health == null)
            health = GetComponent<NetworkHealthComponent>() ?? GetComponentInParent<NetworkHealthComponent>() ?? ResolveOwner().GetComponentInChildren<NetworkHealthComponent>(true);

        if (health == null)
            return;

        health.Died -= HandleDeath;
        health.Died += HandleDeath;
    }

    private GameObject ResolveOwner()
    {
        PlayerMovement player = GetComponentInParent<PlayerMovement>();
        return player != null ? player.gameObject : gameObject;
    }

    private void HandleDeath(NetworkHealthComponent deadHealth)
    {
        PublishDeath();
    }

    private void PublishDeathIfNeeded()
    {
        if (health != null && health.IsDead)
            PublishDeath();
    }

    private void PublishDeath()
    {
        if (deathPublished)
            return;

        deathPublished = true;

        if (ragdoll != null && (Object == null || Object.HasStateAuthority))
            ragdoll.Kill();

        onDeath.Invoke();
    }
}
