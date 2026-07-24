using Fusion;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NavMeshAgent))]
public class CSHEnemy : NetworkBehaviour, INetworkEntityComponent
{
    private enum EnemyStateId
    {
        Idle = 0,
        Patrol = 1,
        Chase = 2,
        Investigate = 3,
        Lure = 4
    }

    private interface IEnemyState
    {
        EnemyStateId Id { get; }
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }

    // Legacy serialized configuration remains on the coordinator so existing prefab data migrates losslessly.
    [SerializeField] private Transform target;
    [SerializeField] private Transform visual;
    [SerializeField] private EnemyAnimationDriver animationDriver;
    [SerializeField, HideInInspector] private GameObject ui;
    [SerializeField, HideInInspector] private float moveSpeed = 5f;
    [SerializeField, HideInInspector] private float patrolSpeed = 2.2f;
    [SerializeField, HideInInspector] private float rotationSpeed = 3f;
    [SerializeField, HideInInspector] private float detectionRange = 12f;
    [SerializeField, HideInInspector] private float proximityDetectionRange = 4f;
    [SerializeField, HideInInspector] private float loseRange = 18f;
    [SerializeField, HideInInspector] private float viewAngle = 360f;
    [SerializeField, HideInInspector] private float loseSightDelay = 5f;
    [SerializeField, HideInInspector] private LayerMask lineOfSightMask = ~0;
    [SerializeField, HideInInspector] private float eyeHeight = 1.5f;
    [SerializeField, HideInInspector] private float patrolRadius = 8f;
    [SerializeField, HideInInspector] private float patrolPointReachedDistance = 0.6f;
    [SerializeField, HideInInspector] private float idleAtPatrolPointTime = 1.2f;
    [SerializeField, HideInInspector] private float investigateDuration = 4f;
    [SerializeField, HideInInspector] private float investigateRadius = 3f;
    [SerializeField, HideInInspector] private LayerMask patrolWallMask = ~0;
    [SerializeField, HideInInspector] private Vector2 wallPatrolDistanceRange = new Vector2(28f, 55f);
    [SerializeField, HideInInspector, Range(0f, 1f)] private float patrolWallInfluence = 0.25f;
    [SerializeField, HideInInspector] private Vector2 patrolWanderYawRange = new Vector2(-10f, 10f);
    [SerializeField, HideInInspector] private Vector2 patrolSegmentYawRange = new Vector2(-12f, 12f);
    [SerializeField, HideInInspector] private Vector2 patrolReverseYawRange = new Vector2(145f, 215f);
    [SerializeField, HideInInspector, Range(0f, 1f)] private float patrolRandomReverseChance = 0.01f;
    [SerializeField, HideInInspector] private float wallFollowProbeDistance = 3.5f;
    [SerializeField, HideInInspector] private float wallFollowClearance = 1.05f;
    [SerializeField, HideInInspector] private float wallFollowStepDistance = 3.2f;
    [SerializeField, HideInInspector] private float wallFollowForwardBlockDistance = 1.35f;
    [SerializeField, HideInInspector] private float wallFollowDestinationRefreshDistance = 0.9f;
    [SerializeField, HideInInspector] private float enemySeparationRadius = 2.25f;
    [SerializeField, HideInInspector] private float enemySeparationStrength = 5.5f;
    [SerializeField, HideInInspector] private float enemySeparationMaxForce = 3.5f;
    [SerializeField, HideInInspector] private float navMeshSnapDistance = 8f;
    [SerializeField, HideInInspector] private float chaseNavMeshSampleDistance = 1.2f;
    [SerializeField, HideInInspector] private float chaseTargetNavMeshSampleDistance = 2f;
    [SerializeField, HideInInspector] private float chaseMaxNavPointJumpDistance = 4f;
    [SerializeField, HideInInspector] private float chaseTargetBackoffDistance = 3f;
    [SerializeField, HideInInspector] private float chaseTargetBackoffStep = 0.35f;
    [SerializeField, HideInInspector] private float chaseDirectFallbackDistance = 6f;
    [SerializeField, HideInInspector] private bool preferUnclaimedChaseTargets = true;
    [SerializeField, HideInInspector] private float targetClaimSwitchDistance = 1.5f;
    [SerializeField, HideInInspector] private float attackDamage = 100f;
    [SerializeField, HideInInspector] private float attackAnimationDuration = 0.8f;
    [SerializeField, HideInInspector] private float killAnimationDuration = 10f;
    [SerializeField, HideInInspector] private float killKnockbackUpwardForce = 3f;
    [SerializeField, HideInInspector] private LayerMask chaseDoorBreakMask = ~0;
    [SerializeField, HideInInspector] private float chaseDoorBreakDistance = 1.8f;
    [SerializeField, HideInInspector] private float chaseDoorBreakRadius = 0.65f;
    [SerializeField, HideInInspector] private float chaseDoorBreakHeight = 1.1f;
    [SerializeField, HideInInspector] private float chaseDoorBreakCooldown = 0.5f;

