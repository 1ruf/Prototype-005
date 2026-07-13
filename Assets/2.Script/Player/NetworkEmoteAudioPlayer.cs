using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkEmoteAudioPlayer : NetworkBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] emoteClips;
    [SerializeField] private float volume = 1f;
    [SerializeField] private float minDistance = 1.2f;
    [SerializeField] private float maxDistance = 12f;
    [SerializeField] private bool skipWhenListenerOutsideRange = true;

    private bool subscribed;

    private void Awake()
    {
        ResolveAudioSource();
        ConfigureAudioSource();
    }

    public override void Spawned()
    {
        ResolveAudioSource();
        ConfigureAudioSource();

        if (Object != null && Object.HasInputAuthority)
            Subscribe();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Unsubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        EmoteWheelController.CommandSelected += RequestPlayEmote;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        EmoteWheelController.CommandSelected -= RequestPlayEmote;
        subscribed = false;
    }

    public void RequestPlayEmote(int emoteIndex)
    {
        if (!IsValidEmoteIndex(emoteIndex))
            return;

        if (Object == null)
        {
            PlayLocal(emoteIndex);
            return;
        }

        if (Object.HasStateAuthority)
        {
            RPC_PlayEmote(emoteIndex);
            return;
        }

        if (Object.HasInputAuthority)
            RPC_RequestPlayEmote(emoteIndex);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestPlayEmote(int emoteIndex)
    {
        if (!IsValidEmoteIndex(emoteIndex))
            return;

        RPC_PlayEmote(emoteIndex);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayEmote(int emoteIndex)
    {
        PlayLocal(emoteIndex);
    }

    private void PlayLocal(int emoteIndex)
    {
        if (!IsValidEmoteIndex(emoteIndex))
            return;

        ResolveAudioSource();
        ConfigureAudioSource();

        if (audioSource == null)
            return;

        if (skipWhenListenerOutsideRange && !IsLocalListenerInRange())
            return;

        audioSource.transform.position = transform.position;
        audioSource.PlayOneShot(emoteClips[emoteIndex], Mathf.Clamp01(volume));
    }

    private bool IsValidEmoteIndex(int emoteIndex)
    {
        return emoteClips != null
            && emoteIndex >= 0
            && emoteIndex < emoteClips.Length
            && emoteClips[emoteIndex] != null;
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
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = Mathf.Max(0.01f, minDistance);
        audioSource.maxDistance = Mathf.Max(audioSource.minDistance + 0.1f, maxDistance);
    }

    private bool IsLocalListenerInRange()
    {
        AudioListener listener = FindFirstObjectByType<AudioListener>();
        if (listener == null || !listener.isActiveAndEnabled)
            return true;

        float audibleDistance = Mathf.Max(minDistance, maxDistance);
        return Vector3.Distance(listener.transform.position, transform.position) <= audibleDistance;
    }
}
