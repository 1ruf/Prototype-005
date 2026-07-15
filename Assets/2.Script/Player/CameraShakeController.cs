using Unity.Cinemachine;
using UnityEngine;

public class CameraShakeController : MonoBehaviour
{
    [SerializeField] private CinemachineBasicMultiChannelPerlin noise;
    [SerializeField] private float maxAmplitude = 0.25f;
    [SerializeField] private float maxFrequency = 0.75f;
    [SerializeField] private float blendSpeed = 6f;

    private float targetAmplitude;
    private float targetFrequency;

    private void Awake()
    {
        if (noise == null)
            noise = GetComponent<CinemachineBasicMultiChannelPerlin>();

        ApplyImmediate(0f, 0f);
    }

    private void Update()
    {
        if (noise == null)
            return;

        float t = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        noise.AmplitudeGain = Mathf.Lerp(noise.AmplitudeGain, targetAmplitude, t);
        noise.FrequencyGain = Mathf.Lerp(noise.FrequencyGain, targetFrequency, t);
    }

    public void SetShake01(float intensity)
    {
        intensity = Mathf.Clamp01(intensity);
        SetShake(maxAmplitude * intensity, maxFrequency * intensity);
    }

    public void SetShake(float amplitude, float frequency)
    {
        targetAmplitude = Mathf.Max(0f, amplitude);
        targetFrequency = Mathf.Max(0f, frequency);
    }

    public void AddShake(float amplitude, float frequency)
    {
        targetAmplitude = Mathf.Max(targetAmplitude, amplitude);
        targetFrequency = Mathf.Max(targetFrequency, frequency);
    }

    public void StopShake()
    {
        SetShake(0f, 0f);
    }

    public void ApplyImmediate(float amplitude, float frequency)
    {
        targetAmplitude = Mathf.Max(0f, amplitude);
        targetFrequency = Mathf.Max(0f, frequency);

        if (noise == null)
            return;

        noise.AmplitudeGain = targetAmplitude;
        noise.FrequencyGain = targetFrequency;
    }
}
