using UnityEngine;

[DefaultExecutionOrder(10100)]
[DisallowMultipleComponent]
public sealed class FirstPersonEquipmentAim : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private Transform positionSource;
    [SerializeField] private float positionFollowSpeed = 30f;
    [SerializeField] private float rotationPredictionTime = 0.055f;
    [SerializeField] private float maxHorizontalLeadAngle = 4f;
    [SerializeField] private float maxVerticalLeadAngle = 3f;
    [SerializeField] private float angularVelocityFollowSpeed = 18f;

    private Vector3 localPositionOffset;
    private Quaternion localRotationOffset;
    private bool initialized;
    private MouseLookSystem mouseLook;
    private Vector2 previousViewAngles;
    private Vector2 smoothedAngularVelocity;
    private bool hasPreviousViewAngles;

    private void Awake()
    {
        ResolveReferences();
        CacheOffsets();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        if (playerMovement == null || positionSource == null)
            return;

        if (!playerMovement.IsLocalNetworkPlayer)
            return;

        if (!initialized)
            CacheOffsets();

        Quaternion baseViewRotation = GetBaseViewRotation();
        Vector3 viewEuler = baseViewRotation.eulerAngles;
        Vector2 viewAngles = new Vector2(NormalizeAngle(viewEuler.x), NormalizeAngle(viewEuler.y));
        if (!hasPreviousViewAngles)
        {
            previousViewAngles = viewAngles;
            hasPreviousViewAngles = true;
        }

        float safeDeltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 targetAngularVelocity = new Vector2(
            Mathf.DeltaAngle(previousViewAngles.x, viewAngles.x) / safeDeltaTime,
            Mathf.DeltaAngle(previousViewAngles.y, viewAngles.y) / safeDeltaTime);
        previousViewAngles = viewAngles;

        float velocityBlend = 1f - Mathf.Exp(-Mathf.Max(0f, angularVelocityFollowSpeed) * Time.deltaTime);
        smoothedAngularVelocity = Vector2.Lerp(smoothedAngularVelocity, targetAngularVelocity, velocityBlend);

        float pitchLead = Mathf.Clamp(
            smoothedAngularVelocity.x * rotationPredictionTime,
            -maxVerticalLeadAngle,
            maxVerticalLeadAngle);
        float yawLead = Mathf.Clamp(
            smoothedAngularVelocity.y * rotationPredictionTime,
            -maxHorizontalLeadAngle,
            maxHorizontalLeadAngle);

        Quaternion leadRotation = Quaternion.Euler(pitchLead, yawLead, 0f);
        Quaternion targetRotation = baseViewRotation * leadRotation * localRotationOffset;
        Vector3 targetPosition = positionSource.position + targetRotation * localPositionOffset;
        float positionBlend = 1f - Mathf.Exp(-Mathf.Max(0f, positionFollowSpeed) * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, targetPosition, positionBlend);
        transform.rotation = targetRotation;
    }

    private Quaternion GetBaseViewRotation()
    {
        return mouseLook != null ? mouseLook.transform.rotation : positionSource.rotation;
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (positionSource == null && transform.parent != null)
        {
            mouseLook = transform.parent.GetComponentInChildren<MouseLookSystem>(true);
            if (mouseLook != null)
                positionSource = mouseLook.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>(true)?.transform;
        }

        if (mouseLook == null && transform.parent != null)
            mouseLook = transform.parent.GetComponentInChildren<MouseLookSystem>(true);
    }

    private void CacheOffsets()
    {
        if (positionSource == null || playerMovement == null)
            return;

        Quaternion baseViewRotation = GetBaseViewRotation();
        localPositionOffset = Quaternion.Inverse(baseViewRotation) * (transform.position - positionSource.position);
        localRotationOffset = Quaternion.Inverse(baseViewRotation) * transform.rotation;
        initialized = true;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
