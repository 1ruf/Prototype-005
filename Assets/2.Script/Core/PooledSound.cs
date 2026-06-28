using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledSound : MonoBehaviour, IPoolable
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float despawnPadding = 0.05f;

    private Coroutine despawnRoutine;

    private void Awake()
    {
        ResolveAudioSource();
        ConfigureAudioSource();
    }

    public void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null)
        {
            Despawn();
            return;
        }

        ResolveAudioSource();
        if (audioSource == null)
        {
            Despawn();
            return;
        }

        if (despawnRoutine != null)
            StopCoroutine(despawnRoutine);

        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.volume = Mathf.Clamp01(volume);
        audioSource.pitch = Mathf.Approximately(pitch, 0f) ? 1f : pitch;
        audioSource.Play();

        float duration = clip.length / Mathf.Abs(audioSource.pitch);
        despawnRoutine = StartCoroutine(DespawnAfter(duration + Mathf.Max(0f, despawnPadding)));
    }

    public void OnSpawnedFromPool(in PoolSpawnContext context)
    {
        ResolveAudioSource();
        ConfigureAudioSource();
    }

    public void OnDespawnedToPool()
    {
        if (despawnRoutine != null)
        {
            StopCoroutine(despawnRoutine);
            despawnRoutine = null;
        }

        if (audioSource == null)
            return;

        audioSource.Stop();
        audioSource.clip = null;
    }

    private IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        despawnRoutine = null;
        Despawn();
    }

    private void Despawn()
    {
        GameObjectPoolManager.Despawn(gameObject);
    }

    private void ResolveAudioSource()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        despawnPadding = Mathf.Max(0f, despawnPadding);
    }
#endif
}
