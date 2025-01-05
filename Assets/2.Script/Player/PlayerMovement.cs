using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float _speed;
    [SerializeField] private float _sprintMultiply;
    [SerializeField] private float _acceleration;
    [SerializeField] private float _deceleration;
    [SerializeField] private float _mass;

    [SerializeField] private Animator _camAnimator;
    [SerializeField] private CharacterController _controller;
    [SerializeField] private Transform _groundChecker;
    [SerializeField] private float _groundDistance = 0.4f;
    [SerializeField] private LayerMask _groundMask;

    [Header("CameraSetting")]
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private float _normalFOV = 60f;
    [SerializeField] private float _sprintFOV = 75f;
    [SerializeField] private float _fovTransitionSpeed = 1f;

    private float _gravity = -9.81f;
    private Vector3 velocity;
    private Vector3 targetDirection;
    private Vector3 currentSpeed = Vector3.zero;
    private bool isGround;

    private void OnEnable()
    {
        _camAnimator.Play("Walk");
    }

    void Update()
    {
        GroundCheck();
        Movement();
        Falling();
        WalkAnimation();
        AdjustFOV();
    }

    private float SprintCheck()
    {
        if (Input.GetKey(KeyCode.LeftShift))
        {
            return _sprintMultiply;
        }
        return 1;
    }

    private void Movement()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        targetDirection = (transform.right * x + transform.forward * z).normalized;

        if (targetDirection.sqrMagnitude > 0)
        {
            currentSpeed = Vector3.MoveTowards(
                currentSpeed,
                targetDirection * _speed * SprintCheck(),
                _acceleration * Time.deltaTime * SprintCheck()
            );
        }
        else
        {
            currentSpeed = Vector3.MoveTowards(
                currentSpeed,
                Vector3.zero,
                _deceleration * Time.deltaTime
            );
        }

        _controller.Move(currentSpeed * Time.deltaTime);
    }

    private void AdjustFOV()
    {
        float targetFOV = Input.GetKey(KeyCode.LeftShift) ? _sprintFOV : _normalFOV;
        _playerCamera.fieldOfView = Mathf.Lerp(
            _playerCamera.fieldOfView,
            targetFOV,
            Time.deltaTime * _fovTransitionSpeed
        );
    }

    private void GroundCheck()
    {
        isGround = Physics.CheckSphere(_groundChecker.position, _groundDistance, _groundMask);

        if (isGround && velocity.y < 0.1f)
        {
            velocity.y = -3f * _mass;
        }
    }

    private void Falling()
    {
        velocity.y += _gravity * Time.deltaTime * _mass;
        _controller.Move(velocity * Time.deltaTime);
    }


    private void WalkAnimation()
    {
        float currentSpeedMagnitude = currentSpeed.magnitude;

        float animationSpeed = currentSpeedMagnitude / _speed;

        animationSpeed = Mathf.Clamp(animationSpeed, 0, 2f);

        _camAnimator.speed = animationSpeed;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(_groundChecker.position, _groundDistance);
    }
}