    [SerializeField] private EnemyPerceptionComponent perceptionComponent;
    [SerializeField] private EnemyNavigationComponent navigationComponent;
    [SerializeField] private EnemyCombatComponent combatComponent;

    private float idleTimer;
    private IEnemyState currentState;
    private IEnemyState idleState;
    private IEnemyState patrolState;
    private IEnemyState chaseState;
    private IEnemyState investigateState;
    private IEnemyState lureState;
    private Vector3 investigateCenter;
    private float investigateTimer;
    private Vector3 localLureDestination;
    private float localLureEndTime;
    private bool networkSpawned;
    private int renderedAttackSequence;
    private int renderedKillSequence;
    private GameObject owner;
    private bool servicesConfigured;

    [Networked] private Vector3 NetworkPosition { get; set; }
    [Networked] private Quaternion NetworkRotation { get; set; }
    [Networked] private EnemyStateId NetworkState { get; set; }
    [Networked] private EnemyAnimationState NetworkAnimationState { get; set; }
    [Networked] private TickTimer AttackAnimationTimer { get; set; }
    [Networked] private TickTimer KillAnimationTimer { get; set; }
    [Networked] private int NetworkAttackSequence { get; set; }
    [Networked] private int NetworkKillSequence { get; set; }
    [Networked] private Vector3 NetworkLureDestination { get; set; }
    [Networked] private TickTimer LureTimer { get; set; }

    public int RealtimeEnemyId { get; private set; }
    public GameObject Owner => owner != null ? owner : gameObject;

    internal bool HasActiveStateForServices => currentState != null;
    internal bool HasStateAuthorityForServices => Object == null || Object.HasStateAuthority;
    internal int TargetClaimOrderForServices => RealtimeEnemyId != 0 ? RealtimeEnemyId : GetInstanceID();
    internal float LoseRangeForServices => perceptionComponent != null ? perceptionComponent.LoseRange : loseRange;

    public bool IsChasingTarget(PlayerMovement player)
    {
        if (player == null)
            return false;

        bool isChasing = Object != null && networkSpawned
            ? NetworkState == EnemyStateId.Chase
            : currentState != null && currentState.Id == EnemyStateId.Chase;

        if (!isChasing)
            return false;

        if (target == null)
            return false;

        return target == player.transform
            || target.IsChildOf(player.transform)
            || target.GetComponentInParent<PlayerMovement>() == player;
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
    }

    public void SetRealtimeEnemyId(int enemyId)
    {
        RealtimeEnemyId = enemyId;
    }

    /// <summary>
    /// Forces this enemy to investigate a map-wide lure. Only state authority may issue
    /// the order; replicated state and pose keep every client visually in sync.
    /// </summary>
    public void BeginLure(Vector3 destination, float duration)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        duration = Mathf.Max(0.1f, duration);
        target = null;
        perceptionComponent?.ResetTargetTracking();
        navigationComponent?.ResetChaseTracking(null);

