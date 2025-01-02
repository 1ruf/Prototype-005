using UnityEngine;

public class MouseLookSystem : MonoBehaviour
{
    [SerializeField] private Transform _playerBody;
    [Header("Setting")]
    [SerializeField] private float _mouseSensitivity = 100f;
    [SerializeField] private float _cameraMaxAngle = 90f;
    [SerializeField] private float _smoothTime = 0.1f;
    [SerializeField] private float _cameraXMoveSpeed = 3.5f;

    private float _xRotation = 0f;
    private Vector2 _currentMouseDelta;
    private Vector2 _currentMouseDeltaVelocity;

    void Update()
    {
        float targetMouseX = Mathf.Clamp(Input.GetAxis("Mouse X") * _mouseSensitivity * Time.deltaTime,-_cameraXMoveSpeed, _cameraXMoveSpeed);
        float targetMouseY = Input.GetAxis("Mouse Y") * _mouseSensitivity * Time.deltaTime;

        _currentMouseDelta.x = Mathf.SmoothDamp(_currentMouseDelta.x, targetMouseX, ref _currentMouseDeltaVelocity.x, _smoothTime);
        _currentMouseDelta.y = Mathf.SmoothDamp(_currentMouseDelta.y, targetMouseY, ref _currentMouseDeltaVelocity.y, _smoothTime);

        _xRotation -= _currentMouseDelta.y;
        _xRotation = Mathf.Clamp(_xRotation, -_cameraMaxAngle, _cameraMaxAngle);

        transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);

        _playerBody.Rotate(Vector3.up * _currentMouseDelta.x);
    }

}
