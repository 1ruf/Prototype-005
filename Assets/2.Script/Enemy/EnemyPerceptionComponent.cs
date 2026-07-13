using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyPerceptionComponent : MonoBehaviour
{
    [System.Serializable]
    public struct LegacySettings
    {
        public float DetectionRange;
        public float ProximityDetectionRange;
        public float LoseRange;
        public float ViewAngle;
        public float LoseSightDelay;
        public LayerMask LineOfSightMask;
        public float EyeHeight;
        public bool PreferUnclaimedChaseTargets;
        public float TargetClaimSwitchDistance;
    }

    [SerializeField] private bool useLegacySettings = true;
    [SerializeField] private LegacySettings settings;

    private CSHEnemy coordinator;
    private Transform visual;
    private float lostSightTimer;

    public float LoseRange => settings.LoseRange;
    public float ProximityDetectionRange => settings.ProximityDetectionRange;

    public void ConfigureLegacy(CSHEnemy owner, Transform visualRoot, LegacySettings legacySettings)
    {
        coordinator = owner;
        visual = visualRoot;
        if (useLegacySettings)
            settings = legacySettings;
    }

    public void CommitLegacySettings()
    {
        useLegacySettings = false;
    }

    public void ResetTargetTracking()
    {
        lostSightTimer = 0f;
    }

    public bool TryFindVisibleTarget(out Transform foundTarget)
    {
        float closestDistance = float.PositiveInfinity;
        foundTarget = null;

        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player == null || IsDeadTarget(player.transform) || IsHiddenTarget(player.transform))
                continue;

            Vector3 offset = player.transform.position - coordinator.transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance > settings.DetectionRange || distance >= closestDistance)
                continue;

            if (distance > settings.ProximityDetectionRange && !CanSee(player.transform, settings.DetectionRange))
                continue;

            if (settings.PreferUnclaimedChaseTargets && IsTargetClaimedByOtherEnemy(player.transform, distance))
                continue;

            closestDistance = distance;
            foundTarget = player.transform;
        }

        return foundTarget != null;
    }

    public bool CanSee(Transform candidate, float maxDistance)
    {
        if (candidate == null || coordinator == null)
            return false;

        Vector3 toTarget = candidate.position - coordinator.transform.position;
        float distance = toTarget.magnitude;
        if (distance > maxDistance)
            return false;

        Vector3 flatDirection = toTarget;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude <= 0.0001f)
            return true;

        Vector3 forward = visual != null ? visual.forward : coordinator.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = coordinator.transform.forward;

        if (settings.ViewAngle < 359f && Vector3.Angle(forward.normalized, flatDirection.normalized) > settings.ViewAngle * 0.5f)
            return false;

        Vector3 origin = coordinator.transform.position + Vector3.up * settings.EyeHeight;
        Vector3 targetPoint = candidate.position + Vector3.up * settings.EyeHeight;
        Vector3 rayDirection = targetPoint - origin;

        RaycastHit[] hits = Physics.RaycastAll(origin, rayDirection.normalized, distance, settings.LineOfSightMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null)
                continue;

            if (hit.transform == candidate || hit.transform.IsChildOf(candidate))
                return true;

            if (hit.transform == coordinator.transform || hit.transform.IsChildOf(coordinator.transform))
                continue;

            return false;
        }

        return true;
    }

    public bool ShouldLoseTarget(Transform target, float deltaTime)
    {
        if (target == null || IsDeadTarget(target))
            return true;

        if (IsHiddenTarget(target))
            return true;

        if (IsCompromisedHiddenTarget(target))
            return false;

        if (settings.PreferUnclaimedChaseTargets && HasBetterEnemyClaimingTarget(target))
            return true;

        Vector3 offset = target.position - coordinator.transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        if (distance > settings.LoseRange)
            return true;

        if (distance <= settings.ProximityDetectionRange)
        {
            lostSightTimer = 0f;
            coordinator.RefreshChasePointFromPerception();
            return false;
        }

        if (CanSee(target, settings.LoseRange))
        {
            lostSightTimer = 0f;
            coordinator.RefreshChasePointFromPerception();
            return false;
        }

        lostSightTimer += deltaTime;
        return lostSightTimer >= settings.LoseSightDelay;
    }

    public bool CanCurrentlySee(PlayerMovement player)
    {
        return player != null && !IsDeadTarget(player.transform) && CanSee(player.transform, settings.LoseRange);
    }

    private bool IsTargetClaimedByOtherEnemy(Transform candidate, float challengerDistance)
    {
        if (candidate == null)
            return false;

        foreach (CSHEnemy other in EnemyRuntimeRegistry.Enemies)
        {
            if (other == null || other == coordinator || !other.isActiveAndEnabled)
                continue;

            if (!other.IsChasingTransformForServices(candidate))
                continue;

            float otherDistance = GetFlatDistance(other.transform.position, candidate.position);
            if (otherDistance <= challengerDistance + Mathf.Max(0f, settings.TargetClaimSwitchDistance))
                return true;
        }

        return false;
    }

    private bool HasBetterEnemyClaimingTarget(Transform candidate)
    {
        if (candidate == null)
            return false;

        float myDistance = GetFlatDistance(coordinator.transform.position, candidate.position);

        foreach (CSHEnemy other in EnemyRuntimeRegistry.Enemies)
        {
            if (other == null || other == coordinator || !other.isActiveAndEnabled)
                continue;

            if (!other.IsChasingTransformForServices(candidate))
                continue;

            float otherDistance = GetFlatDistance(other.transform.position, candidate.position);
            if (otherDistance + Mathf.Max(0f, settings.TargetClaimSwitchDistance) < myDistance)
                return true;

            if (Mathf.Abs(otherDistance - myDistance) <= Mathf.Max(0f, settings.TargetClaimSwitchDistance)
                && other.TargetClaimOrderForServices < coordinator.TargetClaimOrderForServices)
                return true;
        }

        return false;
    }

    public static float GetFlatDistance(Vector3 from, Vector3 to)
    {
        Vector3 offset = to - from;
        offset.y = 0f;
        return offset.magnitude;
    }

    public static bool IsDeadTarget(Transform candidate)
    {
        if (candidate == null)
            return true;

        PlayerMovement player = candidate.GetComponentInParent<PlayerMovement>();
        NetworkHealthComponent health = GetPlayerComponent<NetworkHealthComponent>(candidate.gameObject, player);
        if (health != null && (health.IsDead || health.CurrentHealth <= 0f))
            return true;

        RagdollEntityComponent ragdoll = GetPlayerComponent<RagdollEntityComponent>(candidate.gameObject, player);
        return ragdoll != null && (ragdoll.IsDead || ragdoll.IsRagdollEnabled);
    }

    public static bool IsHiddenTarget(Transform candidate)
    {
        if (candidate == null)
            return false;

        PlayerMovement player = candidate.GetComponentInParent<PlayerMovement>();
        NetworkPlayerHidingComponent hiding = player != null
            ? player.Owner.GetComponentInChildren<NetworkPlayerHidingComponent>(true)
            : candidate.GetComponentInParent<NetworkPlayerHidingComponent>();
        return hiding != null && hiding.IsHiding && !hiding.IsHidingCompromised;
    }

    public static bool IsCompromisedHiddenTarget(Transform candidate)
    {
        if (candidate == null)
            return false;

        PlayerMovement player = candidate.GetComponentInParent<PlayerMovement>();
        NetworkPlayerHidingComponent hiding = player != null
            ? player.Owner.GetComponentInChildren<NetworkPlayerHidingComponent>(true)
            : candidate.GetComponentInParent<NetworkPlayerHidingComponent>();
        return hiding != null && hiding.IsHiding && hiding.IsHidingCompromised;
    }

    private static T GetPlayerComponent<T>(GameObject source, PlayerMovement player) where T : Component
    {
        T component = source.GetComponentInParent<T>();
        if (component != null)
            return component;

        return player != null ? player.GetComponentInChildren<T>(true) : null;
    }
}