        if (Object != null && networkSpawned)
        {
            NetworkLureDestination = destination;
            LureTimer = TickTimer.CreateFromSeconds(Runner, duration);
        }
        else
        {
            localLureDestination = destination;
            localLureEndTime = Time.time + duration;
        }

        ChangeState(EnemyStateId.Lure);
    }

    public void ConfigureLegacyComponents()
    {
        servicesConfigured = false;
        EnsureServiceComponents();
    }

    private void Awake()
    {
        Initialize(gameObject);
        EnsureServiceComponents();
        navigationComponent.SnapToNavMesh();
        ResolveAnimationDriver();
        CreateStates();
    }

    private void OnEnable()
    {
        EnemyRuntimeRegistry.Register(this);
    }

    private void OnDisable()
    {
        EnemyRuntimeRegistry.Unregister(this);
    }

    private void Start()
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        navigationComponent.CaptureSpawnPosition();
        ChangeState(EnemyStateId.Patrol);
        navigationComponent.InitializeMoveDirection(target);
    }

    public override void Spawned()
    {
        networkSpawned = true;
        EnsureServiceComponents();
        navigationComponent.CaptureSpawnPosition();
        navigationComponent.ConfigureAgent();
        navigationComponent.SnapToNavMesh();
        navigationComponent.CaptureSpawnPosition();
        navigationComponent.ApplyNetworkAuthority(Object.HasStateAuthority);

        if (Object.HasStateAuthority)
        {
            ChangeState(EnemyStateId.Patrol);
            navigationComponent.InitializeMoveDirection(target);
            PublishNetworkPose();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        networkSpawned = false;
    }

    private void FixedUpdate()
    {
        if (Object != null)
            return;

        TickState(Time.fixedDeltaTime);
        PublishNetworkPose();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        TickState(Runner.DeltaTime);
        PublishNetworkPose();
    }

    public override void Render()
    {
        if (Object == null || Object.HasStateAuthority)
            return;

        navigationComponent.ApplyNetworkAuthority(false);
        transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 12f);

        if (visual != null)
            visual.rotation = Quaternion.Slerp(visual.rotation, NetworkRotation, Time.deltaTime * 12f);
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, NetworkRotation, Time.deltaTime * 12f);

        bool replayAttack = NetworkAnimationState == EnemyAnimationState.Attack
            && renderedAttackSequence != NetworkAttackSequence;
        bool replayKill = NetworkAnimationState == EnemyAnimationState.Kill
            && renderedKillSequence != NetworkKillSequence;
        ApplyAnimationState(NetworkAnimationState, replayAttack || replayKill);
        renderedAttackSequence = NetworkAttackSequence;
        renderedKillSequence = NetworkKillSequence;
    }

    private void OnCollisionEnter(Collision collision)
    {
        EnsureServiceComponents();
        combatComponent.HandleCollision(collision);
    }

    public void AnimationEvent_ApplyKillKnockback(float force)
    {
        EnsureServiceComponents();
        combatComponent.ApplyKillKnockback(force);
    }

    public bool CanCurrentlySee(PlayerMovement player)
    {
        EnsureServiceComponents();
        return perceptionComponent.CanCurrentlySee(player);
    }

    public bool TryKillPlayer(PlayerMovement player)
    {
        EnsureServiceComponents();
        return combatComponent.TryKillPlayer(player);
    }

    public static bool TryKillIfAnyEnemyCanSee(PlayerMovement player)
    {
        if (player == null)
            return false;

        foreach (CSHEnemy enemy in EnemyRuntimeRegistry.Enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled)
                continue;

            if (enemy.Object != null && !enemy.Object.HasStateAuthority)
                continue;

            if (!enemy.CanCurrentlySee(player))
                continue;

            return enemy.TryKillPlayer(player);
        }

        return false;
    }

    public static bool HasEnemyDetectedPlayer(PlayerMovement player)
    {
        if (player == null)
            return false;

        foreach (CSHEnemy enemy in EnemyRuntimeRegistry.Enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled)
                continue;

            if (enemy.Object != null && !enemy.Object.HasStateAuthority)
                continue;

            if (enemy.CanCurrentlySee(player))
                return true;
        }

        return false;
    }

    internal bool IsChasingTransformForServices(Transform candidate)
    {
        if (candidate == null)
            return false;

        bool isChasing = Object != null && networkSpawned
            ? NetworkState == EnemyStateId.Chase
            : currentState != null && currentState.Id == EnemyStateId.Chase;

        if (!isChasing || target == null)
            return false;

        return target == candidate || target.IsChildOf(candidate) || candidate.IsChildOf(target);
    }

    internal bool CanSeeForServices(Transform candidate, float maxDistance)
    {
        EnsureServiceComponents();
        return perceptionComponent.CanSee(candidate, maxDistance);
    }

    internal void RefreshChasePointFromPerception()
    {
        navigationComponent?.RefreshChasePoint(target);
    }

    internal void StopNavigationFromCombat()
    {
        navigationComponent?.StopMoving();
    }

    internal float GetAnimationClipLengthForServices(EnemyAnimationState state, float fallback)
    {
        if (animationDriver == null)
            ResolveAnimationDriver();

        return animationDriver != null ? animationDriver.GetClipLength(state, fallback) : fallback;
    }

    internal void PublishNetworkRotationFromServices(Quaternion rotation)
    {
        if (Object != null && networkSpawned)
            NetworkRotation = rotation;
    }

    internal void BeginAttackAnimationFromCombat(float duration)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        if (Object != null && networkSpawned)
        {
            AttackAnimationTimer = TickTimer.CreateFromSeconds(Runner, duration);
            NetworkAttackSequence++;
            NetworkAnimationState = EnemyAnimationState.Attack;
            renderedAttackSequence = NetworkAttackSequence;
        }

        ApplyAnimationState(EnemyAnimationState.Attack, true);
    }

    internal void BeginKillAnimationFromCombat(float duration)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        if (Object != null && networkSpawned)
        {
            KillAnimationTimer = TickTimer.CreateFromSeconds(Runner, duration);
            AttackAnimationTimer = TickTimer.None;
            NetworkKillSequence++;
            NetworkAnimationState = EnemyAnimationState.Kill;
            renderedKillSequence = NetworkKillSequence;
        }

        ApplyAnimationState(EnemyAnimationState.Kill, true);
    }

    private void EnsureServiceComponents()
    {
        bool referencesChanged = false;

        if (perceptionComponent == null)
        {
            perceptionComponent = GetComponentInChildren<EnemyPerceptionComponent>(true);
            referencesChanged = true;
        }
        if (perceptionComponent == null)
            perceptionComponent = gameObject.AddComponent<EnemyPerceptionComponent>();

        if (navigationComponent == null)
        {
            navigationComponent = GetComponentInChildren<EnemyNavigationComponent>(true);
            referencesChanged = true;
        }
        if (navigationComponent == null)
            navigationComponent = gameObject.AddComponent<EnemyNavigationComponent>();

        if (combatComponent == null)
        {
            combatComponent = GetComponentInChildren<EnemyCombatComponent>(true);
            referencesChanged = true;
        }
        if (combatComponent == null)
            combatComponent = gameObject.AddComponent<EnemyCombatComponent>();

        if (servicesConfigured && !referencesChanged)
            return;

        perceptionComponent.ConfigureLegacy(this, visual, new EnemyPerceptionComponent.LegacySettings
        {
            DetectionRange = detectionRange,
            ProximityDetectionRange = proximityDetectionRange,
            LoseRange = loseRange,
            ViewAngle = viewAngle,
            LoseSightDelay = loseSightDelay,
            LineOfSightMask = lineOfSightMask,
            EyeHeight = eyeHeight,
            PreferUnclaimedChaseTargets = preferUnclaimedChaseTargets,
            TargetClaimSwitchDistance = targetClaimSwitchDistance
        });

        navigationComponent.ConfigureLegacy(this, visual, new EnemyNavigationComponent.LegacySettings
        {
            MoveSpeed = moveSpeed,
            RotationSpeed = rotationSpeed,
            PatrolSpeed = patrolSpeed,
            IdleAtPatrolPointTime = idleAtPatrolPointTime,
            InvestigateDuration = investigateDuration,
            InvestigateRadius = investigateRadius,
            PatrolRadius = patrolRadius,
            PatrolPointReachedDistance = patrolPointReachedDistance,
            PatrolWallMask = patrolWallMask,
            WallPatrolDistanceRange = wallPatrolDistanceRange,
            PatrolWallInfluence = patrolWallInfluence,
            PatrolWanderYawRange = patrolWanderYawRange,
            PatrolSegmentYawRange = patrolSegmentYawRange,
            PatrolReverseYawRange = patrolReverseYawRange,
            PatrolRandomReverseChance = patrolRandomReverseChance,
            WallFollowProbeDistance = wallFollowProbeDistance,
            WallFollowClearance = wallFollowClearance,
            WallFollowStepDistance = wallFollowStepDistance,
            WallFollowForwardBlockDistance = wallFollowForwardBlockDistance,
            WallFollowDestinationRefreshDistance = wallFollowDestinationRefreshDistance,
            EnemySeparationRadius = enemySeparationRadius,
            EnemySeparationStrength = enemySeparationStrength,
            EnemySeparationMaxForce = enemySeparationMaxForce,
            NavMeshSnapDistance = navMeshSnapDistance,
            ChaseNavMeshSampleDistance = chaseNavMeshSampleDistance,
            ChaseTargetNavMeshSampleDistance = chaseTargetNavMeshSampleDistance,
            ChaseMaxNavPointJumpDistance = chaseMaxNavPointJumpDistance,
            ChaseTargetBackoffDistance = chaseTargetBackoffDistance,
            ChaseTargetBackoffStep = chaseTargetBackoffStep,
            ChaseDirectFallbackDistance = chaseDirectFallbackDistance,
            EyeHeight = eyeHeight,
            ChaseDoorBreakMask = chaseDoorBreakMask,
            ChaseDoorBreakDistance = chaseDoorBreakDistance,
            ChaseDoorBreakRadius = chaseDoorBreakRadius,
            ChaseDoorBreakHeight = chaseDoorBreakHeight,
            ChaseDoorBreakCooldown = chaseDoorBreakCooldown
        });

        combatComponent.ConfigureLegacy(this, visual, perceptionComponent, new EnemyCombatComponent.LegacySettings
        {
            DeathUi = ui,
            AttackDamage = attackDamage,
            AttackAnimationDuration = attackAnimationDuration,
            KillAnimationDuration = killAnimationDuration,
            KillKnockbackUpwardForce = killKnockbackUpwardForce,
            ProximityDetectionRange = proximityDetectionRange
        });

        servicesConfigured = true;
    }

    private void TickState(float deltaTime)
    {
        if (IsKillAnimationPlaying())
        {
            navigationComponent.StopMoving();
            return;
        }

        ClearExpiredKillAnimation();

        if (IsLureActive())
        {
            if (currentState == null || currentState.Id != EnemyStateId.Lure)
                ChangeState(EnemyStateId.Lure);
        }
        else if (currentState != null && currentState.Id == EnemyStateId.Lure)
        {
            ChangeState(EnemyStateId.Patrol);
        }

        if (currentState == null)
            ChangeState(EnemyStateId.Patrol);

        currentState?.Tick(deltaTime);
    }

    private void PublishNetworkPose()
    {
        EnemyAnimationState animationState = ResolveAnimationState();
        if (Object != null && networkSpawned)
        {
            NetworkPosition = transform.position;
            NetworkRotation = visual != null ? visual.rotation : transform.rotation;
            NetworkState = currentState != null ? currentState.Id : EnemyStateId.Patrol;
            NetworkAnimationState = animationState;
        }

        ApplyAnimationState(animationState);
    }

    private void ChangeState(EnemyStateId stateId)
    {
        if (currentState != null && currentState.Id == stateId)
            return;

        currentState?.Exit();
        currentState = GetState(stateId);
        currentState?.Enter();

        EnemyAnimationState animationState = ResolveAnimationState();
        if (Object != null && networkSpawned)
        {
            NetworkState = stateId;
            NetworkAnimationState = animationState;
        }

        ApplyAnimationState(animationState);
    }

    private IEnemyState GetState(EnemyStateId stateId)
    {
        return stateId switch
        {
            EnemyStateId.Idle => idleState,
            EnemyStateId.Chase => chaseState,
            EnemyStateId.Investigate => investigateState,
            EnemyStateId.Lure => lureState,
            _ => patrolState
        };
    }

    private void CreateStates()
    {
        idleState = new IdleState(this);
        patrolState = new PatrolState(this);
        chaseState = new ChaseState(this);
        investigateState = new InvestigateState(this);
        lureState = new LureState(this);
    }

    private void AcquireTarget(Transform newTarget)
    {
        target = newTarget;
        perceptionComponent.ResetTargetTracking();
        navigationComponent.ResetChaseTracking(newTarget);
    }

    private void BeginInvestigating(Vector3 center)
    {
        investigateCenter = center;
        investigateTimer = Mathf.Max(0.1f, navigationComponent.InvestigateDuration);
        ChangeState(EnemyStateId.Investigate);
    }

    private void ResolveAnimationDriver()
    {
        if (animationDriver == null)
        {
            animationDriver = GetComponent<EnemyAnimationDriver>();
            if (animationDriver == null)
                animationDriver = GetComponentInChildren<EnemyAnimationDriver>(true);
        }

        if (animationDriver != null)
            animationDriver.Initialize();
    }

    private EnemyAnimationState ResolveAnimationState()
    {
        if (IsKillAnimationPlaying())
            return EnemyAnimationState.Kill;

        if (IsAttackAnimationPlaying())
            return EnemyAnimationState.Attack;

        if (currentState == null)
            return EnemyAnimationState.Idle;

        if (currentState.Id == EnemyStateId.Chase)
            return EnemyAnimationState.Chase;

        if ((currentState.Id == EnemyStateId.Patrol || currentState.Id == EnemyStateId.Investigate || currentState.Id == EnemyStateId.Lure)
            && navigationComponent.IsMovingForAnimation())
            return EnemyAnimationState.Patrol;

        return EnemyAnimationState.Idle;
    }

    private bool IsAttackAnimationPlaying()
    {
        return Object != null
            && networkSpawned
            && AttackAnimationTimer.IsRunning
            && !AttackAnimationTimer.Expired(Runner);
    }

    private bool IsKillAnimationPlaying()
    {
        if (Object == null)
            return combatComponent != null && combatComponent.IsLocalKillAnimationPlaying;

        return networkSpawned
            && KillAnimationTimer.IsRunning
            && !KillAnimationTimer.Expired(Runner);
    }

    private bool IsLureActive()
    {
        if (Object != null && networkSpawned)
            return LureTimer.IsRunning && !LureTimer.Expired(Runner);

        return localLureEndTime > Time.time;
    }

    private Vector3 GetLureDestination()
    {
        return Object != null && networkSpawned ? NetworkLureDestination : localLureDestination;
    }

    private void ClearExpiredKillAnimation()
    {
        if (Object == null || !Object.HasStateAuthority || !networkSpawned)
            return;

        if (!KillAnimationTimer.IsRunning || !KillAnimationTimer.Expired(Runner))
            return;

        KillAnimationTimer = TickTimer.None;
        combatComponent.ClearPendingKill();
    }

    private void ApplyAnimationState(EnemyAnimationState stateId, bool force = false)
    {
        if (animationDriver == null)
            ResolveAnimationDriver();

        animationDriver?.Play(stateId, force);
    }

    private sealed class IdleState : IEnemyState
    {
        private readonly CSHEnemy enemy;

        public EnemyStateId Id => EnemyStateId.Idle;

        public IdleState(CSHEnemy enemy)
        {
            this.enemy = enemy;
        }

        public void Enter()
        {
            enemy.navigationComponent.StopMoving();
            enemy.idleTimer = enemy.navigationComponent.IdleAtPatrolPointTime;
        }

        public void Tick(float deltaTime)
        {
            if (enemy.perceptionComponent.TryFindVisibleTarget(out Transform visibleTarget))
            {
                enemy.AcquireTarget(visibleTarget);
                enemy.ChangeState(EnemyStateId.Chase);
                return;
            }

            enemy.idleTimer -= deltaTime;
            if (enemy.idleTimer <= 0f)
                enemy.ChangeState(EnemyStateId.Patrol);
        }

        public void Exit()
        {
        }
    }

    private sealed class PatrolState : IEnemyState
    {
        private readonly CSHEnemy enemy;
        private Vector3 patrolDestination;
        private Vector3 travelDirection;
        private Vector3 lastPosition;
        private float remainingSegmentDistance;
        private int wallSide = 1;

        public EnemyStateId Id => EnemyStateId.Patrol;

        public PatrolState(CSHEnemy enemy)
        {
            this.enemy = enemy;
        }

        public void Enter()
        {
            wallSide = Random.value < 0.5f ? -1 : 1;
            travelDirection = enemy.navigationComponent.GetInitialWallPatrolDirection();
            remainingSegmentDistance = enemy.navigationComponent.GetRandomWallPatrolDistance();
            lastPosition = enemy.transform.position;
            enemy.navigationComponent.RecordPatrolCoverage();
            RefreshDestination();
            enemy.navigationComponent.MoveTo(patrolDestination, enemy.navigationComponent.PatrolSpeed);
        }

        public void Tick(float deltaTime)
        {
            if (enemy.perceptionComponent.TryFindVisibleTarget(out Transform visibleTarget))
            {
                enemy.AcquireTarget(visibleTarget);
                enemy.ChangeState(EnemyStateId.Chase);
                return;
            }

            Vector3 currentPosition = enemy.transform.position;
            Vector3 moved = currentPosition - lastPosition;
            moved.y = 0f;
            remainingSegmentDistance -= moved.magnitude;
            lastPosition = currentPosition;
            enemy.navigationComponent.RecordPatrolCoverage();

            if (remainingSegmentDistance <= 0f)
                StartNextSegment();

            if (enemy.navigationComponent.ShouldRefreshWallPatrolDestination(patrolDestination))
                RefreshDestination();

            enemy.navigationComponent.MoveTo(patrolDestination, enemy.navigationComponent.PatrolSpeed);
            enemy.navigationComponent.RotateTowardMovement();
        }

        public void Exit()
        {
        }

        private void StartNextSegment()
        {
            if (enemy.navigationComponent.ShouldRandomlyReversePatrol())
                travelDirection = enemy.navigationComponent.GetRandomReverseDirection(travelDirection);
            else
                travelDirection = enemy.navigationComponent.GetRandomForwardSegmentDirection(travelDirection);

            remainingSegmentDistance = enemy.navigationComponent.GetRandomWallPatrolDistance();
            RefreshDestination();
        }

        private void RefreshDestination()
        {
            if (enemy.navigationComponent.TryGetCoordinatedPatrolPoint(out patrolDestination))
                return;

            for (int i = 0; i < 2; i++)
            {
                if (enemy.navigationComponent.TryGetWallFollowDestination(
                    ref travelDirection,
                    ref wallSide,
                    out patrolDestination,
                    out bool shouldReverse))
                    return;

                if (shouldReverse)
                {
                    travelDirection = enemy.navigationComponent.GetRandomReverseDirection(travelDirection);
                    remainingSegmentDistance = enemy.navigationComponent.GetRandomWallPatrolDistance();
                }
            }

            patrolDestination = enemy.navigationComponent.GetRandomPatrolPoint();
        }
    }

    private sealed class ChaseState : IEnemyState
    {
        private readonly CSHEnemy enemy;

        public EnemyStateId Id => EnemyStateId.Chase;

        public ChaseState(CSHEnemy enemy)
        {
            this.enemy = enemy;
        }

        public void Enter()
        {
            enemy.perceptionComponent.ResetTargetTracking();
        }

        public void Tick(float deltaTime)
        {
            if (enemy.combatComponent.TryKillCompromisedHiddenTarget(enemy.target))
                return;

            if (enemy.perceptionComponent.ShouldLoseTarget(enemy.target, deltaTime))
            {
                bool hiddenTarget = EnemyPerceptionComponent.IsHiddenTarget(enemy.target);
                Vector3 lastKnownPosition = enemy.navigationComponent.GetLastKnownTargetNavigationPosition(enemy.target);
                enemy.target = null;
                if (hiddenTarget)
                    enemy.BeginInvestigating(lastKnownPosition);
                else
                    enemy.ChangeState(EnemyStateId.Patrol);
                return;
            }

            Vector3 destination = enemy.navigationComponent.ResolveChaseDestination(
                enemy.target,
                out bool useDirectChaseMovement);
            if (useDirectChaseMovement)
                enemy.navigationComponent.MoveDirectlyTo(destination, enemy.navigationComponent.MoveSpeed, deltaTime);
            else
                enemy.navigationComponent.MoveTo(destination, enemy.navigationComponent.MoveSpeed);

            enemy.navigationComponent.TryBreakChaseDoor(enemy.target);
            enemy.navigationComponent.RotateTowardMovement();
        }

        public void Exit()
        {
            enemy.navigationComponent.StopMoving();
        }
    }

    private sealed class InvestigateState : IEnemyState
    {
        private readonly CSHEnemy enemy;
        private Vector3 destination;

        public EnemyStateId Id => EnemyStateId.Investigate;

        public InvestigateState(CSHEnemy enemy)
        {
            this.enemy = enemy;
        }

        public void Enter()
        {
            enemy.investigateTimer = Mathf.Max(0.1f, enemy.navigationComponent.InvestigateDuration);
            RefreshDestination();
        }

        public void Tick(float deltaTime)
        {
            if (enemy.perceptionComponent.TryFindVisibleTarget(out Transform visibleTarget))
            {
                enemy.AcquireTarget(visibleTarget);
                enemy.ChangeState(EnemyStateId.Chase);
                return;
            }

            enemy.investigateTimer -= deltaTime;
            if (enemy.investigateTimer <= 0f)
            {
                enemy.ChangeState(EnemyStateId.Patrol);
                return;
            }

            if (enemy.navigationComponent.ShouldRefreshWallPatrolDestination(destination))
                RefreshDestination();

            enemy.navigationComponent.MoveTo(destination, enemy.navigationComponent.PatrolSpeed);
            enemy.navigationComponent.RotateTowardMovement();
        }

        public void Exit()
        {
        }

        private void RefreshDestination()
        {
            destination = enemy.navigationComponent.GetRandomInvestigatePoint(
                enemy.investigateCenter,
                enemy.navigationComponent.InvestigateRadius);
        }
    }

    private sealed class LureState : IEnemyState
    {
        private readonly CSHEnemy enemy;

        public EnemyStateId Id => EnemyStateId.Lure;

        public LureState(CSHEnemy enemy)
        {
            this.enemy = enemy;
        }

        public void Enter()
        {
            enemy.target = null;
            enemy.perceptionComponent.ResetTargetTracking();
            enemy.navigationComponent.ResetChaseTracking(null);
        }

        public void Tick(float deltaTime)
        {
            if (!enemy.IsLureActive())
            {
                enemy.ChangeState(EnemyStateId.Patrol);
                return;
            }

            enemy.navigationComponent.MoveTo(enemy.GetLureDestination(), enemy.navigationComponent.MoveSpeed);
            enemy.navigationComponent.RotateTowardMovement();
        }

        public void Exit()
        {
        }
    }
}
