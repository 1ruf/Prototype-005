using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledAutoDespawn : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0f)] private float lifeTime = 2f;
    [SerializeField] private bool useParticleSystemDuration = true;

    private Coroutine despawnRoutine;

    public void OnSpawnedFromPool(in PoolSpawnContext context)
    {
        StopDespawnRoutine();
        despawnRoutine = StartCoroutine(DespawnAfterDelay(GetDelay()));
    }

    public void OnDespawnedToPool()
    {
        StopDespawnRoutine();
    }

    private float GetDelay()
    {
        if (!useParticleSystemDuration)
            return lifeTime;

        ParticleSystem particleSystem = GetComponentInChildren<ParticleSystem>(true);
        if (particleSystem == null)
            return lifeTime;

        ParticleSystem.MainModule main = particleSystem.main;
        return Mathf.Max(lifeTime, main.duration + main.startLifetime.constantMax);
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        GameObjectPoolManager.Despawn(gameObject);
    }

    private void StopDespawnRoutine()
    {
        if (despawnRoutine == null)
            return;

        StopCoroutine(despawnRoutine);
        despawnRoutine = null;
    }
}
