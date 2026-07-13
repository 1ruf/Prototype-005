using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum ServerRequestValidationFailure
{
    None,
    InvalidRunner,
    MissingStateAuthority,
    InvalidSource,
    MissingPlayerObject,
    PlayerAuthorityMismatch,
    TargetOwnershipMismatch,
    RateLimited,
    TooFar,
    LineOfSightBlocked
}

public readonly struct ServerRequestContext
{
    public PlayerRef Requester { get; }
    public NetworkObject PlayerObject { get; }
    public bool IsServerRequest { get; }

    public ServerRequestContext(PlayerRef requester, NetworkObject playerObject, bool isServerRequest)
    {
        Requester = requester;
        PlayerObject = playerObject;
        IsServerRequest = isServerRequest;
    }
}

public static class ServerRequestValidator
{
    private readonly struct RateLimitKey : IEquatable<RateLimitKey>
    {
        private readonly int runnerInstanceId;
        private readonly int requesterRaw;
        private readonly uint targetRaw;
        private readonly int scope;

        public RateLimitKey(int runnerInstanceId, int requesterRaw, uint targetRaw, int scope)
        {
            this.runnerInstanceId = runnerInstanceId;
            this.requesterRaw = requesterRaw;
            this.targetRaw = targetRaw;
            this.scope = scope;
        }

        public bool Equals(RateLimitKey other)
        {
            return runnerInstanceId == other.runnerInstanceId
                && requesterRaw == other.requesterRaw
                && targetRaw == other.targetRaw
                && scope == other.scope;
        }

        public override bool Equals(object obj)
        {
            return obj is RateLimitKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = runnerInstanceId;
                hash = (hash * 397) ^ requesterRaw;
                hash = (hash * 397) ^ (int)targetRaw;
                hash = (hash * 397) ^ scope;
                return hash;
            }
        }
    }

    private sealed class RateLimitState
    {
        public double Tokens;
        public double LastRefillTime;
        public double LastSeenTime;
    }

    private const int RateLimitCleanupThreshold = 2048;
    private const double StaleRateLimitSeconds = 60d;

    private static readonly Dictionary<RateLimitKey, RateLimitState> RateLimits = new Dictionary<RateLimitKey, RateLimitState>();
    private static readonly List<RateLimitKey> StaleRateLimitKeys = new List<RateLimitKey>();

    public static bool TryValidate(
        NetworkRunner runner,
        NetworkObject targetObject,
        Transform targetTransform,
        RpcInfo rpcInfo,
        ServerRequestValidationPolicy policy,
        int rateLimitScope,
        out ServerRequestContext context,
        out ServerRequestValidationFailure failure,
        bool allowServerRequest = true)
    {
        context = default;

        if (runner == null || !runner.IsRunning)
        {
            failure = ServerRequestValidationFailure.InvalidRunner;
            return false;
        }

        if (targetObject == null || !targetObject.IsValid || !targetObject.HasStateAuthority)
        {
            failure = ServerRequestValidationFailure.MissingStateAuthority;
            return false;
        }

        PlayerRef requester = rpcInfo.Source;
        if (requester.IsNone)
        {
            if (!allowServerRequest)
            {
                failure = ServerRequestValidationFailure.InvalidSource;
                return false;
            }

            context = new ServerRequestContext(PlayerRef.None, null, true);
            failure = ServerRequestValidationFailure.None;
            return true;
        }

        if (!requester.IsRealPlayer)
        {
            failure = ServerRequestValidationFailure.InvalidSource;
            return false;
        }

        if (!runner.TryGetPlayerObject(requester, out NetworkObject playerObject)
            || playerObject == null
            || !playerObject.IsValid)
        {
            failure = ServerRequestValidationFailure.MissingPlayerObject;
            return false;
        }

        if (playerObject.InputAuthority != requester && playerObject.StateAuthority != requester)
        {
            failure = ServerRequestValidationFailure.PlayerAuthorityMismatch;
            return false;
        }

        policy ??= ServerRequestValidationPolicy.CreateInteractionDefault();
        if (!TryConsumeRateLimit(runner, requester, targetObject, rateLimitScope, policy))
        {
            failure = ServerRequestValidationFailure.RateLimited;
            return false;
        }

        Transform requesterTransform = playerObject.transform;
        Transform validationTarget = targetTransform != null ? targetTransform : targetObject.transform;
        if (!IsWithinDistance(requesterTransform, validationTarget, policy))
        {
            failure = ServerRequestValidationFailure.TooFar;
            return false;
        }

        if (policy.RequireLineOfSight && !HasLineOfSight(requesterTransform, validationTarget, policy))
        {
            failure = ServerRequestValidationFailure.LineOfSightBlocked;
            return false;
        }

        context = new ServerRequestContext(requester, playerObject, false);
        failure = ServerRequestValidationFailure.None;
        return true;
    }

    public static bool TryValidateOwnerRequest(
        NetworkRunner runner,
        NetworkObject targetObject,
        Transform targetTransform,
        RpcInfo rpcInfo,
        ServerRequestValidationPolicy policy,
        int rateLimitScope,
        out ServerRequestContext context,
        out ServerRequestValidationFailure failure,
        bool allowServerRequest = true)
    {
        if (!TryValidate(
                runner,
                targetObject,
                targetTransform,
                rpcInfo,
                policy,
                rateLimitScope,
                out context,
                out failure,
                allowServerRequest))
            return false;

        if (context.IsServerRequest)
            return true;

        if (context.PlayerObject == targetObject)
            return true;

        failure = ServerRequestValidationFailure.TargetOwnershipMismatch;
        context = default;
        return false;
    }

    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsWithinDistance(
        Transform requesterTransform,
        Transform targetTransform,
        ServerRequestValidationPolicy policy)
    {
        if (requesterTransform == null || targetTransform == null)
            return false;

        float maxDistance = policy.MaxDistance;
        if (maxDistance <= 0f)
            return true;

        Vector3 requesterPosition = requesterTransform.position;
        Vector3 targetPosition = targetTransform.position;
        return (targetPosition - requesterPosition).sqrMagnitude <= maxDistance * maxDistance;
    }

    private static bool HasLineOfSight(
        Transform requesterTransform,
        Transform targetTransform,
        ServerRequestValidationPolicy policy)
    {
        Vector3 origin = requesterTransform.position + Vector3.up * policy.RequesterEyeHeight;
        Vector3 target = targetTransform.position + Vector3.up * policy.TargetHeightOffset;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction / distance,
            distance,
            policy.LineOfSightMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        Transform nearestTransform = null;
        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.transform;
            if (hitTransform == null || hitTransform.IsChildOf(requesterTransform))
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestTransform = hitTransform;
        }

        return nearestTransform == null
            || nearestTransform == targetTransform
            || nearestTransform.IsChildOf(targetTransform);
    }

    private static bool TryConsumeRateLimit(
        NetworkRunner runner,
        PlayerRef requester,
        NetworkObject targetObject,
        int scope,
        ServerRequestValidationPolicy policy)
    {
        float refillRate = policy.RequestsPerSecond;
        if (refillRate <= 0f)
            return true;

        int capacity = policy.BurstCapacity;
        double now = Time.realtimeSinceStartupAsDouble;
        var key = new RateLimitKey(
            runner.GetInstanceID(),
            requester.RawEncoded,
            targetObject.Id.Raw,
            scope);

        if (!RateLimits.TryGetValue(key, out RateLimitState state))
        {
            state = new RateLimitState
            {
                Tokens = capacity,
                LastRefillTime = now,
                LastSeenTime = now
            };
            RateLimits.Add(key, state);
        }
        else
        {
            double elapsed = Math.Max(0d, now - state.LastRefillTime);
            state.Tokens = Math.Min(capacity, state.Tokens + elapsed * refillRate);
            state.LastRefillTime = now;
            state.LastSeenTime = now;
        }

        if (state.Tokens < 1d)
            return false;

        state.Tokens -= 1d;

        if (RateLimits.Count > RateLimitCleanupThreshold)
            RemoveStaleRateLimits(now);

        return true;
    }

    private static void RemoveStaleRateLimits(double now)
    {
        StaleRateLimitKeys.Clear();
        foreach (KeyValuePair<RateLimitKey, RateLimitState> entry in RateLimits)
        {
            if (now - entry.Value.LastSeenTime > StaleRateLimitSeconds)
                StaleRateLimitKeys.Add(entry.Key);
        }

        foreach (RateLimitKey key in StaleRateLimitKeys)
            RateLimits.Remove(key);
    }
}
