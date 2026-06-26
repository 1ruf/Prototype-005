using UnityEngine;

public class Flashlight : NetworkHeldItem, IInteractable
{
    [SerializeField] private UnityEngine.Light[] lights;
    [SerializeField] private GameObject beamObject;
    [SerializeField] private float firstPersonSpotAngle = 64f;
    [SerializeField] private float thirdPersonSpotAngle = 72f;
    [SerializeField] private float runSwayPitch = 6f;
    [SerializeField] private float runSwayYaw = 0f;
    [SerializeField] private float runSwayRoll = 0f;
    [SerializeField] private float runSwayFrequency = 12f;
    [SerializeField] private float walkSwayMultiplier = 0.45f;
    [SerializeField] private float thirdPersonLookPitchMultiplier = 0f;

    private float swayTime;
    private bool firstPerson;

    protected override void Awake()
    {
        base.Awake();
        EnsureLightReferences();
    }

    public override void Initialize(NetworkPlayerItemHolder holder)
    {
        base.Initialize(holder);
        EnsureLightReferences();
    }

    public void Interact()
    {
        NetworkPlayerItemHolder holder = GetComponentInParent<NetworkPlayerItemHolder>();
        if (holder != null)
            holder.RequestToggleActive();
    }

    public override void TickPresentation(float deltaTime, bool moving, bool sprinting, float normalizedMoveSpeed, float lookPitch)
    {
        float intensity = moving ? Mathf.Clamp01(normalizedMoveSpeed) : 0f;
        float runBlend = sprinting ? 1f : walkSwayMultiplier;
        swayTime += deltaTime * runSwayFrequency * Mathf.Lerp(0.75f, 1.15f, intensity);

        Vector3 swayEuler = Vector3.zero;
        if (intensity > 0.001f)
        {
            float sin = Mathf.Sin(swayTime);
            float cos = Mathf.Cos(swayTime * 0.5f);
            swayEuler = new Vector3(
                sin * runSwayPitch,
                cos * runSwayYaw,
                -sin * runSwayRoll) * intensity * runBlend;
        }

        Quaternion lookRotation = firstPerson ? Quaternion.identity : Quaternion.Euler(lookPitch * thirdPersonLookPitchMultiplier, 0f, 0f);
        ItemRoot.localRotation = (firstPerson ? FirstPersonRotation : ThirdPersonRotation) * lookRotation * Quaternion.Euler(swayEuler);
    }

    protected override void OnPerspectiveChanged(bool isFirstPerson)
    {
        firstPerson = isFirstPerson;
        ApplyLightProfile();
    }

    protected override void OnActiveStateChanged(bool isActive)
    {
        EnsureLightReferences();

        if (lights != null)
        {
            foreach (UnityEngine.Light flashlightLight in lights)
            {
                if (flashlightLight != null)
                    flashlightLight.enabled = isActive;
            }
        }

        if (beamObject != null)
            beamObject.SetActive(isActive);
    }

    private void ApplyLightProfile()
    {
        EnsureLightReferences();

        if (lights == null)
            return;

        float spotAngle = firstPerson ? firstPersonSpotAngle : thirdPersonSpotAngle;
        foreach (UnityEngine.Light flashlightLight in lights)
        {
            if (flashlightLight != null && flashlightLight.type == LightType.Spot)
                flashlightLight.spotAngle = spotAngle;
        }
    }

    private void EnsureLightReferences()
    {
        if (lights == null || lights.Length == 0)
            lights = GetComponentsInChildren<UnityEngine.Light>(true);
    }
}
