using UnityEngine;

public class MouseLookSystem : MonoBehaviour
{
    [SerializeField] private Transform _playerBody;
    [Header("PlayerSetting")]
    [SerializeField] private float _mouseSensitivity = 100f;
    [SerializeField] private float _cameraMaxAngle = 80f;

    private float _xRotation = 0f;

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * _mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * _mouseSensitivity * Time.deltaTime;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -_cameraMaxAngle, _cameraMaxAngle);

        transform.localRotation = Quaternion.Euler(_xRotation,0f,0f);
        _playerBody.Rotate(Vector3.up * mouseX);
    }
}
