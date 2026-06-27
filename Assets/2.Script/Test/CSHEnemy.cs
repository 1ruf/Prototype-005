using System.Collections;
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
        Chase = 2
    }

    private interface IEnemyState
    {
        EnemyStateId Id { get; }
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }

    [SerializeField] private Transform target;
    [SerializeField] private Transform visual;
    [SerializeField] private EnemyAnimationDriver animationDriver;
    [SerializeField] private GameObject ui;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float patrolSpeed = 2.2f;
    [SerializeField] private float rotationSpeed = 3f;
    [SerializeField] private float detectionRange = 12f;
    [SerializeField] private float proximityDetectionRange = 4f;
    [SerializeField] private float loseRange = 18f;
    [SerializeField] private float viewAngle = 360f;
    [SerializeField] private float loseSightDelay = 5f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;
    [SerializeField] private float eyeHeight = 1.5f;
    [SerializeField] private float patrolRadius = 8f;
    [SerializeField] private float patrolPointReachedDistance = 0.6f;
    [SerializeField] private float idleAtPatrolPointTime = 1.2f;
    [SerializeField] private LayerMask patrolWallMask = ~0;
    [SerializeField] private Vector2 wallPatrolDistanceRange = new Vector2(8f, 18f);
    [SerializeField, Range(0f, 1f)] private float patrolWallInfluence = 0.25f;
    [SerializeField] private Vector2 patrolWanderYawRange = new Vector2(-18f, 18f);
    [SerializeField] private Vector2 patrolReverseYawRange = new Vector2(145f, 215f);
    [SerializeField] private float wallFollowProbeDistance = 3.5f;
    [SerializeField] private float wallFollowClearance = 1.05f;
    [SerializeField] private float wallFollowStepDistance = 3.2f;
    [SerializeField] private float wallFollowForwardBlockDistance = 1.35f;
    [SerializeField] private float wallFollowDestinationRefreshDistance = 0.9f;
    [SerializeField] private float enemySeparationRadius = 2.25f;
    [SerializeField] private float enemySeparationStrength = 5.5f;
    [SerializeField] private float enemySeparationMaxForce = 3.5f;
    [SerializeField] private float navMeshSnapDistance = 8f;
    [SerializeField] private float attackDamage = 100f;
    [SerializeField] private float attackAnimationDuration = 0.8f;
    [SerializeField] private float killAnimationDuration = 10f;
    [SerializeField] private float killKnockbackForce = 12f;
    [SerializeField] private float killKnockbackUpwardForce = 3f;

    private Rigidbody rb;
    private NavMeshAgent agent;
    private Vector3 moveDirection;
    private bool hasKilledLocalPlayer;
    private Vector3 spawnPosition;
    private float lostSightTimer;
    private float idleTimer;
    private IEnemyState currentState;
    private IEnemyState idleState;
    private IEnemyState patrolState;
    private IEnemyState chaseState;
    private bool networkSpawned;
    private int renderedAttackSequence;
    private int renderedKillSequence;
    private IKnockbackable pendingKillKnockback;
    private RagdollEntityComponent pendingKillRagdoll;
    private Vector3 pendingKillKnockbackDirection;
    private float localKillAnimationEndTime;
    private GameObject owner;

    [Networked] private Vector3 NetworkPosition { get; set; }
    [Networked] private Quaternion NetworkRotation { get; set; }
    [Networked] private EnemyStateId NetworkState { get; set; }
    [Networked] private EnemyAnimationState NetworkAnimationState { get; set; }
    [Networked] private TickTimer AttackAnimationTimer { get; set; }
    [Networked] private TickTimer KillAnimationTimer { get; set; }
    [Networked] private int NetworkAttackSequence { get; set; }
    [Networked] private int NetworkKillSequence { get; set; }

    public int RealtimeEnemyId { get; private set; }
    public GameObject Owner => owner != null ? owner : gameObject;

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
    }

    public void SetRealtimeEnemyId(int enemyId)
    {
        RealtimeEnemyId = enemyId;
    }

    private void Awake()
    {
        Initialize(gameObject);

        if (ui != null)
            ui.SetActive(false);

        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = gameObject.AddComponent<NavMeshAgent>();

        ConfigureAgent();
        SnapToNavMesh();
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

        spawnPosition = transform.position;
        ChangeState(EnemyStateId.Patrol);
        InitializeMoveDirection();
    }

    public override void Spawned()
    {
        networkSpawned = true;
        spawnPosition = transform.position;
        ConfigureAgent();
        SnapToNavMesh();
        spawnPosition = transform.position;
        rb.isKinematic = !Object.HasStateAuthority;

        if (Object.HasStateAuthority)
        {
            ChangeState(EnemyStateId.Patrol);
            InitializeMoveDirection();
            PublishNetworkPose();
            return;
        }

        if (agent != null)
            agent.enabled = false;
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

        if (agent != null && agent.enabled)
            agent.enabled = false;

        transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 12f);

        if (visual != null)
            visual.rotation = Quaternion.Slerp(visual.rotation, NetworkRotation, Time.deltaTime * 12f);
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, NetworkRotation, Time.deltaTime * 12f);

        bool replayAttack = NetworkAnimationState == EnemyAnimationState.Attack && renderedAttackSequence != NetworkAttackSequence;
        bool replayKill = NetworkAnimationState == EnemyAnimationState.Kill && renderedKillSequence != NetworkKillSequence;
        ApplyAnimationState(NetworkAnimationState, replayAttack || replayKill);
        renderedAttackSequence = NetworkAttackSequence;
        renderedKillSequence = NetworkKillSequence;
    }

    private void TickState(float deltaTime)
    {
        if (IsKillAnimationPlaying())
        {
            StopMoving();
            return;
        }

        ClearExpiredKillAnimation();

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
            _ => patrolState
        };
    }

    private void CreateStates()
    {
        idleState = new IdleState(this);
        patrolState = new PatrolState(this);
        chaseState = new ChaseState(this);
    }

    private void MoveTo(Vector3 destination, float speed)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            destination = ProjectDestinationToNavMesh(destination);
            agent.nextPosition = transform.position;
            agent.speed = speed;
            agent.isStopped = false;
            agent.SetDestination(destination);

            Vector3 desiredVelocity = agent.desiredVelocity;
            if (desiredVelocity.sqrMagnitude <= 0.001f && agent.hasPath && agent.path.corners.Length > 1)
                desiredVelocity = (agent.path.corners[1] - transform.position).normalized * speed;

            desiredVelocity = ApplyEnemySeparation(desiredVelocity, speed);
            desiredVelocity.y = rb.linearVelocity.y;
            rb.linearVelocity = desiredVelocity.sqrMagnitude > 0.001f ? desiredVelocity : rb.linearVelocity;
            return;
        }

        MoveDirectlyTo(destination, speed, Time.deltaTime);
    }

    private void StopMoving()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.nextPosition = transform.position;
        }

        rb.linearVelocity = Vector3.zero;
    }

    private bool HasReachedDestination()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            return !agent.pathPending && agent.remainingDistance <= patrolPointReachedDistance;

        return rb.linearVelocity.sqrMagnitude <= 0.01f;
    }

    private void MoveDirectlyTo(Vector3 destination, float speed, float deltaTime)
    {
        Vector3 targetDirection = destination - transform.position;
        targetDirection.y = 0f;

        if (targetDirection.sqrMagnitude <= 0.0001f)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        targetDirection.Normalize();
        moveDirection = Vector3.Lerp(moveDirection, targetDirection, rotationSpeed * deltaTime).normalized;
        Vector3 desiredVelocity = ApplyEnemySeparation(moveDirection * speed, speed);
        rb.linearVelocity = desiredVelocity;
        RotateToward(moveDirection);
    }

    private Vector3 ApplyEnemySeparation(Vector3 desiredVelocity, float speed)
    {
        Vector3 separation = GetEnemySeparationVelocity();
        if (separation.sqrMagnitude <= 0.0001f)
            return desiredVelocity;

        Vector3 flatDesired = desiredVelocity;
        flatDesired.y = 0f;

        Vector3 adjustedVelocity = flatDesired + separation;
        if (adjustedVelocity.sqrMagnitude > speed * speed)
            adjustedVelocity = adjustedVelocity.normalized * speed;

        adjustedVelocity.y = desiredVelocity.y;
        return adjustedVelocity;
    }

    private Vector3 GetEnemySeparationVelocity()
    {
        if (enemySeparationRadius <= 0f || enemySeparationStrength <= 0f)
            return Vector3.zero;

        Vector3 separation = Vector3.zero;
        Vector3 position = transform.position;
        float radiusSqr = enemySeparationRadius * enemySeparationRadius;

        foreach (CSHEnemy other in EnemyRuntimeRegistry.Enemies)
        {
            if (other == null || other == this)
                continue;

            if (other.currentState == null)
                continue;

            Vector3 away = position - other.transform.position;
            away.y = 0f;
            float distanceSqr = away.sqrMagnitude;
            if (distanceSqr > radiusSqr)
                continue;

            if (distanceSqr <= 0.0001f)
            {
                away = visual != null ? visual.right : transform.right;
                away.y = 0f;
                distanceSqr = away.sqrMagnitude;
            }

            if (distanceSqr <= 0.0001f)
                continue;

            float distance = Mathf.Sqrt(distanceSqr);
            float weight = 1f - Mathf.Clamp01(distance / enemySeparationRadius);
            separation += away / distance * (weight * enemySeparationStrength);
        }

        float maxForce = Mathf.Max(0f, enemySeparationMaxForce);
        if (maxForce > 0f && separation.sqrMagnitude > maxForce * maxForce)
            separation = separation.normalized * maxForce;

        return separation;
    }

    private void RotateTowardMovement()
    {
        Vector3 velocity = agent != null && agent.enabled ? agent.velocity : rb.linearVelocity;
        velocity.y = 0f;

        if (velocity.sqrMagnitude <= 0.001f)
            return;

        RotateToward(velocity.normalized);
    }

    private void RotateToward(Vector3 direction)
    {
        if (direction == Vector3.zero)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        if (visual != null)
            visual.rotation = Quaternion.Slerp(visual.rotation, targetRotation, Time.deltaTime * 12f);
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 12f);
    }

    private bool TryFindVisibleTarget(out Transform foundTarget)
    {
        float closestDistance = float.PositiveInfinity;
        foundTarget = null;

        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player == null || IsDeadTarget(player.transform))
                continue;

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance > detectionRange || distance >= closestDistance)
                continue;

            if (distance > proximityDetectionRange && !CanSee(player.transform, detectionRange))
                continue;

            closestDistance = distance;
            foundTarget = player.transform;
        }

        return foundTarget != null;
    }

    private bool CanSee(Transform candidate, float maxDistance)
    {
        if (candidate == null)
            return false;

        Vector3 toTarget = candidate.position - transform.position;
        float distance = toTarget.magnitude;
        if (distance > maxDistance)
            return false;

        Vector3 flatDirection = toTarget;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude <= 0.0001f)
            return true;

        Vector3 forward = visual != null ? visual.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = transform.forward;

        if (viewAngle < 359f && Vector3.Angle(forward.normalized, flatDirection.normalized) > viewAngle * 0.5f)
            return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = candidate.position + Vector3.up * eyeHeight;
        Vector3 rayDirection = targetPoint - origin;

        RaycastHit[] hits = Physics.RaycastAll(origin, rayDirection.normalized, distance, lineOfSightMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null)
                continue;

            if (hit.transform == candidate || hit.transform.IsChildOf(candidate))
                return true;

            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            return false;
        }

        return true;
    }

    private bool ShouldLoseTarget(float deltaTime)
    {
        if (target == null || IsDeadTarget(target))
            return true;

        Vector3 offset = target.position - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        if (distance > loseRange)
            return true;

        if (distance <= proximityDetectionRange)
        {
            lostSightTimer = 0f;
            return false;
        }

        if (CanSee(target, loseRange))
        {
            lostSightTimer = 0f;
            return false;
        }

        lostSightTimer += deltaTime;
        return lostSightTimer >= loseSightDelay;
    }

    private Vector3 GetRandomPatrolPoint()
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 random = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = spawnPosition + new Vector3(random.x, 0f, random.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;
        }

        return spawnPosition;
    }

    private Vector3 GetInitialWallPatrolDirection()
    {
        Vector3 direction = visual != null ? visual.forward : transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = moveDirection;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = transform.forward;

        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private float GetRandomWallPatrolDistance()
    {
        float min = Mathf.Max(1f, Mathf.Min(wallPatrolDistanceRange.x, wallPatrolDistanceRange.y));
        float max = Mathf.Max(min, Mathf.Max(wallPatrolDistanceRange.x, wallPatrolDistanceRange.y));
        return Random.Range(min, max);
    }

    private bool ShouldRefreshWallPatrolDestination(Vector3 destination)
    {
        Vector3 offset = destination - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude <= wallFollowDestinationRefreshDistance * wallFollowDestinationRefreshDistance)
            return true;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            return !agent.pathPending && (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid);

        return false;
    }

    private bool TryGetWallFollowDestination(ref Vector3 travelDirection, ref int wallSide, out Vector3 destination, out bool shouldReverse)
    {
        shouldReverse = false;
        destination = transform.position;
        travelDirection.y = 0f;

        if (travelDirection.sqrMagnitude <= 0.0001f)
            travelDirection = GetInitialWallPatrolDirection();

        travelDirection.Normalize();

        if (IsWallPatrolForwardBlocked(travelDirection))
        {
            shouldReverse = true;
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 sideDirection = GetSideDirection(travelDirection, wallSide);
        bool foundWall = Physics.Raycast(origin, sideDirection, out RaycastHit wallHit, wallFollowProbeDistance, patrolWallMask, QueryTriggerInteraction.Ignore);

        if (!foundWall && TryFindNearbyWall(origin, travelDirection, out int detectedSide))
        {
            wallSide = detectedSide;
            sideDirection = GetSideDirection(travelDirection, wallSide);
            foundWall = Physics.Raycast(origin, sideDirection, out wallHit, wallFollowProbeDistance, patrolWallMask, QueryTriggerInteraction.Ignore);
        }

        Vector3 wanderDirection = Quaternion.Euler(0f, Random.Range(patrolWanderYawRange.x, patrolWanderYawRange.y), 0f) * travelDirection;
        Vector3 desiredDirection = wanderDirection.normalized;
        Vector3 clearanceCorrection = Vector3.zero;
        if (foundWall)
        {
            Vector3 wallForward = Vector3.Cross(Vector3.up, wallHit.normal).normalized;
            if (Vector3.Dot(wallForward, desiredDirection) < 0f)
                wallForward = -wallForward;

            desiredDirection = Vector3.Slerp(desiredDirection, wallForward, patrolWallInfluence).normalized;
            clearanceCorrection = sideDirection * ((wallHit.distance - wallFollowClearance) * Mathf.Clamp01(patrolWallInfluence));
        }

        Vector3 candidate = transform.position + desiredDirection * wallFollowStepDistance + clearanceCorrection;
        if (TryProjectWallPatrolPoint(candidate, out destination))
        {
            travelDirection = desiredDirection;
            return true;
        }

        candidate = transform.position + travelDirection * wallFollowStepDistance;
        if (TryProjectWallPatrolPoint(candidate, out destination))
            return true;

        shouldReverse = true;
        return false;
    }

    private Vector3 GetRandomReverseDirection(Vector3 currentDirection)
    {
        currentDirection.y = 0f;
        if (currentDirection.sqrMagnitude <= 0.0001f)
            currentDirection = GetInitialWallPatrolDirection();

        float yaw = Random.Range(patrolReverseYawRange.x, patrolReverseYawRange.y);
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * currentDirection.normalized;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : -currentDirection.normalized;
    }

    private bool TryFindNearbyWall(Vector3 origin, Vector3 travelDirection, out int wallSide)
    {
        wallSide = 1;
        Vector3 right = GetSideDirection(travelDirection, 1);
        Vector3 left = GetSideDirection(travelDirection, -1);
        bool hasRight = Physics.Raycast(origin, right, out RaycastHit rightHit, wallFollowProbeDistance, patrolWallMask, QueryTriggerInteraction.Ignore);
        bool hasLeft = Physics.Raycast(origin, left, out RaycastHit leftHit, wallFollowProbeDistance, patrolWallMask, QueryTriggerInteraction.Ignore);

        if (!hasRight && !hasLeft)
            return false;

        wallSide = hasLeft && (!hasRight || leftHit.distance < rightHit.distance) ? -1 : 1;
        return true;
    }

    private bool IsWallPatrolForwardBlocked(Vector3 travelDirection)
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        return Physics.Raycast(origin, travelDirection, wallFollowForwardBlockDistance, patrolWallMask, QueryTriggerInteraction.Ignore);
    }

    private bool TryProjectWallPatrolPoint(Vector3 candidate, out Vector3 projectedPoint)
    {
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.75f, NavMesh.AllAreas))
        {
            projectedPoint = hit.position;
            return true;
        }

        projectedPoint = candidate;
        return agent == null || !agent.enabled || !agent.isOnNavMesh;
    }

    private static Vector3 GetSideDirection(Vector3 forward, int side)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward.normalized).normalized;
        return side >= 0 ? right : -right;
    }

    private void ConfigureAgent()
    {
        if (agent == null)
            return;

        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.speed = patrolSpeed;
        agent.angularSpeed = 720f;
        agent.acceleration = 16f;
        agent.stoppingDistance = 0.1f;
        agent.radius = Mathf.Max(agent.radius, 0.65f);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(35, 66);
    }

    private void SnapToNavMesh()
    {
        if (agent == null || !agent.enabled)
            return;

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
            return;

        transform.position = hit.position;
        agent.Warp(hit.position);
    }

    private Vector3 ProjectDestinationToNavMesh(Vector3 destination)
    {
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
            return hit.position;

        return destination;
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

        if (currentState.Id == EnemyStateId.Patrol && IsMovingForAnimation())
            return EnemyAnimationState.Patrol;

        return EnemyAnimationState.Idle;
    }

    private bool IsMovingForAnimation()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            if (agent.pathPending)
                return true;

            if (agent.hasPath && agent.remainingDistance > patrolPointReachedDistance)
                return true;

            Vector3 agentVelocity = agent.velocity;
            agentVelocity.y = 0f;
            if (agentVelocity.sqrMagnitude > 0.01f)
                return true;
        }

        Vector3 velocity = rb != null ? rb.linearVelocity : Vector3.zero;
        velocity.y = 0f;
        return velocity.sqrMagnitude > 0.01f;
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
            return Time.time < localKillAnimationEndTime;

        return Object != null
            && networkSpawned
            && KillAnimationTimer.IsRunning
            && !KillAnimationTimer.Expired(Runner);
    }

    private void ClearExpiredKillAnimation()
    {
        if (Object == null || !Object.HasStateAuthority || !networkSpawned)
            return;

        if (!KillAnimationTimer.IsRunning || !KillAnimationTimer.Expired(Runner))
            return;

        KillAnimationTimer = TickTimer.None;
        pendingKillKnockback = null;
        pendingKillRagdoll = null;
        localKillAnimationEndTime = 0f;
    }

    private void TriggerAttackAnimation()
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        if (Object != null && networkSpawned)
        {
            AttackAnimationTimer = TickTimer.CreateFromSeconds(Runner, attackAnimationDuration);
            NetworkAttackSequence++;
            NetworkAnimationState = EnemyAnimationState.Attack;
            renderedAttackSequence = NetworkAttackSequence;
        }

        ApplyAnimationState(EnemyAnimationState.Attack, true);
    }

    private void TriggerKillAnimation(IKnockbackable knockbackable, RagdollEntityComponent ragdoll, Vector3 knockbackDirection, Transform killedVisual)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        StopMoving();
        RotateVisualTowardKillTarget(killedVisual);
        pendingKillKnockback = knockbackable;
        pendingKillRagdoll = ragdoll;
        pendingKillKnockbackDirection = knockbackDirection.sqrMagnitude > 0.0001f ? knockbackDirection.normalized : transform.forward;
        float killDuration = GetKillAnimationDuration();
        localKillAnimationEndTime = Time.time + killDuration;

        if (Object != null && networkSpawned)
        {
            KillAnimationTimer = TickTimer.CreateFromSeconds(Runner, killDuration);
            AttackAnimationTimer = TickTimer.None;
            NetworkKillSequence++;
            NetworkAnimationState = EnemyAnimationState.Kill;
            renderedKillSequence = NetworkKillSequence;
        }

        ApplyAnimationState(EnemyAnimationState.Kill, true);
    }

    private float GetKillAnimationDuration()
    {
        if (animationDriver == null)
            ResolveAnimationDriver();

        float fallback = Mathf.Max(0.1f, killAnimationDuration);
        return animationDriver != null ? animationDriver.GetClipLength(EnemyAnimationState.Kill, fallback) : fallback;
    }

    private void RotateVisualTowardKillTarget(Transform killedVisual)
    {
        if (killedVisual == null)
            return;

        Transform rotatingTransform = visual != null ? visual : transform;
        Vector3 direction = killedVisual.position - rotatingTransform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        rotatingTransform.rotation = targetRotation;

        if (Object != null && networkSpawned)
            NetworkRotation = targetRotation;
    }

    public void ApplyKillKnockbackAnimationEvent()
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        if (pendingKillKnockback == null)
            return;

        pendingKillKnockback.ApplyKnockback(pendingKillKnockbackDirection, killKnockbackForce, killKnockbackUpwardForce);
        pendingKillKnockback = null;
    }

    public void AnimationEvent_ApplyKillKnockback()
    {
        ApplyKillKnockbackAnimationEvent();
    }

    public void OnKillKnockback()
    {
        ApplyKillKnockbackAnimationEvent();
    }

    public void SpawnKillBloodSplatterAnimationEvent(int count)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        pendingKillRagdoll?.RequestBloodSplatter(count);
    }

    public void AnimationEvent_SpawnKillBloodSplatter(int count)
    {
        SpawnKillBloodSplatterAnimationEvent(count);
    }

    public void OnKillBloodSplatter(int count)
    {
        SpawnKillBloodSplatterAnimationEvent(count);
    }

    private void ApplyAnimationState(EnemyAnimationState stateId, bool force = false)
    {
        if (animationDriver == null)
            ResolveAnimationDriver();

        animationDriver?.Play(stateId, force);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        PlayerMovement player = collision.gameObject.GetComponentInParent<PlayerMovement>();
        NetworkHealthComponent health = GetPlayerComponent<NetworkHealthComponent>(collision.gameObject, player);
        RagdollEntityComponent ragdoll = GetPlayerComponent<RagdollEntityComponent>(collision.gameObject, player);
        IKnockbackable knockbackable = GetPlayerKnockbackable(collision.gameObject, player);
        Transform killedVisual = GetPlayerVisualTransform(collision.gameObject, player);

        if (health == null && ragdoll == null && player == null)
            return;

        if ((health != null && health.IsDead) || (ragdoll != null && ragdoll.IsDead))
            return;

        Vector3 knockbackDirection = collision.transform.position - transform.position;
        knockbackDirection.y = 0f;
        if (knockbackDirection.sqrMagnitude <= 0.0001f)
            knockbackDirection = visual != null ? visual.forward : transform.forward;

        if (health != null)
        {
            health.Damage(attackDamage);
            if (health.IsDead)
            {
                if (ragdoll != null)
                {
                    ragdoll.Kill();
                    ragdoll.ResetRagdollVelocity();
                }

                TriggerKillAnimation(knockbackable, ragdoll, knockbackDirection, killedVisual);
            }
            else
            {
                TriggerAttackAnimation();
            }
        }
        else if (ragdoll != null)
        {
            ragdoll.Kill();
            ragdoll.ResetRagdollVelocity();
            TriggerKillAnimation(knockbackable, ragdoll, knockbackDirection, killedVisual);
        }
        else
        {
            TriggerAttackAnimation();
        }

        if (player != null && !player.IsLocalNetworkPlayer)
            return;

        if (hasKilledLocalPlayer)
            return;

        hasKilledLocalPlayer = true;

        if (ui == null)
            ui = FindDeathUI();

        if (ui != null)
            ui.SetActive(true);

        StartCoroutine(Exit());
    }

    private GameObject FindDeathUI()
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject candidate in objects)
        {
            if (candidate.name == "DiedUI" && candidate.scene.IsValid())
                return candidate;
        }

        return null;
    }

    private void InitializeMoveDirection()
    {
        Vector3 direction = target != null ? target.position - transform.position : transform.forward;
        moveDirection = direction;
        moveDirection.y = 0f;
        moveDirection = moveDirection.sqrMagnitude > 0f ? moveDirection.normalized : transform.forward;
    }

    private static bool IsDeadTarget(Transform candidate)
    {
        if (candidate == null)
            return true;

        PlayerMovement player = candidate.GetComponentInParent<PlayerMovement>();
        NetworkHealthComponent health = GetPlayerComponent<NetworkHealthComponent>(candidate.gameObject, player);
        if (health != null && health.IsDead)
            return true;

        RagdollEntityComponent ragdoll = GetPlayerComponent<RagdollEntityComponent>(candidate.gameObject, player);
        return ragdoll != null && ragdoll.IsDead;
    }

    private static T GetPlayerComponent<T>(GameObject source, PlayerMovement player) where T : Component
    {
        T component = source.GetComponentInParent<T>();
        if (component != null)
            return component;

        return player != null ? player.GetComponentInChildren<T>(true) : null;
    }

    private static Transform GetPlayerVisualTransform(GameObject source, PlayerMovement player)
    {
        Transform playerRoot = player != null ? player.transform : source.GetComponentInParent<PlayerMovement>()?.transform;
        if (playerRoot == null)
            return source.transform;

        Transform visual = FindChildByName(playerRoot, "Visual");
        if (visual != null)
            return visual;

        Animator animator = playerRoot.GetComponentInChildren<Animator>(true);
        if (animator != null)
            return animator.transform;

        RagdollPartComponent ragdollPart = source.GetComponentInParent<RagdollPartComponent>();
        if (ragdollPart != null)
            return ragdollPart.transform;

        return playerRoot;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        foreach (Transform child in root)
        {
            Transform found = FindChildByName(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static IKnockbackable GetPlayerKnockbackable(GameObject source, PlayerMovement player)
    {
        IKnockbackable knockbackable = source.GetComponentInParent<IKnockbackable>();
        if (knockbackable != null || player == null)
            return knockbackable;

        MonoBehaviour[] behaviours = player.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IKnockbackable candidate)
                return candidate;
        }

        return null;
    }

    private IEnumerator Exit()
    {
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
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
            enemy.StopMoving();
            enemy.idleTimer = enemy.idleAtPatrolPointTime;
        }

        public void Tick(float deltaTime)
        {
            if (enemy.TryFindVisibleTarget(out Transform visibleTarget))
            {
                enemy.target = visibleTarget;
                enemy.lostSightTimer = 0f;
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
            travelDirection = enemy.GetInitialWallPatrolDirection();
            remainingSegmentDistance = enemy.GetRandomWallPatrolDistance();
            lastPosition = enemy.transform.position;
            RefreshDestination();
            enemy.MoveTo(patrolDestination, enemy.patrolSpeed);
        }

        public void Tick(float deltaTime)
        {
            if (enemy.TryFindVisibleTarget(out Transform visibleTarget))
            {
                enemy.target = visibleTarget;
                enemy.lostSightTimer = 0f;
                enemy.ChangeState(EnemyStateId.Chase);
                return;
            }

            Vector3 currentPosition = enemy.transform.position;
            Vector3 moved = currentPosition - lastPosition;
            moved.y = 0f;
            remainingSegmentDistance -= moved.magnitude;
            lastPosition = currentPosition;

            if (remainingSegmentDistance <= 0f)
                ReverseSegment();

            if (enemy.ShouldRefreshWallPatrolDestination(patrolDestination))
                RefreshDestination();

            enemy.MoveTo(patrolDestination, enemy.patrolSpeed);
            enemy.RotateTowardMovement();
        }

        public void Exit()
        {
        }

        private void ReverseSegment()
        {
            travelDirection = enemy.GetRandomReverseDirection(travelDirection);
            remainingSegmentDistance = enemy.GetRandomWallPatrolDistance();
            RefreshDestination();
        }

        private void RefreshDestination()
        {
            for (int i = 0; i < 2; i++)
            {
                if (enemy.TryGetWallFollowDestination(ref travelDirection, ref wallSide, out patrolDestination, out bool shouldReverse))
                    return;

                if (shouldReverse)
                {
                    travelDirection = enemy.GetRandomReverseDirection(travelDirection);
                    remainingSegmentDistance = enemy.GetRandomWallPatrolDistance();
                }
            }

            patrolDestination = enemy.GetRandomPatrolPoint();
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
            enemy.lostSightTimer = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (enemy.ShouldLoseTarget(deltaTime))
            {
                enemy.target = null;
                enemy.ChangeState(EnemyStateId.Patrol);
                return;
            }

            enemy.MoveTo(enemy.target.position, enemy.moveSpeed);
            enemy.RotateTowardMovement();
        }

        public void Exit()
        {
            enemy.StopMoving();
        }
    }
}
