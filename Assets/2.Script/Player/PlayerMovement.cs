//using UnityEngine;

//public class PlayerMovement : MonoBehaviour
//{
//    [SerializeField] private float _speed;
//    [SerializeField] private float _mass;

//    [SerializeField] private CharacterController _controller;
//    [SerializeField] private Transform _groundChecker;
//    [SerializeField] private float _groundDistance = 0.4f;
//    [SerializeField] private LayerMask _groundMask;

//    private float _gravity = -9.81f;
//    private Vector3 velocity;
//    private bool isGround;
//    void Update()
//    {
//        isGround = Physics.CheckSphere(_groundChecker.position, _groundDistance, _groundMask);

//        if (isGround && velocity.y < 0)
//        {
//            velocity.y = -3f;
//        }

//        float x = Input.GetAxisRaw("Horizontal");
//        float z = Input.GetAxisRaw("Vertical");

//        Vector3 moveDir = transform.right * x + transform.forward * z;

//        _controller.Move(moveDir.normalized * _speed * Time.deltaTime);


//        velocity.y += _gravity * Time.deltaTime * _mass;

//        _controller.Move(velocity * Time.deltaTime);
//    }
//}
















using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float _speed;
    [SerializeField] private float _acceleration;
    [SerializeField] private float _deceleration;
    [SerializeField] private float _mass;

    [SerializeField] private CharacterController _controller;
    [SerializeField] private Transform _groundChecker;
    [SerializeField] private float _groundDistance = 0.4f;
    [SerializeField] private LayerMask _groundMask;

    private float _gravity = -9.81f;
    private Vector3 velocity;
    private Vector3 currentSpeed = Vector3.zero;
    private bool isGround;

    void Update()
    {
        GroundCheck();
        Movement();
        Falling();
    }

    private void Movement()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        Vector3 targetDirection = (transform.right * x + transform.forward * z).normalized;

        if (targetDirection.sqrMagnitude > 0)
        {
            currentSpeed = Vector3.MoveTowards(
                currentSpeed,
                targetDirection * _speed,
                _acceleration * Time.deltaTime
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


    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(_groundChecker.position, _groundDistance);
    }
}
