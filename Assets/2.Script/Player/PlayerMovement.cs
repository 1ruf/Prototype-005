using Fusion;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour, INetworkEntityComponent
{
    [Header("Movement")]
    [SerializeField] private float _speed;
    [SerializeField] private float _sprintMultiply;
    [SerializeField] private float _acceleration;
    [SerializeField] private float _deceleration;
    [SerializeField] private float _mass;
    [SerializeField] private PlayerStamina _stamina;

    // Kept on PlayerMovement so existing prefab and scene serialization remains valid.
    // PlayerAnimationPresentation consumes these values until the prefab is explicitly migrated.
    [SerializeField, HideInInspector] private Animator _camAnimator;
    [SerializeField, HideInInspector] private Animator _visualAnimator;
    [SerializeField, HideInInspector] private RuntimeAnimatorController _visualAnimatorController;
    [SerializeField, HideInInspector] private float _visualCrossFadeTime = 0.12f;

    [Header("Collision")]
    [SerializeField] private CharacterController _controller;
    [SerializeField] private Transform _groundChecker;
    [SerializeField] private float _groundDistance = 0.4f;
    [SerializeField] private LayerMask _groundMask;

    // Kept on PlayerMovement so existing prefab and scene serialization remains valid.
    // PlayerCameraPresentation consumes these values until the prefab is explicitly migrated.
    [SerializeField, HideInInspector] private Camera _playerCamera;
    [SerializeField, HideInInspector] private float _normalFOV = 60f;
    [SerializeField, HideInInspector] private float _sprintFOV = 67f;
    [SerializeField, HideInInspector] private float _fovTransitionSpeed = 1f;

    private const float Gravity = -9.81f;

    private Vector3 velocity;
    private Vector3 targetDirection;
    private Vector3 currentSpeed = Vector3.zero;
    private bool isGround;
    private NetworkCharacterController _networkController;
    private NetworkPlayerHidingComponent _hidingComponent;
    private PlayerCameraPresentation _cameraPresentation;
    private PlayerAnimationPresentation _animationPresentation;
    private PlayerNetworkPowerBridge _networkPowerBridge;
    private bool _networkSpawned;
    private GameObject owner;

    [Networked] public float CameraPitch { get; set; }
    [Networked] public float BodyYaw { get; set; }
    [Networked] public NetworkBool IsSprinting { get; set; }
    [Networked] public NetworkBool IsMoving { get; set; }
    [Networked] public float VisualAnimationSpeed { get; set; }

    public bool IsLocalNetworkPlayer => IsLocalPlayerObject();
    public Camera PlayerCamera => _cameraPresentation != null ? _cameraPresentation.PlayerCamera : Camera.main;
    public GameObject Owner => owner != null ? owner : gameObject;
    public bool IsSprintActive => _stamina != null ? _stamina.IsSprinting : IsSprinting;

    internal bool HasNetworkPowerObject => Object != null;
    internal bool HasNetworkPowerStateAuthority => Object != null && Object.HasStateAuthority;
    internal bool HasNetworkPowerInputAuthority => Object != null && Object.HasInputAuthority;

    public void RefreshLocalCameraPresentation()
    {
        EnsureSupportComponents();
        _cameraPresentation.RefreshLocalCameraPresentation();
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
    }

    public void RequestNetworkPowerChange(string key, bool value)
    {
        EnsureSupportComponents();
        _networkPowerBridge.RequestNetworkPowerChange(key, value);
    }

    public void RequestNetworkPowerToggle(string key)
    {
        EnsureSupportComponents();
        _networkPowerBridge.RequestNetworkPowerToggle(key);
    }

    internal void SendNetworkPowerRequest(NetworkString<_64> key, NetworkBool value)
    {
        RPC_RequestNetworkPower(key, value);
    }

    internal void SendNetworkPowerToggle(NetworkString<_64> key)
    {
        RPC_RequestNetworkPowerToggle(key);
    }

    internal void BroadcastNetworkPower(NetworkString<_64> key, NetworkBool value)
    {
        RPC_ApplyNetworkPower(key, value);
    }

    internal void SendNetworkPowerSnapshotRequest()
    {
        RPC_RequestNetworkPowerSnapshot();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestNetworkPower(NetworkString<_64> key, NetworkBool value)
    {
        EnsureSupportComponents();
        _networkPowerBridge.ReceiveRequestAtStateAuthority(key, value);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestNetworkPowerToggle(NetworkString<_64> key)
    {
        EnsureSupportComponents();
        _networkPowerBridge.ReceiveToggleRequestAtStateAuthority(key);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyNetworkPower(NetworkString<_64> key, NetworkBool value)
    {
        EnsureSupportComponents();
        _networkPowerBridge.ReceiveReplicatedState(key, value);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestNetworkPowerSnapshot()
    {
        EnsureSupportComponents();
        _networkPowerBridge.ReceiveSnapshotRequestAtStateAuthority();
    }

    private void Awake()
    {
        Initialize(gameObject);

        if (_controller == null)
            _controller = GetComponent<CharacterController>();

        if (_stamina == null)
            _stamina = Owner.GetComponentInChildren<PlayerStamina>(true);

        _networkController = GetComponent<NetworkCharacterController>();
        _hidingComponent = Owner.GetComponentInChildren<NetworkPlayerHidingComponent>(true);
        EnsureSupportComponents();
        _cameraPresentation.DisableUntilNetworkSpawned();
    }

    private void OnEnable()
    {
        PlayerRuntimeRegistry.Register(this);
        EnsureSupportComponents();
        _animationPresentation.HandleOwnerEnabled();
    }

    public override void Spawned()
    {
        _networkSpawned = true;
        _networkController = GetComponent<NetworkCharacterController>();
        _hidingComponent = Owner.GetComponentInChildren<NetworkPlayerHidingComponent>(true);
        EnsureSupportComponents();
        ConfigureNetworkController();

        if (Object.HasStateAuthority)
        {
            BodyYaw = transform.eulerAngles.y;
            IsMoving = false;
            VisualAnimationSpeed = 1f;
        }

        ConfigureLocalPresentation();
        _networkPowerBridge.RequestSnapshotIfNeeded();
    }

    private void OnDisable()
    {
        _networkSpawned = false;
        PlayerRuntimeRegistry.Unregister(this);
        _cameraPresentation?.HandleOwnerDisabled();

        if (Object != null && Object.HasInputAuthority && Manager.Instance != null)
            Manager.Instance.ClearLocalPlayer(this);
    }

    private void Update()
    {
        if (Object != null)
        {
            if (!_networkSpawned)
            {
                _cameraPresentation?.DisableUntilNetworkSpawned();
                return;
            }

            EnsureLocalPresentation();
            RenderNetworkPresentation();
            return;
        }

        GroundCheck();
        if (IsHidingInputBlocked())
        {
            _stamina?.Tick(Time.deltaTime, false);
            currentSpeed = Vector3.MoveTowards(currentSpeed, Vector3.zero, _deceleration * Time.deltaTime);
            UpdateOfflinePresentation();
            return;
        }

        Movement();
        Falling();
        UpdateOfflinePresentation();
        AdjustFOV();
    }

    public override void FixedUpdateNetwork()
    {
        if (!_networkSpawned)
            return;

        if (IsHidingInputBlocked())
        {
            _stamina?.Tick(Runner != null ? Runner.DeltaTime : Time.deltaTime, false);
            if (Object != null && Object.HasStateAuthority)
            {
                IsMoving = false;
                IsSprinting = false;
                VisualAnimationSpeed = 1f;
            }

            return;
        }

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
        bool sprinting = _stamina == null
            ? Input.GetKey(KeyCode.LeftShift)
            : _stamina.Tick(Time.deltaTime, Input.GetKey(KeyCode.LeftShift));

        return sprinting ? _sprintMultiply : 1f;
    }

    private void Movement()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        targetDirection = (transform.right * x + transform.forward * z).normalized;

        if (targetDirection.sqrMagnitude > 0)
        {
            float sprintMultiple = SprintCheck();
            currentSpeed = Vector3.MoveTowards(
                currentSpeed,
                targetDirection * _speed * sprintMultiple,
                _acceleration * Time.deltaTime * sprintMultiple);
        }
        else
        {
            _stamina?.Tick(Time.deltaTime, false);
            currentSpeed = Vector3.MoveTowards(currentSpeed, Vector3.zero, _deceleration * Time.deltaTime);
        }

        _controller.Move(currentSpeed * Time.deltaTime);
    }

    private void AdjustFOV()
    {
        EnsureSupportComponents();
        _cameraPresentation.ApplySprintFOV(IsSprintActive);
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
        velocity.y += Gravity * Time.deltaTime * _mass;
        _controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateOfflinePresentation()
    {
        EnsureSupportComponents();
        bool moving = currentSpeed.sqrMagnitude > 0.01f;
        float animationSpeed = _animationPresentation.CalculateAnimationSpeed(
            moving,
            IsSprintActive,
            currentSpeed.magnitude,
            _speed,
            _sprintMultiply);
        _animationPresentation.PresentMotion(moving, IsSprintActive, animationSpeed);
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
        bool moving = move.sqrMagnitude > 0.001f;
        bool sprinting = input.Sprint && moving;
        if (_stamina != null)
        {
            float deltaTime = Runner != null ? Runner.DeltaTime : Time.deltaTime;
            sprinting = _stamina.Tick(deltaTime, sprinting);
        }

        IsSprinting = sprinting;
        float sprintMultiplier = sprinting ? _sprintMultiply : 1f;
        _networkController.maxSpeed = _speed * sprintMultiplier;
        _networkController.acceleration = _acceleration;
        _networkController.braking = _deceleration;
        _networkController.gravity = Gravity * _mass;
        _networkController.rotationSpeed = 0f;
        _networkController.Move(move);

        currentSpeed = move * _networkController.maxSpeed;
        IsMoving = moving;
        VisualAnimationSpeed = _animationPresentation.CalculateAnimationSpeed(
            IsMoving,
            IsSprinting,
            currentSpeed.magnitude,
            _speed,
            _sprintMultiply);
    }

    private void ConfigureNetworkController()
    {
        if (_networkController == null)
            return;

        _networkController.rotationSpeed = 0f;
        _networkController.acceleration = _acceleration;
        _networkController.braking = _deceleration;
        _networkController.gravity = Gravity * _mass;
    }

    private void RenderNetworkPresentation()
    {
        bool localInputBlocked = IsHidingInputBlocked();
        bool local = IsLocalNetworkPlayer;

        _cameraPresentation.TickLocalPresentation(local, localInputBlocked, IsSprintActive);

        if (Object != null && !Object.HasStateAuthority)
            transform.rotation = Quaternion.Euler(0f, BodyYaw, 0f);

        bool visualMoving = IsMoving;
        bool presentationSprinting = IsSprinting;
        float visualAnimationSpeed = VisualAnimationSpeed;

        if (local)
        {
            Vector2 localMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool localMoving = !localInputBlocked && localMove.sqrMagnitude > 0.001f;
            bool localSprinting = !localInputBlocked && IsSprintActive;
            float localSpeed = _speed * (localSprinting ? _sprintMultiply : 1f);
            visualMoving = localMoving;
            presentationSprinting = localSprinting;
            visualAnimationSpeed = _animationPresentation.CalculateAnimationSpeed(
                localMoving,
                localSprinting,
                localSpeed,
                _speed,
                _sprintMultiply);
        }

        _animationPresentation.PresentMotion(visualMoving, presentationSprinting, visualAnimationSpeed);
    }

    private bool IsHidingInputBlocked()
    {
        if (_hidingComponent == null)
            _hidingComponent = Owner.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

        return _hidingComponent != null && _hidingComponent.BlocksPlayerInput;
    }

    private void ConfigureLocalPresentation()
    {
        EnsureSupportComponents();
        bool local = IsLocalNetworkPlayer;

        _cameraPresentation.ConfigureLocalPresentation(local);
        _animationPresentation.ConfigureLocalPlayer(local);

        if (local && Manager.Instance != null)
            Manager.Instance.RegisterLocalPlayer(this);
    }

    private void EnsureLocalPresentation()
    {
        EnsureSupportComponents();
        if (!_cameraPresentation.IsConfigured)
            ConfigureLocalPresentation();
    }

    private bool IsLocalPlayerObject()
    {
        if (Object == null)
            return true;

        if (!_networkSpawned)
            return false;

        if (Runner == null || !Runner.IsRunning)
            return false;

        NetworkObject localPlayerObject = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (localPlayerObject != null)
            return localPlayerObject == Object;

        return Object.HasInputAuthority && Object.InputAuthority == Runner.LocalPlayer;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _networkSpawned = false;
        _cameraPresentation?.HandleNetworkDespawned();
    }

    private void EnsureSupportComponents()
    {
        if (_cameraPresentation == null)
        {
            _cameraPresentation = GetComponentInChildren<PlayerCameraPresentation>(true);
            if (_cameraPresentation == null)
                _cameraPresentation = gameObject.AddComponent<PlayerCameraPresentation>();

            _cameraPresentation.Initialize(this, _playerCamera, _normalFOV, _sprintFOV, _fovTransitionSpeed);
        }

        if (_animationPresentation == null)
        {
            _animationPresentation = GetComponentInChildren<PlayerAnimationPresentation>(true);
            if (_animationPresentation == null)
                _animationPresentation = gameObject.AddComponent<PlayerAnimationPresentation>();

            _animationPresentation.Initialize(
                this,
                _camAnimator,
                _visualAnimator,
                _visualAnimatorController,
                _visualCrossFadeTime);
        }

        if (_networkPowerBridge == null)
        {
            _networkPowerBridge = GetComponentInChildren<PlayerNetworkPowerBridge>(true);
            if (_networkPowerBridge == null)
                _networkPowerBridge = gameObject.AddComponent<PlayerNetworkPowerBridge>();

            _networkPowerBridge.Initialize(this);
        }
    }

    private void OnDrawGizmos()
    {
        if (_groundChecker == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(_groundChecker.position, _groundDistance);
    }
}
