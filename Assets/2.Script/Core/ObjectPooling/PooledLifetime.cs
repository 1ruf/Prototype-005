using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledLifetime : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0f)] private float lifeTime = 5f;
    [SerializeField] private bool startOnEnable = true;

    private Coroutine lifetimeRoutine;

    private void OnEnable()
    {
        if (startOnEnable)
            RestartTimer();
    }

    public void OnSpawnedFromPool(in PoolSpawnContext context)
    {
        RestartTimer();
    }

    public void OnDespawnedToPool()
    {
        StopTimer();
    }

    public void RestartTimer()
    {
        StopTimer();
        lifetimeRoutine = StartCoroutine(DespawnAfterLifetime());
    }

    private IEnumerator DespawnAfterLifetime()
    {
        if (lifeTime > 0f)
            yield return new WaitForSeconds(lifeTime);

        GameObjectPoolManager.Despawn(gameObject);
    }

    private void StopTimer()
    {
        if (lifetimeRoutine == null)
            return;

        StopCoroutine(lifetimeRoutine);
        lifetimeRoutine = null;
    }
}
