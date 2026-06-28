using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlayerFearFeedback : MonoBehaviour, IPlayerFearFeedback
{
    private const string ContactClipPath = "Assets/3.Asset/Audio/Contact.wav";
    private const string RunClipPath = "Assets/3.Asset/Audio/Run.wav";

    [Header("Audio")]
    [SerializeField] private GameObject soundPrefab;
    [SerializeField] private AudioClip contactClip;
    [SerializeField] private AudioClip runClip;
    [SerializeField] private AudioClip chasingClip;
    [SerializeField] private PlayerFearSettings settings;
    [SerializeField, Range(0f, 1f)] private float contactVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float runVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float chasingVolume = 1f;

    [Header("Screen Volume")]
    [SerializeField] private Volume fearVolume;
    [SerializeField] private bool autoResolveVolume = true;
    [SerializeField, Range(0f, 1f)] private float contactFearWeight = 0.5f;
    [SerializeField, Range(0f, 1f)] private float chaseFearWeight = 1f;
    [SerializeField] private float riseDuration = 0.12f;
    [SerializeField] private float fallDuration = 1.35f;
    [SerializeField] private AnimationCurve riseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve fallCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private Coroutine pulseRoutine;

    private void Awake()
    {
        ResolveReferences();
        ApplyVolumeWeight(0f);
    }

    public void Play(PlayerFearThreat threat)
    {
        PlayContactSound();
        if (threat.IsChasingLocalPlayer)
        {
            PlayRunSound();
            PlayChasingSound();
        }

        PlayVolumePulse(threat.IsChasingLocalPlayer ? chaseFearWeight : contactFearWeight);
    }

    public void PlayChaseStarted(PlayerFearThreat threat)
    {
        PlayRunSound();
        PlayChasingSound();
        PlayVolumePulse(chaseFearWeight);
    }

    private void PlayContactSound()
    {
        PlaySound(contactClip, contactVolume);
    }

    private void PlayRunSound()
    {
        PlaySound(runClip, runVolume);
    }

    private void PlayChasingSound()
    {
        PlaySound(chasingClip, chasingVolume);
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (soundPrefab == null || clip == null)
            return;

        PooledSoundPlayer.Play(soundPrefab, clip, transform.position, volume);
    }

    private void PlayVolumePulse(float targetWeight)
    {
        if (fearVolume == null)
            return;

        if (pulseRoutine != null)
            StopCoroutine(pulseRoutine);

        pulseRoutine = StartCoroutine(PulseVolume(Mathf.Clamp01(targetWeight)));
    }

    private IEnumerator PulseVolume(float targetWeight)
    {
        yield return AnimateVolume(GetCurrentVolumeWeight(), targetWeight, riseDuration, riseCurve);
        yield return AnimateVolume(GetCurrentVolumeWeight(), 0f, fallDuration, fallCurve);
        pulseRoutine = null;
    }

    private IEnumerator AnimateVolume(float from, float to, float duration, AnimationCurve curve)
    {
        if (duration <= 0f)
        {
            ApplyVolumeWeight(to);
            yield break;
        }

        ApplyVolumeWeight(from);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float curvedTime = EvaluateCurveProgress(curve, normalizedTime);
            ApplyVolumeWeight(Mathf.Lerp(from, to, curvedTime));
            yield return null;
        }

        ApplyVolumeWeight(to);
    }

    private void ApplyVolumeWeight(float weight)
    {
        if (fearVolume != null)
            fearVolume.weight = Mathf.Clamp01(weight);
    }

    private float GetCurrentVolumeWeight()
    {
        return fearVolume != null ? Mathf.Clamp01(fearVolume.weight) : 0f;
    }

    private static float EvaluateCurveProgress(AnimationCurve curve, float normalizedTime)
    {
        if (curve == null || curve.length == 0)
            return normalizedTime;

        float start = curve.Evaluate(0f);
        float end = curve.Evaluate(1f);
        if (Mathf.Approximately(start, end))
            return normalizedTime;

        return Mathf.Clamp01(Mathf.InverseLerp(start, end, curve.Evaluate(normalizedTime)));
    }

    private void ResolveReferences()
    {
        if (settings == null)
            settings = Resources.Load<PlayerFearSettings>("PlayerFearSettings");

        if (soundPrefab == null && settings != null)
            soundPrefab = settings.SoundPrefab;

        if (contactClip == null && settings != null)
            contactClip = settings.ContactClip;

        if (runClip == null && settings != null)
            runClip = settings.RunClip;

        if (chasingClip == null && settings != null)
            chasingClip = settings.ChasingClip;

#if UNITY_EDITOR
        if (contactClip == null)
            contactClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(ContactClipPath);

        if (runClip == null)
            runClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(RunClipPath);
#endif

        if (fearVolume == null && autoResolveVolume)
            fearVolume = GetComponent<Volume>() ?? GetComponentInChildren<Volume>(true) ?? FindFirstObjectByType<Volume>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (fearVolume == null && autoResolveVolume)
            fearVolume = GetComponent<Volume>() ?? GetComponentInChildren<Volume>(true) ?? FindFirstObjectByType<Volume>();

        if (settings == null)
            settings = Resources.Load<PlayerFearSettings>("PlayerFearSettings");

        if (soundPrefab == null && settings != null)
            soundPrefab = settings.SoundPrefab;

        if (contactClip == null && settings != null)
            contactClip = settings.ContactClip;

        if (runClip == null && settings != null)
            runClip = settings.RunClip;

        if (chasingClip == null && settings != null)
            chasingClip = settings.ChasingClip;

        if (contactClip == null)
            contactClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(ContactClipPath);

        if (runClip == null)
            runClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(RunClipPath);

        riseDuration = Mathf.Max(0f, riseDuration);
        fallDuration = Mathf.Max(0f, fallDuration);
        contactFearWeight = Mathf.Clamp01(contactFearWeight);
        chaseFearWeight = Mathf.Clamp01(chaseFearWeight);
        contactVolume = Mathf.Clamp01(contactVolume);
        runVolume = Mathf.Clamp01(runVolume);
        chasingVolume = Mathf.Clamp01(chasingVolume);
    }
#endif
}
