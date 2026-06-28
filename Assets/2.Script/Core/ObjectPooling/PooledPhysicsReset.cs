using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledPhysicsReset : MonoBehaviour, IPoolable
{
    [SerializeField] private bool resetRigidbodyVelocity = true;
    [SerializeField] private bool resetParticleSystems = true;
    [SerializeField] private bool resetTrailRenderers = true;

    private Rigidbody[] rigidbodies;
    private ParticleSystem[] particleSystems;
    private TrailRenderer[] trailRenderers;

    private void Awake()
    {
        CacheComponents();
    }

    public void OnSpawnedFromPool(in PoolSpawnContext context)
    {
        CacheComponents();
        ResetPhysics();
        ResetParticles();
        ResetTrails();
    }

    public void OnDespawnedToPool()
    {
        CacheComponents();
        ResetPhysics();
        ResetParticles();
        ResetTrails();
    }

    private void CacheComponents()
    {
        if (rigidbodies == null || rigidbodies.Length == 0)
            rigidbodies = GetComponentsInChildren<Rigidbody>(true);

        if (particleSystems == null || particleSystems.Length == 0)
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);

        if (trailRenderers == null || trailRenderers.Length == 0)
            trailRenderers = GetComponentsInChildren<TrailRenderer>(true);
    }

    private void ResetPhysics()
    {
        if (!resetRigidbodyVelocity || rigidbodies == null)
            return;

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody body = rigidbodies[i];
            if (body == null)
                continue;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private void ResetParticles()
    {
        if (!resetParticleSystems || particleSystems == null)
            return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            particleSystem.Clear(true);
            particleSystem.Play(true);
        }
    }

    private void ResetTrails()
    {
        if (!resetTrailRenderers || trailRenderers == null)
            return;

        for (int i = 0; i < trailRenderers.Length; i++)
        {
            TrailRenderer trailRenderer = trailRenderers[i];
            if (trailRenderer != null)
                trailRenderer.Clear();
        }
    }
}
