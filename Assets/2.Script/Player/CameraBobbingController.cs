using UnityEngine;

public class CameraBobbingController : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private float walkAmplitude = 0.035f;
    [SerializeField] private float runAmplitude = 0.06f;
    [SerializeField] private float walkFrequency = 7f;
    [SerializeField] private float runFrequency = 10f;
    [SerializeField] private float sideAmplitudeMultiplier = 0.45f;
    [SerializeField] private float walkRollAngle = 1.2f;
    [SerializeField] private float runRollAngle = 2f;
    [SerializeField] private float returnSpeed = 10f;

    private float phase;
    private Vector3 baseLocalPosition;

    private void Awake()
    {
        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        baseLocalPosition = transform.localPosition;
    }

    private void LateUpdate()
    {
        if (playerMovement != null && !playerMovement.IsLocalNetworkPlayer)
            return;

        Vector2 move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool moving = move.sqrMagnitude > 0.01f;
        bool running = playerMovement != null ? playerMovement.IsSprintActive : Input.GetKey(KeyCode.LeftShift);

        if (moving)
        {
            float amplitude = running ? runAmplitude : walkAmplitude;
            float frequency = running ? runFrequency : walkFrequency;
            float rollAngle = running ? runRollAngle : walkRollAngle;
            phase += Time.deltaTime * frequency;

            float side = Mathf.Sin(phase);
            Vector3 offset = new Vector3(
                side * amplitude * sideAmplitudeMultiplier,
                Mathf.Abs(Mathf.Cos(phase)) * amplitude,
                0f);

            transform.localPosition = baseLocalPosition + offset;
            transform.localRotation *= Quaternion.Euler(0f, 0f, -side * rollAngle);
            return;
        }

        transform.localPosition = Vector3.Lerp(transform.localPosition, baseLocalPosition, Time.deltaTime * returnSpeed);
    }
}
