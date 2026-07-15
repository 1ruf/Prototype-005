using UnityEngine;

[DefaultExecutionOrder(10000)]
public class MouseLookSystem : MonoBehaviour
{
    [SerializeField] private Transform _playerBody;
    [SerializeField] private Transform _headTransform;
    [SerializeField] private Animator _animatedBodyAnimator;
    [SerializeField] private Transform _animatedHeadTransform;

    [Header("Setting")]
    [SerializeField] private float _mouseSensitivity = 100f;
    [SerializeField] private float _cameraMaxAngle = 90f;
    [SerializeField] private float _smoothTime = 0.1f;
    [SerializeField] private float _cameraXMoveSpeed = 3.5f;
    [SerializeField] private float _networkRotationSmoothSpeed = 14f;
    [SerializeField] private bool _followHeadPosition = true;
    [SerializeField] private Transform _headPositionTarget;

    private float _xRotation = 0f;
    private Vector2 _currentMouseDelta;
    private Vector2 _currentMouseDeltaVelocity;
    private PlayerMovement _networkPlayer;
    private Quaternion _baseLocalRotation;
    private Quaternion _baseHeadLocalRotation;
    private Vector3 _headFollowLocalPosition;
    private bool _hasHeadFollowLocalPosition;
    private Quaternion _smoothedNetworkRotation;
    private bool _hasSmoothedNetworkRotation;
    private float _visualNetworkYaw;
    private float _visualNetworkPitch;
    private bool _hasVisualNetworkLook;

    public Quaternion RawViewRotation => _hasVisualNetworkLook
        ? Quaternion.Euler(_visualNetworkPitch, _visualNetworkYaw, 0f)
        : transform.rotation;

    private void Awake()
    {
        _networkPlayer = GetComponentInParent<PlayerMovement>();
        if (_headTransform == null)
            _headTransform = transform.parent;

        _baseLocalRotation = transform.localRotation;
        _baseHeadLocalRotation = _headTransform != null ? _headTransform.localRotation : Quaternion.identity;
        ResolveAnimatedHeadTransform();
        CacheHeadFollowLocalPosition();
    }

    private void Update()
    {
        if (_networkPlayer != null && _networkPlayer.Object != null)
        {
            ApplyNetworkCameraPitch();
            return;
        }

        SetCamera();
    }

    private void LateUpdate()
    {
        FollowHeadPosition();
    }

    private void SetCamera()
    {
        if (EmoteWheelController.IsBlockingGameplayInput)
        {
            _currentMouseDelta = Vector2.zero;
            _currentMouseDeltaVelocity = Vector2.zero;
            return;
        }

        float targetMouseX = Mathf.Clamp(Input.GetAxis("Mouse X") * _mouseSensitivity * Time.deltaTime, -_cameraXMoveSpeed, _cameraXMoveSpeed);
        float targetMouseY = Input.GetAxis("Mouse Y") * _mouseSensitivity * Time.deltaTime;

        _currentMouseDelta.x = Mathf.SmoothDamp(_currentMouseDelta.x, targetMouseX, ref _currentMouseDeltaVelocity.x, _smoothTime);
        _currentMouseDelta.y = Mathf.SmoothDamp(_currentMouseDelta.y, targetMouseY, ref _currentMouseDeltaVelocity.y, _smoothTime);

        _xRotation -= _currentMouseDelta.y;
        _xRotation = Mathf.Clamp(_xRotation, -_cameraMaxAngle, _cameraMaxAngle);
        SetCameraAngle(new Vector3(_xRotation, 0f, 0f));
        SetHeadAngle(_xRotation);

        SetBodyRotation(Vector3.up * _currentMouseDelta.x);
    }

    private void SetBodyRotation(Vector3 rotation)
    {
        if (_playerBody != null)
            _playerBody.Rotate(rotation);
    }

    private void SetCameraAngle(Vector3 angle)
    {
        transform.localRotation = _baseLocalRotation * Quaternion.Euler(angle);
    }

