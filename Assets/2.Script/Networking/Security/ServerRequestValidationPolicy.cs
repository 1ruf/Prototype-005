using System;
using UnityEngine;

[Serializable]
public sealed class ServerRequestValidationPolicy
{
    [SerializeField, Min(0f)] private float maxDistance = 5f;
    [SerializeField] private bool requireLineOfSight;
    [SerializeField] private LayerMask lineOfSightMask = ~0;
    [SerializeField, Min(0f)] private float requesterEyeHeight = 1.4f;
    [SerializeField] private float targetHeightOffset = 0.5f;
    [SerializeField, Min(0f)] private float requestsPerSecond = 8f;
    [SerializeField, Min(1)] private int burstCapacity = 2;

    public float MaxDistance => Mathf.Max(0f, maxDistance);
    public bool RequireLineOfSight => requireLineOfSight;
    public LayerMask LineOfSightMask => lineOfSightMask;
    public float RequesterEyeHeight => Mathf.Max(0f, requesterEyeHeight);
    public float TargetHeightOffset => targetHeightOffset;
    public float RequestsPerSecond => Mathf.Max(0f, requestsPerSecond);
    public int BurstCapacity => Mathf.Max(1, burstCapacity);

    public ServerRequestValidationPolicy(
        float maxDistance,
        bool requireLineOfSight,
        LayerMask lineOfSightMask,
        float requesterEyeHeight,
        float targetHeightOffset,
        float requestsPerSecond,
        int burstCapacity)
    {
        this.maxDistance = maxDistance;
        this.requireLineOfSight = requireLineOfSight;
        this.lineOfSightMask = lineOfSightMask;
        this.requesterEyeHeight = requesterEyeHeight;
        this.targetHeightOffset = targetHeightOffset;
        this.requestsPerSecond = requestsPerSecond;
        this.burstCapacity = burstCapacity;
    }

    public static ServerRequestValidationPolicy CreateInteractionDefault()
    {
        return new ServerRequestValidationPolicy(
            maxDistance: 5f,
            requireLineOfSight: false,
            lineOfSightMask: Physics.DefaultRaycastLayers,
            requesterEyeHeight: 1.4f,
            targetHeightOffset: 0.5f,
            requestsPerSecond: 8f,
            burstCapacity: 2);
    }

    public static ServerRequestValidationPolicy CreateOwnerRequestDefault()
    {
        return new ServerRequestValidationPolicy(
            maxDistance: 2f,
            requireLineOfSight: false,
            lineOfSightMask: Physics.DefaultRaycastLayers,
            requesterEyeHeight: 1.4f,
            targetHeightOffset: 0.5f,
            requestsPerSecond: 6f,
            burstCapacity: 2);
    }
}
