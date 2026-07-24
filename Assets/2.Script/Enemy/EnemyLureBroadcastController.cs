using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Connect this to a one-shot LeverPowerController. Turning on its power key broadcasts
/// the configured sound locally on every client and orders all state-authoritative enemies
/// to converge on this object's lure point for the configured duration.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyLureBroadcastController : MonoBehaviour
{
    [SerializeField] private string powerKey = PowerKeys.EnemyLureBroadcast;
    [SerializeField, Min(0.1f)] private float lureDuration = 30f;
    [SerializeField] private Transform lurePoint;

    [Header("Broadcast audio")]
    [SerializeField] private List<AudioSource> broadcastAudioSources = new();
    [SerializeField] private AudioClip broadcastClip;

    private bool triggered;

    private void OnEnable()
    {
        NetworkPowerRuntime.PowerStateChanged += HandlePowerStateChanged;
    }

    private void OnDisable()
    {
        NetworkPowerRuntime.PowerStateChanged -= HandlePowerStateChanged;
    }

    private void Start()
    {
        // A player joining after activation should see the lever down, but should not hear
        // an old one-shot broadcast replayed from the beginning.
        triggered = NetworkPowerRuntime.HasPowerState(powerKey) && NetworkPowerRuntime.GetPower(powerKey);
    }

    private void HandlePowerStateChanged(string changedPowerKey, bool isOn)
    {
        if (changedPowerKey != powerKey || !isOn || triggered)
            return;

        triggered = true;
        PlayBroadcastAudio();
        CommandEnemiesToLure();
    }

    private void PlayBroadcastAudio()
    {
        if (broadcastClip == null)
            return;

        foreach (AudioSource audioSource in broadcastAudioSources)
        {
            if (audioSource != null)
                audioSource.PlayOneShot(broadcastClip);
        }
    }

    private void CommandEnemiesToLure()
    {
        Vector3 destination = lurePoint != null ? lurePoint.position : transform.position;

        foreach (CSHEnemy enemy in EnemyRuntimeRegistry.Enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled)
                continue;

            enemy.BeginLure(destination, lureDuration);
        }
    }
}
