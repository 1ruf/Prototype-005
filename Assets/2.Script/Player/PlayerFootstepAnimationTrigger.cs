using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerFootstepAnimationTrigger : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private List<AudioClip> footstepClips = new List<AudioClip>();
    [SerializeField] private Vector2 volumeRange = new Vector2(0.85f, 1f);
    [SerializeField] private Vector2 pitchRange = new Vector2(0.85f, 1.15f);
    [SerializeField] private float duplicateEventLockout = 0.06f;

    private float lastFootstepTime = float.NegativeInfinity;

    private void Awake()
    {
        ResolveReferences();
    }

    public void AnimationEvent_Footstep(AnimationEvent animationEvent)
    {
        int footIndex = animationEvent != null ? animationEvent.intParameter : 0;
        AudioClip eventClip = animationEvent != null ? animationEvent.objectReferenceParameter as AudioClip : null;
        PlayFootstep(footIndex, eventClip);
    }

    public void Footstep(int footIndex)
    {
        PlayFootstep(footIndex, null);
    }

    private void PlayFootstep(int footIndex, AudioClip eventClip)
    {
        ResolveReferences();

        if (audioSource == null || Time.time - lastFootstepTime < duplicateEventLockout)
            return;

        AudioClip clip = eventClip != null ? eventClip : ResolveClip(footIndex);
        if (clip == null)
            return;

        lastFootstepTime = Time.time;
        audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        audioSource.PlayOneShot(clip, Random.Range(volumeRange.x, volumeRange.y));
    }

    private AudioClip ResolveClip(int footIndex)
    {
        if (footstepClips != null && footstepClips.Count > 0)
        {
            int index = Mathf.Abs(footIndex) % footstepClips.Count;
            AudioClip indexedClip = footstepClips[index];
            if (indexedClip != null)
                return indexedClip;

            for (int i = 0; i < footstepClips.Count; i++)
            {
                if (footstepClips[i] != null)
                    return footstepClips[i];
            }
        }

        return audioSource != null ? audioSource.clip : null;
    }

    private void ResolveReferences()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = Mathf.Max(0.1f, audioSource.minDistance);
        audioSource.maxDistance = Mathf.Max(audioSource.minDistance + 0.1f, audioSource.maxDistance);
    }
}
