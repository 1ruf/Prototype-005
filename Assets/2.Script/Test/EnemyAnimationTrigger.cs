using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAnimationTrigger : MonoBehaviour
{
    [SerializeField] private CSHEnemy enemy;
    [SerializeField] private List<AudioSource> audios;

    private void Awake()
    {
        ResolveReferences();
    }

    public void ApplyKillKnockbackAnimationEvent()
    {
        ResolveReferences();
        enemy?.ApplyKillKnockbackAnimationEvent();
    }

    public void SpawnKillBloodSplatterAnimationEvent(int count)
    {
        ResolveReferences();
        enemy?.SpawnKillBloodSplatterAnimationEvent(count);
    }

    public void AnimationEvent_SpawnKillBloodSplatter(int count)
    {
        SpawnKillBloodSplatterAnimationEvent(count);
    }

    public void OnKillBloodSplatter(int count)
    {
        SpawnKillBloodSplatterAnimationEvent(count);
    }

    public void PlayAudioEvent(int id)
    {
        audios[id].Play();
    }
    public void StopAudioEvent(int id)
    {
        audios[id].Stop();
    }

    public void Trigger(string eventName)
    {
        ResolveReferences();

        if (enemy == null || string.IsNullOrEmpty(eventName))
            return;

        enemy.SendMessage(eventName, SendMessageOptions.DontRequireReceiver);
    }

    public void Trigger(AnimationEvent animationEvent)
    {
        if (animationEvent == null)
            return;

        Trigger(animationEvent.stringParameter);
    }

    public void AnimationEvent(string eventName)
    {
        Trigger(eventName);
    }

    private void ResolveReferences()
    {
        if (enemy == null)
            enemy = GetComponentInParent<CSHEnemy>();
    }
}