    private void ApplyNetworkCameraPitch()
    {
        float networkYaw = _networkPlayer.BodyYaw;
        float networkPitch = _networkPlayer.CameraPitch;
        if (!_hasVisualNetworkLook)
        {
            _visualNetworkYaw = networkYaw;
            _visualNetworkPitch = networkPitch;
            _hasVisualNetworkLook = true;
        }

        _visualNetworkYaw = networkYaw;
        _visualNetworkPitch = networkPitch;

        Quaternion targetRotation = Quaternion.Euler(_visualNetworkPitch, _visualNetworkYaw, 0f) * _baseLocalRotation;

        if (!_hasSmoothedNetworkRotation)
        {
            _smoothedNetworkRotation = targetRotation;
            _hasSmoothedNetworkRotation = true;
        }

        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, _networkRotationSmoothSpeed) * Time.deltaTime);
        _smoothedNetworkRotation = Quaternion.Slerp(_smoothedNetworkRotation, targetRotation, blend);
        transform.rotation = _smoothedNetworkRotation;
        SetHeadAngle(_visualNetworkPitch);
    }

    private void SetHeadAngle(float pitch)
    {
        if (_headTransform == null || _headTransform == transform || _headTransform == _playerBody)
            return;

        _headTransform.localRotation = _baseHeadLocalRotation * Quaternion.Euler(-pitch, 0f, 0f);
    }

    private void FollowHeadPosition()
    {
        if (!_followHeadPosition)
            return;

        if (_networkPlayer != null && !_networkPlayer.IsLocalNetworkPlayer)
            return;

        if (_headPositionTarget == null)
            ResolveAnimatedHeadTransform();

        Transform source = ResolveHeadPositionSource();
        if (source == null)
            return;

        if (!_hasHeadFollowLocalPosition)
            CacheHeadFollowLocalPosition();

        transform.position = source.TransformPoint(_headFollowLocalPosition);
    }

    private Transform ResolveHeadPositionSource()
    {
        if (_animatedHeadTransform == null)
            ResolveAnimatedHeadTransform();

        if (_animatedHeadTransform != null)
            return _animatedHeadTransform;

        if (_headPositionTarget != null)
            return _headPositionTarget;

        return _headTransform;
    }

    private Vector3 GetHeadFollowPosition(Transform source)
    {
        if (_headPositionTarget != null)
        {
            if (_headPositionTarget == source || _headPositionTarget.IsChildOf(source))
                return _headPositionTarget.position;

            if (_headPositionTarget.parent == _headTransform && source != _headTransform)
                return source.TransformPoint(_headPositionTarget.localPosition);
        }

        return source.position;
    }

    private void ResolveAnimatedHeadTransform()
    {
        if (_animatedBodyAnimator == null)
            _animatedBodyAnimator = ResolveAnimatedBodyAnimator();

        if (_animatedBodyAnimator != null && _animatedBodyAnimator.isHuman)
            _animatedHeadTransform = _animatedBodyAnimator.GetBoneTransform(HumanBodyBones.Head);

        if (_animatedHeadTransform == null && _animatedBodyAnimator != null)
            _animatedHeadTransform = FindHeadByName(_animatedBodyAnimator.transform);

        if (_animatedHeadTransform == null && _headTransform != null)
            _animatedHeadTransform = _headTransform;
    }

    private void CacheHeadFollowLocalPosition()
    {
        Transform source = ResolveHeadPositionSource();
        if (source == null)
            return;

        _headFollowLocalPosition = source.InverseTransformPoint(transform.position);
        _hasHeadFollowLocalPosition = true;
    }

    private Animator ResolveAnimatedBodyAnimator()
    {
        if (_headTransform != null)
        {
            Animator headAnimator = _headTransform.GetComponentInParent<Animator>();
            if (headAnimator != null)
                return headAnimator;
        }

        PlayerMovement player = GetComponentInParent<PlayerMovement>();
        if (player == null)
            return GetComponentInParent<Animator>();

        Animator[] animators = player.GetComponentsInChildren<Animator>(true);
        foreach (Animator animator in animators)
        {
            if (animator != null && animator.isHuman)
                return animator;
        }

        foreach (Animator animator in animators)
        {
            if (animator != null && FindHeadByName(animator.transform) != null)
                return animator;
        }

        return animators.Length > 0 ? animators[0] : null;
    }

    private static Transform FindHeadByName(Transform root)
    {
        if (root.name == "Head" || root.name == "mixamorig:Head" || root.name.EndsWith(":Head"))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindHeadByName(root.GetChild(i));
            if (found != null)
                return found;
        }

        return null;
    }
}
