using System.Collections;
using UnityEngine;

public sealed class PlayerHeartbeatAudioController : MonoBehaviour
{
    private const string HeartbeatAClipPath = "Assets/3.Asset/Audio/HeartBeat_A.wav";
    private const string HeartbeatBClipPath = "Assets/3.Asset/Audio/HeartBeat_B.wav";

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private PlayerFearSettings settings;
    [SerializeField] private AudioClip heartbeatAClip;
    [SerializeField] private AudioClip heartbeatBClip;

    [Header("Threat Distance")]
    [SerializeField] private float startDistance = 18f;
    [SerializeField] private float closestDistance = 2f;
    [SerializeField] private bool onlyReactToChasingEnemies = false;

    [Header("Heartbeat")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;
    [SerializeField] private float farBeatInterval = 1.35f;
    [SerializeField] private float closeBeatInterval = 0.34f;
    [SerializeField] private float beatPairGap = 0.16f;
    [SerializeField] private float intensityBlendSpeed = 5f;

    private Coroutine heartbeatRoutine;
    private float currentIntensity;

    private void Awake()
    {
        ResolveReferences();
        ConfigureAudioSource();
    }

    private void OnDisable()
    {
        StopHeartbeat();
        currentIntensity = 0f;
    }

    private void Update()
    {
        float targetIntensity = ResolveThreatIntensity();
        if (targetIntensity > currentIntensity)
        {
            currentIntensity = targetIntensity;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-intensityBlendSpeed * Time.deltaTime);
            currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, blend);
        }

        if (currentIntensity > 0.01f)
        {
            if (heartbeatRoutine == null)
                heartbeatRoutine = StartCoroutine(HeartbeatLoop());

            return;
        }

        currentIntensity = 0f;
        StopHeartbeat();
    }

    private IEnumerator HeartbeatLoop()
    {
        while (currentIntensity > 0.01f)
        {
            PlayHeartbeatClip(heartbeatAClip);
            yield return new WaitForSeconds(Mathf.Max(0.01f, beatPairGap));

            PlayHeartbeatClip(heartbeatBClip);
            yield return new WaitForSeconds(GetCurrentBeatInterval());
        }

        heartbeatRoutine = null;
    }

    private void PlayHeartbeatClip(AudioClip clip)
    {
        if (audioSource == null || clip == null)
            return;

        audioSource.PlayOneShot(clip, currentIntensity * maxVolume);
    }

    private float GetCurrentBeatInterval()
    {
        float far = Mathf.Max(0.01f, farBeatInterval);
        float close = Mathf.Max(0.01f, closeBeatInterval);
        return Mathf.Lerp(far, close, currentIntensity);
    }

    private float ResolveThreatIntensity()
    {
        PlayerMovement localPlayer = ResolveLocalPlayer();
        if (localPlayer == null)
            return 0f;

        float nearestDistance = float.PositiveInfinity;
        foreach (CSHEnemy enemy in EnemyRuntimeRegistry.Enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled)
                continue;

            if (onlyReactToChasingEnemies && !enemy.IsChasingTarget(localPlayer))
                continue;

            float distance = Vector3.Distance(localPlayer.transform.position, enemy.transform.position);
            if (distance < nearestDistance)
                nearestDistance = distance;
        }

        if (float.IsPositiveInfinity(nearestDistance) || nearestDistance >= startDistance)
            return 0f;

        float near = Mathf.Max(0.01f, closestDistance);
        float far = Mathf.Max(near + 0.01f, startDistance);
        return Mathf.InverseLerp(far, near, nearestDistance);
    }

    private void StopHeartbeat()
    {
        if (heartbeatRoutine == null)
            return;

        StopCoroutine(heartbeatRoutine);
        heartbeatRoutine = null;
    }

    private void ResolveReferences()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (settings == null)
            settings = Resources.Load<PlayerFearSettings>("PlayerFearSettings");

        if (heartbeatAClip == null && settings != null)
            heartbeatAClip = settings.HeartbeatAClip;

        if (heartbeatBClip == null && settings != null)
            heartbeatBClip = settings.HeartbeatBClip;

#if UNITY_EDITOR
        if (heartbeatAClip == null)
            heartbeatAClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(HeartbeatAClipPath);

        if (heartbeatBClip == null)
            heartbeatBClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(HeartbeatBClipPath);
#endif
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.loop = false;
    }

    private static PlayerMovement ResolveLocalPlayer()
    {
        if (Manager.Instance != null && Manager.Instance.PlayerManager != null && Manager.Instance.PlayerManager.LocalPlayer != null)
            return Manager.Instance.PlayerManager.LocalPlayer;

        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.IsLocalNetworkPlayer)
                return player;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (settings == null)
            settings = Resources.Load<PlayerFearSettings>("PlayerFearSettings");

        if (heartbeatAClip == null && settings != null)
            heartbeatAClip = settings.HeartbeatAClip;

        if (heartbeatBClip == null && settings != null)
            heartbeatBClip = settings.HeartbeatBClip;

        if (heartbeatAClip == null)
            heartbeatAClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(HeartbeatAClipPath);

        if (heartbeatBClip == null)
            heartbeatBClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(HeartbeatBClipPath);

        startDistance = Mathf.Max(0.01f, startDistance);
        closestDistance = Mathf.Clamp(closestDistance, 0.01f, Mathf.Max(0.01f, startDistance - 0.01f));
        maxVolume = Mathf.Clamp01(maxVolume);
        farBeatInterval = Mathf.Max(0.01f, farBeatInterval);
        closeBeatInterval = Mathf.Max(0.01f, closeBeatInterval);
        beatPairGap = Mathf.Max(0.01f, beatPairGap);
        intensityBlendSpeed = Mathf.Max(0.01f, intensityBlendSpeed);
    }
#endif
}
