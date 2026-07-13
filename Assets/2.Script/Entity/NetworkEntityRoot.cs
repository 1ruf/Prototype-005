using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class NetworkEntityRoot : MonoBehaviour
{
    [SerializeField] private GameObject ownerOverride;
    [SerializeField] private bool initializeOnAwake = true;
    [SerializeField] private bool autoCollectComponents = true;
    [SerializeField] private MonoBehaviour[] componentBehaviours;

    private readonly List<IEntityComponent> entityComponents = new List<IEntityComponent>(8);
    private readonly List<INetworkEntityComponent> networkEntityComponents = new List<INetworkEntityComponent>(8);
    private GameObject resolvedOwner;

    public GameObject Owner => resolvedOwner != null ? resolvedOwner : gameObject;
    public IReadOnlyList<INetworkEntityComponent> Components => networkEntityComponents;
    public IReadOnlyList<IEntityComponent> AllComponents => entityComponents;

    private void Awake()
    {
        if (initializeOnAwake)
            InitializeComponents();
    }

    private void Start()
    {
        InitializeComponents();
    }

    public void InitializeComponents()
    {
        resolvedOwner = ownerOverride != null ? ownerOverride : gameObject;
        BuildComponentCache();

        foreach (IEntityComponent component in entityComponents)
            component.Initialize(resolvedOwner);
    }

    private void BuildComponentCache()
    {
        entityComponents.Clear();
        networkEntityComponents.Clear();

        if (autoCollectComponents || componentBehaviours == null || componentBehaviours.Length == 0)
            componentBehaviours = GetComponentsInChildren<MonoBehaviour>(true);

        if (componentBehaviours == null)
            return;

        foreach (MonoBehaviour behaviour in componentBehaviours)
        {
            if (behaviour == null || behaviour == this)
                continue;

            if (behaviour is not IEntityComponent component || entityComponents.Contains(component))
                continue;

            entityComponents.Add(component);
            if (component is INetworkEntityComponent networkComponent)
                networkEntityComponents.Add(networkComponent);
        }
    }
}
