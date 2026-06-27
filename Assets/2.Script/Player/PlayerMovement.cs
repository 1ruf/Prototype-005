using Fusion;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour, INetworkEntityComponent
{
    [SerializeField] private float _speed;
    [SerializeField] private float _sprintMultiply;
    [SerializeField] private float _acceleration;
    [SerializeField] private float _deceleration;
    [SerializeField] private float _mass;

    [SerializeField] private Animator _camAnimator;
    [SerializeField] private Animator _visualAnimator;
    [SerializeField] private RuntimeAnimatorController _visualAnimatorController;
    [SerializeField] private float _visualCrossFadeTime = 0.12f;
    [SerializeField] private CharacterController _controller;
    [SerializeField] private Transform _groundChecker;
    [SerializeField] private float _groundDistance = 0.4f;
    [SerializeField] private LayerMask _groundMask;

    [Header("CameraSetting")]
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private float _normalFOV = 60f;
    [SerializeField] private float _sprintFOV = 67f;
    [SerializeField] private float _fovTransitionSpeed = 1f;

    private float _gravity = -9.81f;
    private Vector3 velocity;
    private Vector3 targetDirection;
    private Vector3 currentSpeed = Vector3.zero;
    private bool isGround;
    private NetworkCharacterController _networkController;
    private UnityEngine.Behaviour[] _localOnlyCameraBehaviours;
    private Renderer[] _visualRenderers;
    private bool _localPresentationConfigured;
    private string _lastVisualStateName;
    private float _lastVisualAnimatorSpeed = -1f;
    private GameObject owner;

    private const string IdleStateName = "Base Layer.Idle";
    private const string RunStateName = "Base Layer.Run";
    private const string VisualAnimatorControllerResourcePath = "PlayerVisual";

    [Networked] public float CameraPitch { get; set; }
    [Networked] public float BodyYaw { get; set; }
    [Networked] public NetworkBool IsSprinting { get; set; }
    [Networked] public NetworkBool IsMoving { get; set; }
    [Networked] public float VisualAnimationSpeed { get; set; }

    public bool IsLocalNetworkPlayer => Object == null || Object.HasInputAuthority;
    public GameObject Owner => owner != null ? owner : gameObject;

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
    }

    private void Awake()
    {
        Initialize(gameObject);

        if (_controller == null)
            _controller = GetComponent<CharacterController>();

        _networkController = GetComponent<NetworkCharacterController>();
        ResolveVisualAnimator();
    }

    private void OnEnable()
    {
        PlayerRuntimeRegistry.Register(this);

        if (_camAnimator != null)
            _camAnimator.Play("Walk");

        ResolveVisualAnimator();
        ApplyVisualAnimation(false, 1f, true);
    }

    public override void Spawned()
    {
        _networkController = GetComponent<NetworkCharacterController>();
        ResolveVisualAnimator();
        ConfigureNetworkController();
        if (Object.HasStateAuthority)
        {
            BodyYaw = transform.eulerAngles.y;
            IsMoving = false;
            VisualAnimationSpeed = 1f;
        }

        ConfigureLocalPresentation();
    }

    private void OnDisable()
    {
        PlayerRuntimeRegistry.Unregister(this);

        if (Object != null && Object.HasInputAuthority && Manager.Instance != null)
            Manager.Instance.ClearLocalPlayer(this);
    }

    private void Update()
    {
        if (Object != null)
        {
            EnsureLocalPresentation();
            RenderNetworkPresentation();
            return;
        }

        GroundCheck();
        Movement();
        Falling();
        WalkAnimation();
        UpdateLocalVisualAnimation();
        AdjustFOV();
    }

    public override void FixedUpdateNetwork()
    {
        if (_networkController == null)
            return;

        if (GetInput(out NetworkPlayerInput input))
        {
            ApplyNetworkLook(input);
            ApplyNetworkMovement(input);
        }
    }

    private float SprintCheck()
    {
        if (Input.GetKey(KeyCode.LeftShift))
        {
            if (_camAnimator != null)
                _camAnimator.SetBool("Running", true);
            return _sprintMultiply;
        }

        if (_camAnimator != null)
            _camAnimator.SetBool("Running", false);
        return 1;
    }

    private void Movement()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        targetDirection = (transform.right * x + transform.forward * z).normalized;

        if (targetDirection.sqrMagnitude > 0)
        {
            float sprintMultiple = SprintCheck();
            currentSpeed = Vector3.MoveTowards(currentSpeed, targetDirection * _speed * sprintMultiple, _acceleration * Time.deltaTime * sprintMultiple);
        }
        else
        {
            currentSpeed = Vector3.MoveTowards(currentSpeed, Vector3.zero, _deceleration * Time.deltaTime);
        }

        _controller.Move(currentSpeed * Time.deltaTime);
    }

    private void AdjustFOV()
    {
        if (_playerCamera == null)
            return;

        float targetFOV = Input.GetKey(KeyCode.LeftShift) ? _sprintFOV : _normalFOV;
        _playerCamera.fieldOfView = Mathf.Lerp(_playerCamera.fieldOfView, targetFOV, Time.deltaTime * _fovTransitionSpeed);
    }

    private void GroundCheck()
    {
        if (_groundChecker == null)
            return;

        isGround = Physics.CheckSphere(_groundChecker.position, _groundDistance, _groundMask);

        if (isGround && velocity.y < 0.1f)
            velocity.y = -3f * _mass;
    }

    private void Falling()
    {
        velocity.y += _gravity * Time.deltaTime * _mass;
        _controller.Move(velocity * Time.deltaTime);
    }

    private void WalkAnimation()
    {
        if (_camAnimator == null)
            return;

        float animationSpeed = _speed <= 0f ? 0f : Mathf.Clamp01(currentSpeed.magnitude / _speed);
        _camAnimator.speed = animationSpeed;
    }

    private void UpdateLocalVisualAnimation()
    {
        bool moving = currentSpeed.sqrMagnitude > 0.01f;
        float animationSpeed = CalculateVisualAnimationSpeed(moving, Input.GetKey(KeyCode.LeftShift), currentSpeed.magnitude);
        ApplyVisualAnimation(moving, animationSpeed, false);
    }

    private void ApplyNetworkLook(NetworkPlayerInput input)
    {
        BodyYaw = Mathf.Repeat(BodyYaw + input.LookDelta.x, 360f);
        transform.rotation = Quaternion.Euler(0f, BodyYaw, 0f);
        CameraPitch = Mathf.Clamp(CameraPitch - input.LookDelta.y, -89f, 89f);
    }

    private void ApplyNetworkMovement(NetworkPlayerInput input)
    {
        Vector3 move = transform.right * input.Move.x + transform.forward * input.Move.y;
        move = Vector3.ClampMagnitude(move, 1f);

        IsSprinting = input.Sprint;
        float sprintMultiplier = input.Sprint ? _sprintMultiply : 1f;
        _networkController.maxSpeed = _speed * sprintMultiplier;
        _networkController.acceleration = _acceleration;
        _networkController.braking = _deceleration;
        _networkController.gravity = _gravity * _mass;
        _networkController.rotationSpeed = 0f;
        _networkController.Move(move);

        currentSpeed = move * _networkController.maxSpeed;
        IsMoving = move.sqrMagnitude > 0.001f;
        VisualAnimationSpeed = CalculateVisualAnimationSpeed(IsMoving, IsSprinting, currentSpeed.magnitude);
    }

    private void ConfigureNetworkController()
    {
        if (_networkController == null)
            return;

        _networkController.rotationSpeed = 0f;
        _networkController.acceleration = _acceleration;
        _networkController.braking = _deceleration;
        _networkController.gravity = _gravity * _mass;
    }

    private void RenderNetworkPresentation()
    {
        if (Object != null && !Object.HasStateAuthority)
            transform.rotation = Quaternion.Euler(0f, BodyYaw, 0f);

        bool visualMoving = IsMoving;
        float visualAnimationSpeed = VisualAnimationSpeed;

        if (IsLocalNetworkPlayer)
        {
            Vector2 localMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool localMoving = localMove.sqrMagnitude > 0.001f;
            bool localSprinting = Input.GetKey(KeyCode.LeftShift);
            float localSpeed = _speed * (localSprinting ? _sprintMultiply : 1f);
            visualMoving = localMoving;
            visualAnimationSpeed = CalculateVisualAnimationSpeed(localMoving, localSprinting, localSpeed);
        }

        if (_playerCamera != null && IsLocalNetworkPlayer)
        {
            float targetFOV = IsSprinting ? _sprintFOV : _normalFOV;
            _playerCamera.fieldOfView = Mathf.Lerp(_playerCamera.fieldOfView, targetFOV, Time.deltaTime * _fovTransitionSpeed);
        }

        if (_camAnimator == null)
        {
            ApplyVisualAnimation(visualMoving, visualAnimationSpeed, false);
            return;
        }

        _camAnimator.SetBool("Running", IsSprinting);
        _camAnimator.speed = visualMoving ? visualAnimationSpeed : 0f;
        ApplyVisualAnimation(visualMoving, visualAnimationSpeed, false);
    }

    private float CalculateVisualAnimationSpeed(bool moving, bool sprinting, float movementSpeed)
    {
        if (!moving)
            return 1f;

        float baseSpeed = Mathf.Max(_speed, 0.001f);
        float speedRatio = Mathf.Max(0.1f, movementSpeed / baseSpeed);
        if (sprinting)
            speedRatio = Mathf.Max(speedRatio, _sprintMultiply);

        return speedRatio;
    }

    private void ApplyVisualAnimation(bool moving, float animationSpeed, bool force)
    {
        ResolveVisualAnimator();
        if (_visualAnimator == null)
            return;

        string targetStateName = moving ? RunStateName : IdleStateName;
        if (force || _lastVisualStateName != targetStateName)
        {
            if (force)
                _visualAnimator.Play(targetStateName, 0, 0f);
            else
                _visualAnimator.CrossFade(targetStateName, _visualCrossFadeTime, 0);

            _lastVisualStateName = targetStateName;
        }

        float targetSpeed = Mathf.Max(0.01f, animationSpeed);
        if (force || !Mathf.Approximately(_lastVisualAnimatorSpeed, targetSpeed))
        {
            _visualAnimator.speed = targetSpeed;
            _lastVisualAnimatorSpeed = targetSpeed;
        }
    }

    private void ResolveVisualAnimator()
    {
        if (_visualAnimator == null)
        {
            Transform visual = FindChildByName(transform, "Visual");
            if (visual != null)
            {
                if (!visual.gameObject.activeSelf)
                    visual.gameObject.SetActive(true);

                _visualAnimator = visual.GetComponentInChildren<Animator>(true);
                if (_visualAnimator == null)
                    _visualAnimator = visual.gameObject.AddComponent<Animator>();
            }
        }

        if (_visualAnimator == null)
            _visualAnimator = GetComponentInChildren<Animator>(true);

        if (_visualAnimator == null)
            return;

        if (!_visualAnimator.gameObject.activeSelf)
            _visualAnimator.gameObject.SetActive(true);

        if (_visualAnimator.runtimeAnimatorController == null)
            _visualAnimator.runtimeAnimatorController = ResolveVisualAnimatorController();

        _visualAnimator.enabled = true;
        _visualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _visualAnimator.updateMode = AnimatorUpdateMode.Normal;
        _visualAnimator.applyRootMotion = false;
    }

    private RuntimeAnimatorController ResolveVisualAnimatorController()
    {
        if (_visualAnimatorController != null)
            return _visualAnimatorController;

        _visualAnimatorController = Resources.Load<RuntimeAnimatorController>(VisualAnimatorControllerResourcePath);
        return _visualAnimatorController;
    }

    private void ConfigureLocalPresentation()
    {
        bool local = IsLocalNetworkPlayer;

        if (_playerCamera == null)
            _playerCamera = GetComponentInChildren<Camera>(true);

        if (_playerCamera != null)
        {
            _playerCamera.enabled = local;
            AudioListener listener = _playerCamera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = local;
        }

        _localOnlyCameraBehaviours = FindLocalOnlyCameraBehaviours();
        foreach (UnityEngine.Behaviour behaviour in _localOnlyCameraBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = local;
        }

        if (local && Manager.Instance != null)
            Manager.Instance.RegisterLocalPlayer(this);

        SetLocalVisualRenderersVisible(!local);
        _localPresentationConfigured = true;
    }

    private void EnsureLocalPresentation()
    {
        if (!_localPresentationConfigured)
            ConfigureLocalPresentation();
    }

    private UnityEngine.Behaviour[] FindLocalOnlyCameraBehaviours()
    {
        var behaviours = new System.Collections.Generic.List<UnityEngine.Behaviour>();
        AddBehavioursByTypeName("Unity.Cinemachine.CinemachineCamera", behaviours);
        AddBehavioursByTypeName("Unity.Cinemachine.CinemachineBrain", behaviours);
        return behaviours.ToArray();
    }

    private void AddBehavioursByTypeName(string typeName, System.Collections.Generic.List<UnityEngine.Behaviour> behaviours)
    {
        System.Type type = System.Type.GetType(typeName + ", Unity.Cinemachine");
        if (type == null)
            return;

        Component[] components = GetComponentsInChildren(type, true);
        foreach (Component component in components)
        {
            if (component is UnityEngine.Behaviour behaviour)
                behaviours.Add(behaviour);
        }
    }

    private void SetLocalVisualRenderersVisible(bool visible)
    {
        if (_visualRenderers == null || _visualRenderers.Length == 0)
        {
            Transform visual = FindChildByName(transform, "Visual");
            if (visual != null)
                _visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
        }

        if (_visualRenderers == null)
            return;

        foreach (Renderer visualRenderer in _visualRenderers)
        {
            if (visualRenderer != null)
                visualRenderer.enabled = visible;
        }
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void OnDrawGizmos()
    {
        if (_groundChecker == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(_groundChecker.position, _groundDistance);
    }
}
