using Fusion;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour, INetworkEntityComponent
{
    private static PlayerMovement activeLocalPlayer;

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
    private bool _networkSpawned;
    private string _lastVisualStateName;
    private float _lastVisualAnimatorSpeed = -1f;
    private Component[] _localCinemachineCameras;
    private string _lastCameraAuthorityLog;
    private NetworkPlayerHidingComponent _hidingComponent;
    private GameObject owner;

    private const string IdleStateName = "Base Layer.Idle";
    private const string RunStateName = "Base Layer.Run";
    private const string VisualAnimatorControllerResourcePath = "PlayerVisual";

    [Networked] public float CameraPitch { get; set; }
    [Networked] public float BodyYaw { get; set; }
    [Networked] public NetworkBool IsSprinting { get; set; }
    [Networked] public NetworkBool IsMoving { get; set; }
    [Networked] public float VisualAnimationSpeed { get; set; }

    public bool IsLocalNetworkPlayer => IsLocalPlayerObject();
    public Camera PlayerCamera
    {
        get
        {
            return Camera.main;
        }
    }

    public GameObject Owner => owner != null ? owner : gameObject;

    public void RefreshLocalCameraPresentation()
    {
        if (!IsLocalNetworkPlayer)
            return;

        activeLocalPlayer = this;
        EnforceSingleLocalCamera();
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
    }

    public void RequestNetworkPowerChange(string key, bool value)
    {
        if (Object == null)
        {
            NetworkPowerRuntime.ApplyPower(key, value);
            return;
        }

        NetworkString<_64> networkKey = key;
        if (Object.HasStateAuthority)
        {
            NetworkPowerRuntime.ApplyPower(key, value);
            RPC_ApplyNetworkPower(networkKey, value);
            return;
        }

        RPC_RequestNetworkPower(networkKey, value);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestNetworkPower(NetworkString<_64> key, NetworkBool value)
    {
        NetworkPowerRuntime.ApplyPower(key.ToString(), value);
        RPC_ApplyNetworkPower(key, value);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyNetworkPower(NetworkString<_64> key, NetworkBool value)
    {
        NetworkPowerRuntime.ApplyPower(key.ToString(), value);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestNetworkPowerSnapshot()
    {
        NetworkPowerRuntime.ForEachPowerState((key, value) =>
        {
            RPC_ApplyNetworkPower(key, value);
        });
    }

    private void Awake()
    {
        Initialize(gameObject);

        if (_controller == null)
            _controller = GetComponent<CharacterController>();

        _networkController = GetComponent<NetworkCharacterController>();
        _hidingComponent = GetComponent<NetworkPlayerHidingComponent>();
        ResolveVisualAnimator();
        DisableNetworkCameraUntilSpawned();
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
        _networkSpawned = true;
        _networkController = GetComponent<NetworkCharacterController>();
        _hidingComponent = GetComponent<NetworkPlayerHidingComponent>();
        ResolveVisualAnimator();
        ConfigureNetworkController();
        if (Object.HasStateAuthority)
        {
            BodyYaw = transform.eulerAngles.y;
            IsMoving = false;
            VisualAnimationSpeed = 1f;
        }

        ConfigureLocalPresentation();

        if (Object.HasInputAuthority && !Object.HasStateAuthority)
            RPC_RequestNetworkPowerSnapshot();
    }

    private void OnDisable()
    {
        _networkSpawned = false;
        PlayerRuntimeRegistry.Unregister(this);

        if (Object != null && Object.HasInputAuthority && Manager.Instance != null)
            Manager.Instance.ClearLocalPlayer(this);
    }

    private void Update()
    {
        if (Object != null)
        {
            if (!_networkSpawned)
            {
                DisableNetworkCameraUntilSpawned();
                return;
            }

            EnsureLocalPresentation();
            RenderNetworkPresentation();
            return;
        }

        GroundCheck();
        if (IsHidingInputBlocked())
        {
            currentSpeed = Vector3.MoveTowards(currentSpeed, Vector3.zero, _deceleration * Time.deltaTime);
            WalkAnimation();
            UpdateLocalVisualAnimation();
            return;
        }

        Movement();
        Falling();
        WalkAnimation();
        UpdateLocalVisualAnimation();
        AdjustFOV();
    }

    public override void FixedUpdateNetwork()
    {
        if (!_networkSpawned)
            return;

        if (IsHidingInputBlocked())
        {
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
        ApplyLocalFOV(Input.GetKey(KeyCode.LeftShift));
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
        bool localInputBlocked = IsHidingInputBlocked();

        if (IsLocalNetworkPlayer && !localInputBlocked)
            EnforceSingleLocalCamera();

        if (Object != null && !Object.HasStateAuthority)
            transform.rotation = Quaternion.Euler(0f, BodyYaw, 0f);

        bool visualMoving = IsMoving;
        float visualAnimationSpeed = VisualAnimationSpeed;

        if (IsLocalNetworkPlayer)
        {
            Vector2 localMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool localMoving = !localInputBlocked && localMove.sqrMagnitude > 0.001f;
            bool localSprinting = !localInputBlocked && Input.GetKey(KeyCode.LeftShift);
            float localSpeed = _speed * (localSprinting ? _sprintMultiply : 1f);
            visualMoving = localMoving;
            visualAnimationSpeed = CalculateVisualAnimationSpeed(localMoving, localSprinting, localSpeed);
        }

        if (IsLocalNetworkPlayer && !localInputBlocked)
            ApplyLocalFOV(Input.GetKey(KeyCode.LeftShift));

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

    private bool IsHidingInputBlocked()
    {
        if (_hidingComponent == null)
            _hidingComponent = GetComponent<NetworkPlayerHidingComponent>();

        return _hidingComponent != null && _hidingComponent.BlocksPlayerInput;
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

        SetOwnedCamerasActive(false);

        _localOnlyCameraBehaviours = FindLocalOnlyCameraBehaviours();
        _localCinemachineCameras = FindCinemachineCameras();
        foreach (UnityEngine.Behaviour behaviour in _localOnlyCameraBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = local;
        }

        if (local && Manager.Instance != null)
            Manager.Instance.RegisterLocalPlayer(this);

        if (local)
        {
            activeLocalPlayer = this;
            EnforceSingleLocalCamera();
        }
        else
        {
            activeLocalPlayer?.EnforceSingleLocalCamera();
        }

        SetLocalVisualRenderersVisible(!local);
        _localPresentationConfigured = true;

        Debug.Log($"PlayerMovement: LocalPresentation local={local}, localPlayer={Runner?.LocalPlayer}, inputAuthority={Object?.InputAuthority}, object={name}.");
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
        _localPresentationConfigured = false;
        if (activeLocalPlayer == this)
            activeLocalPlayer = null;

        DisableNetworkCameraUntilSpawned();
    }

    private void DisableNetworkCameraUntilSpawned()
    {
        if (GetComponent<NetworkObject>() == null)
            return;

        SetOwnedCamerasActive(false);

        UnityEngine.Behaviour[] behaviours = FindLocalOnlyCameraBehaviours();
        foreach (UnityEngine.Behaviour behaviour in behaviours)
        {
            if (behaviour != null)
                behaviour.enabled = false;
        }
    }

    private void EnforceSingleLocalCamera()
    {
        Camera sceneCamera = Camera.main != null ? Camera.main : FindSceneMainCamera();
        if (sceneCamera == null)
            return;

        sceneCamera.enabled = true;
        SetCameraTag(sceneCamera, true);

        AudioListener sceneListener = sceneCamera.GetComponent<AudioListener>();
        if (sceneListener != null)
            sceneListener.enabled = true;

        SetCinemachineBrain(sceneCamera.gameObject, true);
        int disabledPlayerCameras = DisableAllPlayerCameraComponents();
        int disabledRemoteVirtualCameras = SetPlayerVirtualCamerasForLocalOwner();

        string cameraAuthorityLog = $"owner={name}, sceneCamera={GetHierarchyPath(sceneCamera.transform)}, localPlayer={Runner?.LocalPlayer}, inputAuthority={Object?.InputAuthority}, disabledPlayerCameras={disabledPlayerCameras}, disabledRemoteVirtualCameras={disabledRemoteVirtualCameras}";
        if (_lastCameraAuthorityLog != cameraAuthorityLog)
        {
            _lastCameraAuthorityLog = cameraAuthorityLog;
            Debug.Log($"PlayerMovement: CameraAuthority {cameraAuthorityLog}.");
        }
    }

    private static Camera FindSceneMainCamera()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera != null && camera.name == "Main Camera" && camera.GetComponentInParent<PlayerMovement>() == null)
                return camera;
        }

        foreach (Camera camera in cameras)
        {
            if (camera != null && camera.GetComponentInParent<PlayerMovement>() == null)
                return camera;
        }

        return null;
    }

    private void SetOwnedCamerasActive(bool local)
    {
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        foreach (Camera camera in cameras)
        {
            if (camera == null)
                continue;

            camera.enabled = false;
            SetCameraTag(camera, false);

            AudioListener[] listeners = camera.GetComponents<AudioListener>();
            foreach (AudioListener listener in listeners)
            {
                if (listener != null)
                    listener.enabled = false;
            }

            SetCinemachineBrain(camera.gameObject, false);
        }
    }

    private static int DisableAllPlayerCameraComponents()
    {
        int disabledCameras = 0;
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera == null || camera.GetComponentInParent<PlayerMovement>() == null)
                continue;

            if (camera.enabled)
                disabledCameras++;

            camera.enabled = false;
            SetCameraTag(camera, false);

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;

            SetCinemachineBrain(camera.gameObject, false);
        }

        return disabledCameras;
    }

    private static int SetPlayerVirtualCamerasForLocalOwner()
    {
        int disabledRemoteVirtualCameras = 0;
        System.Type cameraType = System.Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
        if (cameraType == null)
            return 0;

        UnityEngine.Object[] virtualCameras = FindObjectsByType(cameraType, FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (UnityEngine.Object virtualCameraObject in virtualCameras)
        {
            Component virtualCamera = virtualCameraObject as Component;
            if (virtualCamera == null)
                continue;

            PlayerMovement owner = virtualCamera.GetComponentInParent<PlayerMovement>();
            if (owner == null)
                continue;

            bool shouldEnable = owner == activeLocalPlayer;
            if (virtualCamera is UnityEngine.Behaviour behaviour)
            {
                if (behaviour.enabled && !shouldEnable)
                    disabledRemoteVirtualCameras++;

                behaviour.enabled = shouldEnable;
            }
        }

        return disabledRemoteVirtualCameras;
    }

    private static void SetCameraTag(Camera camera, bool mainCamera)
    {
        if (camera == null)
            return;

        if (mainCamera)
        {
            camera.tag = "MainCamera";
            return;
        }

        if (camera.CompareTag("MainCamera"))
            camera.tag = "Untagged";
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void SetCinemachineBrain(GameObject cameraObject, bool enabled)
    {
        System.Type brainType = System.Type.GetType("Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine");
        if (brainType == null || cameraObject == null)
            return;

        Component brain = cameraObject.GetComponent(brainType);
        if (brain is UnityEngine.Behaviour behaviour)
            behaviour.enabled = enabled;
    }

    private void EnsureLocalPresentation()
    {
        if (!_localPresentationConfigured)
            ConfigureLocalPresentation();
    }

    private void ApplyLocalFOV(bool sprinting)
    {
        float targetFOV = sprinting ? _sprintFOV : _normalFOV;
        float currentFOV = GetCurrentLocalFOV();
        float nextFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime * _fovTransitionSpeed);

        Camera targetCamera = Camera.main;
        if (targetCamera != null)
            targetCamera.fieldOfView = nextFOV;

        ApplyCinemachineFOV(nextFOV);
    }

    private float GetCurrentLocalFOV()
    {
        if (_localCinemachineCameras != null)
        {
            foreach (Component virtualCamera in _localCinemachineCameras)
            {
                if (TryGetCinemachineFOV(virtualCamera, out float fov))
                    return fov;
            }
        }

        Camera targetCamera = Camera.main;
        return targetCamera != null ? targetCamera.fieldOfView : _normalFOV;
    }

    private void ApplyCinemachineFOV(float fov)
    {
        if (_localCinemachineCameras == null || _localCinemachineCameras.Length == 0)
            _localCinemachineCameras = FindCinemachineCameras();

        if (_localCinemachineCameras == null)
            return;

        foreach (Component virtualCamera in _localCinemachineCameras)
            TrySetCinemachineFOV(virtualCamera, fov);
    }

    private Component[] FindCinemachineCameras()
    {
        System.Type cameraType = System.Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
        if (cameraType == null)
            return System.Array.Empty<Component>();

        Component[] components = GetComponentsInChildren(cameraType, true);
        return components ?? System.Array.Empty<Component>();
    }

    private static bool TryGetCinemachineFOV(Component virtualCamera, out float fov)
    {
        fov = 0f;
        if (virtualCamera == null)
            return false;

        if (!TryGetCinemachineLens(virtualCamera, out object lens, out _, out _))
            return false;

        System.Reflection.FieldInfo fieldOfView = lens.GetType().GetField("FieldOfView");
        if (fieldOfView == null || fieldOfView.GetValue(lens) is not float value)
            return false;

        fov = value;
        return true;
    }

    private static bool TrySetCinemachineFOV(Component virtualCamera, float fov)
    {
        if (virtualCamera == null)
            return false;

        if (!TryGetCinemachineLens(virtualCamera, out object lens, out System.Reflection.PropertyInfo lensProperty, out System.Reflection.FieldInfo lensField))
            return false;

        System.Reflection.FieldInfo fieldOfView = lens.GetType().GetField("FieldOfView");
        if (fieldOfView == null)
            return false;

        fieldOfView.SetValue(lens, fov);

        if (lensProperty != null)
        {
            lensProperty.SetValue(virtualCamera, lens);
            return true;
        }

        if (lensField != null)
        {
            lensField.SetValue(virtualCamera, lens);
            return true;
        }

        return false;
    }

    private static bool TryGetCinemachineLens(Component virtualCamera, out object lens, out System.Reflection.PropertyInfo lensProperty, out System.Reflection.FieldInfo lensField)
    {
        lens = null;
        lensProperty = null;
        lensField = null;

        if (virtualCamera == null)
            return false;

        System.Type type = virtualCamera.GetType();
        lensProperty = type.GetProperty("Lens");
        if (lensProperty != null)
        {
            lens = lensProperty.GetValue(virtualCamera);
            return lens != null;
        }

        lensField = type.GetField("Lens");
        if (lensField == null)
            return false;

        lens = lensField.GetValue(virtualCamera);
        return lens != null;
    }

    private UnityEngine.Behaviour[] FindLocalOnlyCameraBehaviours()
    {
        var behaviours = new System.Collections.Generic.List<UnityEngine.Behaviour>();
        AddBehavioursByTypeName("Unity.Cinemachine.CinemachineCamera", behaviours);
        AddBehavioursByTypeName("Unity.Cinemachine.CinemachineBrain", behaviours);
        AddBehavioursByTypeName(nameof(MouseLookSystem), behaviours);
        AddBehavioursByTypeName(nameof(CameraBobbingController), behaviours);
        AddBehavioursByTypeName(nameof(CameraShakeController), behaviours);
        return behaviours.ToArray();
    }

    private void AddBehavioursByTypeName(string typeName, System.Collections.Generic.List<UnityEngine.Behaviour> behaviours)
    {
        System.Type type = System.Type.GetType(typeName + ", Unity.Cinemachine") ??
                           System.Type.GetType(typeName + ", Assembly-CSharp");
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
