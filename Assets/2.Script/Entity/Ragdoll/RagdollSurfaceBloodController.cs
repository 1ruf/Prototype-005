using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns ragdoll surface-contact probing, re-entry suppression, and blood presentation.
/// Network event sequencing remains in RagdollEntityComponent.
/// </summary>
[DisallowMultipleComponent]
public sealed class RagdollSurfaceBloodController : MonoBehaviour
{
    private readonly HashSet<RagdollPartComponent> bloodSplatteredSurfaceParts = new();
    private readonly Dictionary<RagdollPartComponent, float> surfaceExitTimes = new();
    private readonly Collider[] surfaceContactBuffer = new Collider[8];

    private RagdollEntityComponent coordinator;
    private RagdollPartComponent[] parts;
    private BloodSplatterComponent bloodSplatter;
    private int bloodCountOnSurfaceContact;
    private float surfaceReentryDelay;
    private float surfaceContactProbeRadius;
    private LayerMask surfaceMask;

    public void Configure(
        RagdollEntityComponent ownerCoordinator,
        RagdollPartComponent[] configuredParts,
        BloodSplatterComponent configuredBloodSplatter,
        int configuredBloodCountOnSurfaceContact,
        float configuredSurfaceReentryDelay,
        float configuredSurfaceContactProbeRadius,
        LayerMask configuredSurfaceMask)
    {
        coordinator = ownerCoordinator;
        if (configuredParts != null && configuredParts.Length > 0)
            parts = configuredParts;
        if (configuredBloodSplatter != null)
            bloodSplatter = configuredBloodSplatter;

        bloodCountOnSurfaceContact = configuredBloodCountOnSurfaceContact;
        surfaceReentryDelay = configuredSurfaceReentryDelay;
        surfaceContactProbeRadius = configuredSurfaceContactProbeRadius;
        surfaceMask = configuredSurfaceMask;
        ResolveReferences();
    }

    public void TickSurfaceContacts()
    {
        if (!CanProcessSurfaceBlood())
            return;

        ResolveReferences();
        if (parts == null || parts.Length == 0)
            return;

        float radius = Mathf.Max(0.01f, surfaceContactProbeRadius);
        for (int i = 0; i < parts.Length; i++)
        {
            RagdollPartComponent part = parts[i];
            if (part != null
                && bloodSplatteredSurfaceParts.Contains(part)
                && !surfaceExitTimes.ContainsKey(part)
                && !IsPartNearBloodSurface(part))
            {
                surfaceExitTimes[part] = Time.time;
            }

            if (!CanSpawnSurfaceBloodForPart(part))
                continue;

            Vector3 partPosition = part.transform.position;
            int hitCount = Physics.OverlapSphereNonAlloc(
                partPosition,
                radius,
                surfaceContactBuffer,
                surfaceMask,
                QueryTriggerInteraction.Ignore);

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Collider surface = surfaceContactBuffer[hitIndex];
                if (surface == null || part.Collider == surface)
                    continue;

                Vector3 contactPoint = surface.ClosestPoint(partPosition);
                Vector3 normal = partPosition - contactPoint;
                if (normal.sqrMagnitude <= 0.0001f)
                    normal = Vector3.up;

                SpawnSurfaceBloodForPart(part, contactPoint, normal.normalized);
                break;
            }
        }
    }

    public void NotifySurfaceContact(
        RagdollPartComponent part,
        Vector3 contactPoint,
        Vector3 contactNormal,
        int surfaceLayer)
    {
        if (!CanSpawnSurfaceBloodForPart(part) || !ContainsLayer(surfaceLayer))
            return;

        SpawnSurfaceBloodForPart(part, contactPoint, contactNormal);
    }

    public void NotifySurfaceExit(RagdollPartComponent part, int surfaceLayer)
    {
        if (part == null || !ContainsLayer(surfaceLayer))
            return;

        surfaceExitTimes[part] = Time.time;
    }

    public void SpawnBlood(int count, Vector3 sourcePosition, Vector3 impulseDirection)
    {
        if (count <= 0)
            return;

        ResolveReferences();
        bloodSplatter?.SpawnBlood(count, sourcePosition, impulseDirection);
    }

    public void ResetSurfaceState()
    {
        bloodSplatteredSurfaceParts.Clear();
        surfaceExitTimes.Clear();
    }

    private bool CanProcessSurfaceBlood()
    {
        return coordinator != null
            && coordinator.IsDead
            && coordinator.IsRagdollEnabled
            && bloodCountOnSurfaceContact > 0;
    }

    private bool CanSpawnSurfaceBloodForPart(RagdollPartComponent part)
    {
        if (part == null || !CanProcessSurfaceBlood())
            return false;

        if (bloodSplatteredSurfaceParts.Contains(part))
        {
            if (!surfaceExitTimes.TryGetValue(part, out float exitTime))
                return false;

            if (Time.time - exitTime < surfaceReentryDelay)
                return false;

            bloodSplatteredSurfaceParts.Remove(part);
            surfaceExitTimes.Remove(part);
        }

        return true;
    }

    private bool IsPartNearBloodSurface(RagdollPartComponent part)
    {
        if (part == null)
            return false;

        int hitCount = Physics.OverlapSphereNonAlloc(
            part.transform.position,
            Mathf.Max(0.01f, surfaceContactProbeRadius),
            surfaceContactBuffer,
            surfaceMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider surface = surfaceContactBuffer[i];
            if (surface != null && surface != part.Collider)
                return true;
        }

        return false;
    }

    private void SpawnSurfaceBloodForPart(
        RagdollPartComponent part,
        Vector3 contactPoint,
        Vector3 contactNormal)
    {
        ResolveReferences();
        if (bloodSplatter == null)
            return;

        bloodSplatteredSurfaceParts.Add(part);
        bloodSplatter.SpawnBloodOnSurface(
            bloodCountOnSurfaceContact,
            contactPoint,
            contactNormal);
    }

    private bool ContainsLayer(int layer)
    {
        return layer >= 0 && layer < 32 && (surfaceMask.value & (1 << layer)) != 0;
    }

    private void ResolveReferences()
    {
        if (coordinator == null)
            coordinator = GetComponentInParent<RagdollEntityComponent>();

        GameObject root = coordinator != null ? coordinator.Owner : gameObject;
        if ((parts == null || parts.Length == 0) && root != null)
            parts = root.GetComponentsInChildren<RagdollPartComponent>(true);

        if (bloodSplatter == null && root != null)
            bloodSplatter = root.GetComponentInChildren<BloodSplatterComponent>(true);
    }
}
