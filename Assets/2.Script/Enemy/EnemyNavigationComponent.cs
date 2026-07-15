using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class EnemyNavigationComponent : MonoBehaviour
{
    [System.Serializable]
    public struct LegacySettings
    {
        public float MoveSpeed;
        public float RotationSpeed;
        public float PatrolSpeed;
        public float IdleAtPatrolPointTime;
        public float InvestigateDuration;
        public float InvestigateRadius;
        public float PatrolRadius;
        public float PatrolPointReachedDistance;
        public LayerMask PatrolWallMask;
        public Vector2 WallPatrolDistanceRange;
        public float PatrolWallInfluence;
        public Vector2 PatrolWanderYawRange;
        public Vector2 PatrolSegmentYawRange;
        public Vector2 PatrolReverseYawRange;
        public float PatrolRandomReverseChance;
        public float WallFollowProbeDistance;
        public float WallFollowClearance;
        public float WallFollowStepDistance;
        public float WallFollowForwardBlockDistance;
        public float WallFollowDestinationRefreshDistance;
        public float EnemySeparationRadius;
        public float EnemySeparationStrength;
        public float EnemySeparationMaxForce;
        public float NavMeshSnapDistance;
        public float ChaseNavMeshSampleDistance;
        public float ChaseTargetNavMeshSampleDistance;
        public float ChaseMaxNavPointJumpDistance;
        public float ChaseTargetBackoffDistance;
        public float ChaseTargetBackoffStep;
        public float ChaseDirectFallbackDistance;
        public float EyeHeight;
        public LayerMask ChaseDoorBreakMask;
        public float ChaseDoorBreakDistance;
        public float ChaseDoorBreakRadius;
        public float ChaseDoorBreakHeight;
        public float ChaseDoorBreakCooldown;
    }

    private readonly struct NavigationPoint
    {
        public readonly Vector3 Position;
        public readonly bool IsValid;

        public NavigationPoint(Vector3 position)
        {
            Position = position;
            IsValid = true;
        }
    }

    [SerializeField] private bool useLegacySettings = true;
    [SerializeField] private LegacySettings settings;

    private CSHEnemy coordinator;
    private Transform visual;
    private Rigidbody body;
    private NavMeshAgent agent;
    private Vector3 moveDirection;
    private Vector3 spawnPosition;
    private float nextAllowedDoorBreakTime;
    private NavigationPoint chasePoint;
    private Vector3 lastObservedTargetPosition;

    public float PatrolPointReachedDistance => settings.PatrolPointReachedDistance;
    public float MoveSpeed => settings.MoveSpeed;
    public float PatrolSpeed => settings.PatrolSpeed;
    public float IdleAtPatrolPointTime => settings.IdleAtPatrolPointTime;
    public float InvestigateDuration => settings.InvestigateDuration;
    public float InvestigateRadius => settings.InvestigateRadius;

    public void ConfigureLegacy(CSHEnemy owner, Transform visualRoot, LegacySettings legacySettings)
    {
        coordinator = owner;
        visual = visualRoot;
        if (useLegacySettings)
            settings = legacySettings;

        GameObject entityRoot = owner != null ? owner.gameObject : gameObject;
        body = entityRoot.GetComponent<Rigidbody>();
        if (body != null)
            body.freezeRotation = true;

        agent = entityRoot.GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = entityRoot.AddComponent<NavMeshAgent>();

        ConfigureAgent();
    }

    public void CommitLegacySettings()
    {
        useLegacySettings = false;
    }

    public void CaptureSpawnPosition()
    {
        spawnPosition = coordinator.transform.position;
    }

    public void ConfigureAgent()
    {
        if (agent == null)
            return;

        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.speed = settings.PatrolSpeed;
        agent.angularSpeed = 720f;
        agent.acceleration = 16f;
        agent.stoppingDistance = 0.1f;
        agent.radius = Mathf.Max(agent.radius, 0.65f);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(35, 66);
    }

    public void SnapToNavMesh()
    {
        if (agent == null || !agent.enabled)
            return;

        if (!NavMesh.SamplePosition(coordinator.transform.position, out NavMeshHit hit, settings.NavMeshSnapDistance, NavMesh.AllAreas))
            return;

        coordinator.transform.position = hit.position;
        agent.Warp(hit.position);
    }

    public void ApplyNetworkAuthority(bool hasStateAuthority)
    {
        if (body != null)
            body.isKinematic = !hasStateAuthority;

        if (!hasStateAuthority && agent != null)
            agent.enabled = false;
    }

    public void InitializeMoveDirection(Transform target)
    {
        Vector3 direction = target != null
            ? target.position - coordinator.transform.position
            : coordinator.transform.forward;

        moveDirection = direction;
        moveDirection.y = 0f;
        moveDirection = moveDirection.sqrMagnitude > 0f ? moveDirection.normalized : coordinator.transform.forward;
    }

    public void ResetChaseTracking(Transform target)
    {
        chasePoint = default;
        lastObservedTargetPosition = target != null ? target.position : coordinator.transform.position;
        RefreshChasePoint(target);
    }

    public Vector3 GetLastKnownTargetNavigationPosition(Transform target)
    {
        if (chasePoint.IsValid)
            return chasePoint.Position;

        return target != null ? target.position : lastObservedTargetPosition;
    }

    public void MoveTo(Vector3 destination, float speed)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            destination = ProjectDestinationToNavMesh(destination);
            agent.nextPosition = coordinator.transform.position;
            agent.speed = speed;
            agent.isStopped = false;
            agent.SetDestination(destination);

            Vector3 desiredVelocity = agent.desiredVelocity;
            if (desiredVelocity.sqrMagnitude <= 0.001f && agent.hasPath && agent.path.corners.Length > 1)
                desiredVelocity = (agent.path.corners[1] - coordinator.transform.position).normalized * speed;

            if (desiredVelocity.sqrMagnitude <= 0.001f)
                desiredVelocity = ResolveImmediatePathVelocity(destination, speed);

            desiredVelocity = ApplyEnemySeparation(desiredVelocity, speed);
            desiredVelocity.y = body.linearVelocity.y;
            body.linearVelocity = desiredVelocity.sqrMagnitude > 0.001f ? desiredVelocity : body.linearVelocity;
            return;
        }

        MoveDirectlyTo(destination, speed, Time.deltaTime);
    }

    public void StopMoving()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.nextPosition = coordinator.transform.position;
        }

        if (body != null)
            body.linearVelocity = Vector3.zero;
    }

    public bool HasReachedDestination()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            return !agent.pathPending && agent.remainingDistance <= settings.PatrolPointReachedDistance;

        return body == null || body.linearVelocity.sqrMagnitude <= 0.01f;
    }

    public void MoveDirectlyTo(Vector3 destination, float speed, float deltaTime)
    {
        Vector3 targetDirection = destination - coordinator.transform.position;
        targetDirection.y = 0f;

        if (targetDirection.sqrMagnitude <= 0.0001f)
        {
            if (body != null)
                body.linearVelocity = Vector3.zero;
            return;
        }

        targetDirection.Normalize();
        moveDirection = Vector3.Lerp(moveDirection, targetDirection, settings.RotationSpeed * deltaTime).normalized;
        Vector3 desiredVelocity = ApplyEnemySeparation(moveDirection * speed, speed);
        if (body != null)
            body.linearVelocity = desiredVelocity;
        RotateToward(moveDirection);
    }

    public void RotateTowardMovement()
    {
        Vector3 velocity = agent != null && agent.enabled
            ? agent.velocity
            : body != null ? body.linearVelocity : Vector3.zero;
        velocity.y = 0f;

        if (velocity.sqrMagnitude <= 0.001f)
            return;

        RotateToward(velocity.normalized);
    }

    public void TryBreakChaseDoor(Transform target)
    {
        if (Time.time < nextAllowedDoorBreakTime)
            return;

        Vector3 direction = ResolveForwardMovementDirection(target);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 origin = coordinator.transform.position + Vector3.up * Mathf.Max(0f, settings.ChaseDoorBreakHeight);
        if (!Physics.SphereCast(origin, Mathf.Max(0.01f, settings.ChaseDoorBreakRadius), direction, out RaycastHit hitInfo, Mathf.Max(0f, settings.ChaseDoorBreakDistance), settings.ChaseDoorBreakMask, QueryTriggerInteraction.Ignore))
            return;

        Door door = hitInfo.transform.GetComponentInParent<Door>();
        if (door == null || door.IsOpen || door.IsLocked || door.IsBroken)
            return;

        nextAllowedDoorBreakTime = Time.time + Mathf.Max(0.01f, settings.ChaseDoorBreakCooldown);
        door.RequestBreak(direction);
    }

    public Vector3 GetRandomPatrolPoint()
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 random = Random.insideUnitCircle * settings.PatrolRadius;
            Vector3 candidate = spawnPosition + new Vector3(random.x, 0f, random.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;
        }

        return spawnPosition;
    }

    public bool TryGetCoordinatedPatrolPoint(out Vector3 destination)
    {
        destination = spawnPosition;
        Vector3 origin = coordinator.transform.position;
        float patrolRange = Mathf.Max(24f, settings.PatrolRadius);
        float bestScore = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < 28; i++)
        {
            Vector2 direction = Random.insideUnitCircle;
            if (direction.sqrMagnitude <= 0.001f)
                continue;

            // Bias away from the immediate area: patrols should make meaningful sweeps.
            float distance = patrolRange * Random.Range(0.35f, 1f);
            Vector3 candidate = spawnPosition + new Vector3(direction.x, 0f, direction.y).normalized * distance;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, settings.NavMeshSnapDistance, NavMesh.AllAreas)
                || !HasValidPathTo(hit.position))
                continue;

            float score = EnemyPatrolCoverageRegistry.GetDestinationScore(coordinator, origin, hit.position);
            if (score <= bestScore)
                continue;

            bestScore = score;
            destination = hit.position;
            found = true;
        }

        if (found)
            EnemyPatrolCoverageRegistry.ReserveDestination(coordinator, destination);

        return found;
    }

    public void RecordPatrolCoverage()
    {
        EnemyPatrolCoverageRegistry.RecordPatrolPosition(coordinator.transform.position);
    }

    public Vector3 GetInitialWallPatrolDirection()
    {
        Vector3 direction = visual != null ? visual.forward : coordinator.transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = moveDirection;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = coordinator.transform.forward;

        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    public float GetRandomWallPatrolDistance()
    {
        float min = Mathf.Max(1f, Mathf.Min(settings.WallPatrolDistanceRange.x, settings.WallPatrolDistanceRange.y));
        float max = Mathf.Max(min, Mathf.Max(settings.WallPatrolDistanceRange.x, settings.WallPatrolDistanceRange.y));
        return Random.Range(min, max);
    }

    public bool ShouldRefreshWallPatrolDestination(Vector3 destination)
    {
        Vector3 offset = destination - coordinator.transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude <= settings.WallFollowDestinationRefreshDistance * settings.WallFollowDestinationRefreshDistance)
            return true;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            return !agent.pathPending && (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid);

        return false;
    }

    public bool TryGetWallFollowDestination(ref Vector3 travelDirection, ref int wallSide, out Vector3 destination, out bool shouldReverse)
    {
        shouldReverse = false;
        destination = coordinator.transform.position;
        travelDirection.y = 0f;

        if (travelDirection.sqrMagnitude <= 0.0001f)
            travelDirection = GetInitialWallPatrolDirection();

        travelDirection.Normalize();

        if (IsWallPatrolForwardBlocked(travelDirection))
        {
            shouldReverse = true;
            return false;
        }

        Vector3 origin = coordinator.transform.position + Vector3.up * settings.EyeHeight;
        Vector3 sideDirection = GetSideDirection(travelDirection, wallSide);
        bool foundWall = Physics.Raycast(origin, sideDirection, out RaycastHit wallHit, settings.WallFollowProbeDistance, settings.PatrolWallMask, QueryTriggerInteraction.Ignore);

        if (!foundWall && TryFindNearbyWall(origin, travelDirection, out int detectedSide))
        {
            wallSide = detectedSide;
            sideDirection = GetSideDirection(travelDirection, wallSide);
            foundWall = Physics.Raycast(origin, sideDirection, out wallHit, settings.WallFollowProbeDistance, settings.PatrolWallMask, QueryTriggerInteraction.Ignore);
        }

        Vector3 wanderDirection = Quaternion.Euler(0f, Random.Range(settings.PatrolWanderYawRange.x, settings.PatrolWanderYawRange.y), 0f) * travelDirection;
        Vector3 desiredDirection = wanderDirection.normalized;
        Vector3 clearanceCorrection = Vector3.zero;
        if (foundWall)
        {
            Vector3 wallForward = Vector3.Cross(Vector3.up, wallHit.normal).normalized;
            if (Vector3.Dot(wallForward, desiredDirection) < 0f)
                wallForward = -wallForward;

            desiredDirection = Vector3.Slerp(desiredDirection, wallForward, settings.PatrolWallInfluence).normalized;
            clearanceCorrection = sideDirection * ((wallHit.distance - settings.WallFollowClearance) * Mathf.Clamp01(settings.PatrolWallInfluence));
        }

        Vector3 candidate = coordinator.transform.position + desiredDirection * settings.WallFollowStepDistance + clearanceCorrection;
        if (TryProjectWallPatrolPoint(candidate, out destination))
        {
            travelDirection = desiredDirection;
            return true;
        }

        candidate = coordinator.transform.position + travelDirection * settings.WallFollowStepDistance;
        if (TryProjectWallPatrolPoint(candidate, out destination))
            return true;

        shouldReverse = true;
        return false;
    }

    public Vector3 GetRandomReverseDirection(Vector3 currentDirection)
    {
        currentDirection.y = 0f;
        if (currentDirection.sqrMagnitude <= 0.0001f)
            currentDirection = GetInitialWallPatrolDirection();

        float yaw = Random.Range(settings.PatrolReverseYawRange.x, settings.PatrolReverseYawRange.y);
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * currentDirection.normalized;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : -currentDirection.normalized;
    }

    public Vector3 GetRandomForwardSegmentDirection(Vector3 currentDirection)
    {
        currentDirection.y = 0f;
        if (currentDirection.sqrMagnitude <= 0.0001f)
            currentDirection = GetInitialWallPatrolDirection();

        float yaw = Random.Range(settings.PatrolSegmentYawRange.x, settings.PatrolSegmentYawRange.y);
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * currentDirection.normalized;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : currentDirection.normalized;
    }

    public bool ShouldRandomlyReversePatrol()
    {
        return Random.value < settings.PatrolRandomReverseChance;
    }

    public Vector3 GetRandomInvestigatePoint(Vector3 investigateCenter, float investigateRadius)
    {
        Vector2 offset = Random.insideUnitCircle * Mathf.Max(0.1f, investigateRadius);
        Vector3 candidate = investigateCenter + new Vector3(offset.x, 0f, offset.y);
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, settings.NavMeshSnapDistance, NavMesh.AllAreas))
            return hit.position;

        return candidate;
    }

    public Vector3 ResolveChaseDestination(Transform chaseTarget, out bool useDirectChaseMovement)
    {
        useDirectChaseMovement = false;

        if (chaseTarget == null)
            return coordinator.transform.position;

        RefreshChasePoint(chaseTarget);

        if (chasePoint.IsValid)
            return chasePoint.Position;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            return coordinator.transform.position;

        Vector3 targetPosition = chaseTarget.position;
        float targetDistance = EnemyPerceptionComponent.GetFlatDistance(coordinator.transform.position, targetPosition);
        if (targetDistance <= Mathf.Max(0f, settings.ChaseDirectFallbackDistance)
            || coordinator.CanSeeForServices(chaseTarget, coordinator.LoseRangeForServices))
        {
            useDirectChaseMovement = true;
            return targetPosition;
        }

        return coordinator.transform.position;
    }

    public bool RefreshChasePoint(Transform target)
    {
        if (target == null)
            return false;

        lastObservedTargetPosition = target.position;
        if (!TryResolveChaseNavPoint(lastObservedTargetPosition, out NavigationPoint point))
            return false;

        if (chasePoint.IsValid
            && EnemyPerceptionComponent.GetFlatDistance(chasePoint.Position, point.Position) > Mathf.Max(0.1f, settings.ChaseMaxNavPointJumpDistance))
            return false;

        chasePoint = point;
        return true;
    }

    public bool IsMovingForAnimation()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            if (agent.pathPending)
                return true;

            if (agent.hasPath && agent.remainingDistance > settings.PatrolPointReachedDistance)
                return true;

            Vector3 agentVelocity = agent.velocity;
            agentVelocity.y = 0f;
            if (agentVelocity.sqrMagnitude > 0.01f)
                return true;
        }

        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        velocity.y = 0f;
        return velocity.sqrMagnitude > 0.01f;
    }

    private Vector3 ResolveImmediatePathVelocity(Vector3 destination, float speed)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return Vector3.zero;

        NavMeshPath path = new NavMeshPath();
        if (agent.CalculatePath(destination, path) && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 1)
        {
            Vector3 nextCornerDirection = path.corners[1] - coordinator.transform.position;
            nextCornerDirection.y = 0f;
            if (nextCornerDirection.sqrMagnitude > 0.001f)
                return nextCornerDirection.normalized * speed;
        }

        Vector3 directDirection = destination - coordinator.transform.position;
        directDirection.y = 0f;
        return directDirection.sqrMagnitude > 0.001f ? directDirection.normalized * speed : Vector3.zero;
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
        if (settings.EnemySeparationRadius <= 0f || settings.EnemySeparationStrength <= 0f)
            return Vector3.zero;

        Vector3 separation = Vector3.zero;
        Vector3 position = coordinator.transform.position;
        float radiusSqr = settings.EnemySeparationRadius * settings.EnemySeparationRadius;

        foreach (CSHEnemy other in EnemyRuntimeRegistry.Enemies)
        {
            if (other == null || other == coordinator || !other.HasActiveStateForServices)
                continue;

            Vector3 away = position - other.transform.position;
            away.y = 0f;
            float distanceSqr = away.sqrMagnitude;
            if (distanceSqr > radiusSqr)
                continue;

            if (distanceSqr <= 0.0001f)
            {
                away = visual != null ? visual.right : coordinator.transform.right;
                away.y = 0f;
                distanceSqr = away.sqrMagnitude;
            }

            if (distanceSqr <= 0.0001f)
                continue;

            float distance = Mathf.Sqrt(distanceSqr);
            float weight = 1f - Mathf.Clamp01(distance / settings.EnemySeparationRadius);
            separation += away / distance * (weight * settings.EnemySeparationStrength);
        }

        float maxForce = Mathf.Max(0f, settings.EnemySeparationMaxForce);
        if (maxForce > 0f && separation.sqrMagnitude > maxForce * maxForce)
            separation = separation.normalized * maxForce;

        return separation;
    }

    private void RotateToward(Vector3 direction)
    {
        if (direction == Vector3.zero)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        if (visual != null)
            visual.rotation = Quaternion.Slerp(visual.rotation, targetRotation, Time.deltaTime * 12f);
        else
            coordinator.transform.rotation = Quaternion.Slerp(coordinator.transform.rotation, targetRotation, Time.deltaTime * 12f);
    }

    private Vector3 ResolveForwardMovementDirection(Transform target)
    {
        Vector3 direction = body != null ? body.linearVelocity : Vector3.zero;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.01f && agent != null)
        {
            direction = agent.desiredVelocity;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.01f && target != null)
        {
            direction = target.position - coordinator.transform.position;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.01f)
        {
            direction = visual != null ? visual.forward : coordinator.transform.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private bool TryFindNearbyWall(Vector3 origin, Vector3 travelDirection, out int wallSide)
    {
        wallSide = 1;
        Vector3 right = GetSideDirection(travelDirection, 1);
        Vector3 left = GetSideDirection(travelDirection, -1);
        bool hasRight = Physics.Raycast(origin, right, out RaycastHit rightHit, settings.WallFollowProbeDistance, settings.PatrolWallMask, QueryTriggerInteraction.Ignore);
        bool hasLeft = Physics.Raycast(origin, left, out RaycastHit leftHit, settings.WallFollowProbeDistance, settings.PatrolWallMask, QueryTriggerInteraction.Ignore);

        if (!hasRight && !hasLeft)
            return false;

        wallSide = hasLeft && (!hasRight || leftHit.distance < rightHit.distance) ? -1 : 1;
        return true;
    }

    private bool IsWallPatrolForwardBlocked(Vector3 travelDirection)
    {
        Vector3 origin = coordinator.transform.position + Vector3.up * settings.EyeHeight;
        return Physics.Raycast(origin, travelDirection, settings.WallFollowForwardBlockDistance, settings.PatrolWallMask, QueryTriggerInteraction.Ignore);
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

    private Vector3 ProjectDestinationToNavMesh(Vector3 destination)
    {
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, settings.NavMeshSnapDistance, NavMesh.AllAreas))
            return hit.position;

        return destination;
    }

    private bool HasValidPathTo(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return true;

        NavMeshPath path = new NavMeshPath();
        return agent.CalculatePath(destination, path)
            && path.status != NavMeshPathStatus.PathInvalid
            && path.corners.Length > 1;
    }

    private bool TryResolveChaseNavPoint(Vector3 targetPosition, out NavigationPoint point)
    {
        point = default;

        Vector3 towardEnemy = coordinator.transform.position - targetPosition;
        towardEnemy.y = 0f;

        if (towardEnemy.sqrMagnitude <= 0.0001f)
        {
            towardEnemy = visual != null ? -visual.forward : -coordinator.transform.forward;
            towardEnemy.y = 0f;
        }

        if (towardEnemy.sqrMagnitude <= 0.0001f)
            towardEnemy = Vector3.back;

        towardEnemy.Normalize();

        float maxBackoff = Mathf.Max(0f, settings.ChaseTargetBackoffDistance);
        float step = Mathf.Max(0.1f, settings.ChaseTargetBackoffStep);
        int steps = Mathf.Max(1, Mathf.CeilToInt(maxBackoff / step));
        float sampleDistance = Mathf.Max(0.05f, settings.ChaseNavMeshSampleDistance, settings.ChaseTargetNavMeshSampleDistance);

        for (int i = 0; i <= steps; i++)
        {
            float distance = Mathf.Min(i * step, maxBackoff);
            Vector3 candidate = targetPosition + towardEnemy * distance;

            if (TryProjectReachableChasePoint(candidate, sampleDistance, out Vector3 reachablePoint))
            {
                point = new NavigationPoint(reachablePoint);
                return true;
            }
        }

        if (TryProjectReachableChasePoint(targetPosition, sampleDistance, out Vector3 targetPoint))
        {
            point = new NavigationPoint(targetPoint);
            return true;
        }

        return false;
    }

    private bool TryProjectReachableChasePoint(Vector3 candidate, float sampleDistance, out Vector3 point)
    {
        point = candidate;

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return false;

        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
            return false;

        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path))
            return false;

        if (path.status == NavMeshPathStatus.PathInvalid)
            return false;

        point = hit.position;
        return true;
    }
}
