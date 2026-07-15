using UnityEngine;

public class CameraBobbingController : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private float walkAmplitude = 0.012f;
    [SerializeField] private float runAmplitude = 0.02f;
    [SerializeField] private float walkFrequency = 4.4f;
    [SerializeField] private float runFrequency = 6f;
    [SerializeField] private float sideAmplitudeMultiplier = 0.35f;
    [SerializeField] private float walkRollAngle = 0.25f;
    [SerializeField] private float runRollAngle = 0.45f;
    [SerializeField] private float turnRollAngle = 0.45f;
    [SerializeField] private float motionFollowSpeed = 12f;
    [SerializeField] private float turnRollFollowSpeed = 8f;
    [SerializeField] private float returnSpeed = 8f;

    private float phase;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private float currentTurnRoll;

    private void Awake()
    {
        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
    }

    private void LateUpdate()
    {
        if (playerMovement != null && !playerMovement.IsLocalNetworkPlayer)
            return;

        Vector2 move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool moving = move.sqrMagnitude > 0.01f;
        bool running = playerMovement != null ? playerMovement.IsSprintActive : Input.GetKey(KeyCode.LeftShift);
        float targetTurnRoll = -Mathf.Clamp(Input.GetAxis("Mouse X"), -1f, 1f) * turnRollAngle;
        float turnBlend = 1f - Mathf.Exp(-Mathf.Max(0f, turnRollFollowSpeed) * Time.deltaTime);
        currentTurnRoll = Mathf.Lerp(currentTurnRoll, targetTurnRoll, turnBlend);

        Vector3 targetPosition = baseLocalPosition;
        float bobRoll = 0f;

        if (moving)
        {
            float amplitude = running ? runAmplitude : walkAmplitude;
            float frequency = running ? runFrequency : walkFrequency;
            float rollAngle = running ? runRollAngle : walkRollAngle;
            phase += Time.deltaTime * frequency;

            float side = Mathf.Sin(phase);
            float vertical = -Mathf.Cos(phase * 2f);
            targetPosition += new Vector3(
                side * amplitude * sideAmplitudeMultiplier,
                vertical * amplitude,
                0f);
            bobRoll = -side * rollAngle;
        }

        float followSpeed = moving ? motionFollowSpeed : returnSpeed;
        float follow = 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * Time.deltaTime);
        Quaternion targetRotation = baseLocalRotation * Quaternion.Euler(0f, 0f, bobRoll + currentTurnRoll);
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, follow);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, follow);
    }
}
