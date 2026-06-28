using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAnimationTrigger : MonoBehaviour
{
    [SerializeField] private CSHEnemy enemy;
    [SerializeField] private List<AudioSource> audios;
    [SerializeField] private float duplicateAudioEventLockout = 0.25f;

    private float[] lastAudioPlayTimes;
    private bool[] audioEventActive;

    private void Awake()
    {
        ResolveReferences();
        EnsureAudioState();
    }

    public void AnimationEvent_ApplyKillKnockback(float force)
    {
        ResolveReferences();
        enemy?.AnimationEvent_ApplyKillKnockback(force);
    }

    public void PlayAudioEvent(int id)
    {
        if (!TryGetAudio(id, out AudioSource audio))
            return;

        float now = Time.time;
        if (audioEventActive[id] || audio.isPlaying || now - lastAudioPlayTimes[id] < duplicateAudioEventLockout)
            return;

        audioEventActive[id] = true;
        lastAudioPlayTimes[id] = now;
        audio.Play();
    }

    public void StopAudioEvent(int id)
    {
        if (!TryGetAudio(id, out AudioSource audio))
            return;

        audio.Stop();
        audioEventActive[id] = false;
    }

    private void ResolveReferences()
    {
        if (enemy == null)
            enemy = GetComponentInParent<CSHEnemy>();
    }

    private bool TryGetAudio(int id, out AudioSource audio)
    {
        ResolveReferences();
        EnsureAudioState();

        audio = null;
        if (audios == null || id < 0 || id >= audios.Count)
            return false;

        audio = audios[id];
        return audio != null;
    }

    private void EnsureAudioState()
    {
        int count = audios != null ? audios.Count : 0;
        if (lastAudioPlayTimes != null && lastAudioPlayTimes.Length == count && audioEventActive != null && audioEventActive.Length == count)
            return;

        lastAudioPlayTimes = new float[count];
        audioEventActive = new bool[count];
        for (int i = 0; i < lastAudioPlayTimes.Length; i++)
            lastAudioPlayTimes[i] = float.NegativeInfinity;
    }
}
