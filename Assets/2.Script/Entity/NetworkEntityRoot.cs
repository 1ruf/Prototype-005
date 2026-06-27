using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkEntityRoot : MonoBehaviour
{
    [SerializeField] private GameObject ownerOverride;
    [SerializeField] private bool initializeOnAwake = true;
    [SerializeField] private bool autoCollectComponents = true;
    [SerializeField] private MonoBehaviour[] componentBehaviours;

    private readonly List<INetworkEntityComponent> entityComponents = new List<INetworkEntityComponent>(8);
    private GameObject resolvedOwner;

    public GameObject Owner => resolvedOwner != null ? resolvedOwner : gameObject;
    public IReadOnlyList<INetworkEntityComponent> Components => entityComponents;

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

        foreach (INetworkEntityComponent component in entityComponents)
            component.Initialize(resolvedOwner);
    }

    private void BuildComponentCache()
    {
        entityComponents.Clear();

        if (autoCollectComponents || componentBehaviours == null || componentBehaviours.Length == 0)
            componentBehaviours = GetComponentsInChildren<MonoBehaviour>(true);

        if (componentBehaviours == null)
            return;

        foreach (MonoBehaviour behaviour in componentBehaviours)
        {
            if (behaviour == null || behaviour == this)
                continue;

            if (behaviour is INetworkEntityComponent component && !entityComponents.Contains(component))
                entityComponents.Add(component);
        }
    }
}
