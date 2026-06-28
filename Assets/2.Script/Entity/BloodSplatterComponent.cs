using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BloodSplatterComponent : MonoBehaviour, INetworkEntityComponent
{
    private enum DecalNormalAxis
    {
        UpY,
        ForwardZ
    }

    [Header("Prefab")]
    [SerializeField] private GameObject[] bloodPrefabs;
    [SerializeField] private DecalNormalAxis prefabNormalAxis = DecalNormalAxis.UpY;

    [Header("Surface")]
    [SerializeField] private LayerMask surfaceMask = (1 << 7) | (1 << 8);
    [SerializeField] private float floorProbeHeight = 1.1f;
    [SerializeField] private float floorProbeDistance = 3f;
    [SerializeField] private float wallProbeDistance = 2.2f;
    [SerializeField] private float surfaceOffset = 0.01f;

    [Header("Scale")]
    [SerializeField] private Vector2 floorSizeRange = new Vector2(0.22f, 0.75f);
    [SerializeField] private Vector2 wallSizeRange = new Vector2(0.12f, 0.42f);

    [Header("Burst")]
    [SerializeField] private int defaultCount = 10;
    [SerializeField] private float scatterRadius = 1.65f;
    [SerializeField, Range(0f, 1f)] private float wallChanceWhenNearWall = 0.42f;
    [SerializeField] private int maxDecals = 80;

    private readonly Queue<GameObject> decals = new Queue<GameObject>(80);
    private GameObject owner;
    private GameObject fallbackDecalPrefab;
    private Transform decalRoot;

    public GameObject Owner => owner != null ? owner : gameObject;

    private void Awake()
    {
        Initialize(ResolveOwner());
    }

    public void Initialize(GameObject entityOwner)
    {
        owner = entityOwner != null ? entityOwner : gameObject;
        EnsureRoot();
    }

    public void SpawnBlood()
    {
        SpawnBlood(defaultCount);
    }

    public void SpawnBlood(int count)
    {
        SpawnBlood(count, Owner.transform.position);
    }

    public void SpawnBlood(int count, Vector3 sourcePosition)
    {
        SpawnBlood(count, sourcePosition, Vector3.zero);
    }

    public void SpawnBlood(int count, Vector3 sourcePosition, Vector3 impulseDirection)
    {
        if (count <= 0)
            return;

        EnsureRoot();

        bool hasWall = TryFindWall(sourcePosition, impulseDirection, out RaycastHit wallHit);
        int clampedCount = Mathf.Clamp(count, 1, Mathf.Max(1, maxDecals));

        for (int i = 0; i < clampedCount; i++)
        {
            bool useWall = hasWall && Random.value < wallChanceWhenNearWall;
            if (useWall)
            {
                Vector3 wallOrigin = sourcePosition + Random.insideUnitSphere * scatterRadius;
                wallOrigin.y = Mathf.Max(sourcePosition.y + 0.25f, wallOrigin.y);
                Vector3 wallDirection = (wallHit.point - sourcePosition).normalized;

                if (Physics.Raycast(wallOrigin - wallDirection * 0.25f, wallDirection, out RaycastHit hit, wallProbeDistance + 0.5f, surfaceMask, QueryTriggerInteraction.Ignore))
                {
                    CreateDecal(hit, wallSizeRange);
                    continue;
                }

                CreateDecal(wallHit, wallSizeRange);
                continue;
            }

            Vector2 offset = Random.insideUnitCircle * scatterRadius;
            Vector3 floorOrigin = sourcePosition + new Vector3(offset.x, floorProbeHeight, offset.y);
            if (Physics.Raycast(floorOrigin, Vector3.down, out RaycastHit floorHit, floorProbeDistance + floorProbeHeight, surfaceMask, QueryTriggerInteraction.Ignore))
                CreateDecal(floorHit, floorSizeRange);
        }
    }

    public void SpawnBloodOnSurface(int count, Vector3 surfacePoint, Vector3 surfaceNormal)
    {
        if (count <= 0)
            return;

        EnsureRoot();

        Vector3 safeNormal = surfaceNormal.sqrMagnitude > 0.001f ? surfaceNormal.normalized : Vector3.up;
        Vector2 sizeRange = IsWallLike(safeNormal) ? wallSizeRange : floorSizeRange;
        int clampedCount = Mathf.Clamp(count, 1, Mathf.Max(1, maxDecals));

        for (int i = 0; i < clampedCount; i++)
        {
            Vector3 tangent = Vector3.Cross(safeNormal, Vector3.up);
            if (tangent.sqrMagnitude <= 0.001f)
                tangent = Vector3.Cross(safeNormal, Vector3.right);

            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(safeNormal, tangent).normalized;
            Vector2 offset = Random.insideUnitCircle * 0.08f;
            CreateDecal(surfacePoint + tangent * offset.x + bitangent * offset.y, safeNormal, sizeRange);
        }
    }

    public void ClearBlood()
    {
        while (decals.Count > 0)
        {
            GameObject decal = decals.Dequeue();
            if (decal != null)
                GameObjectPoolManager.Despawn(decal);
        }
    }

    private bool TryFindWall(Vector3 sourcePosition, Vector3 impulseDirection, out RaycastHit wallHit)
    {
        wallHit = default;

        Vector3 preferredDirection = impulseDirection;
        preferredDirection.y = 0f;

        if (preferredDirection.sqrMagnitude > 0.001f)
        {
            Vector3 origin = sourcePosition + Vector3.up * 0.65f;
            if (Physics.Raycast(origin, preferredDirection.normalized, out wallHit, wallProbeDistance, surfaceMask, QueryTriggerInteraction.Ignore))
                return IsWallLike(wallHit.normal);
        }

        const int probes = 8;
        float randomYaw = Random.Range(0f, 360f);
        for (int i = 0; i < probes; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, randomYaw + i * (360f / probes), 0f) * Vector3.forward;
            Vector3 origin = sourcePosition + Vector3.up * Random.Range(0.35f, 1.25f);
            if (Physics.Raycast(origin, direction, out wallHit, wallProbeDistance, surfaceMask, QueryTriggerInteraction.Ignore) && IsWallLike(wallHit.normal))
                return true;
        }

        return false;
    }

    private bool IsWallLike(Vector3 normal)
    {
        return Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) < 0.45f;
    }

    private void CreateDecal(RaycastHit hit, Vector2 sizeRange)
    {
        CreateDecal(hit.point, hit.normal, sizeRange);
    }

    private void CreateDecal(Vector3 point, Vector3 normal, Vector2 sizeRange)
    {
        GameObject prefab = GetRandomBloodPrefab();
        bool usesConfiguredPrefab = prefab != null;
        if (prefab == null)
            prefab = GetOrCreateFallbackDecalPrefab();

        GameObject decal = GameObjectPoolManager.Spawn(
            prefab,
            point + normal.normalized * surfaceOffset,
            CreateSurfaceRotation(normal, usesConfiguredPrefab),
            decalRoot);

        if (decal == null)
            return;

        decal.name = prefab.name;
        decal.transform.SetParent(decalRoot, true);
        decal.transform.position = point + normal.normalized * surfaceOffset;
        decal.transform.rotation = CreateSurfaceRotation(normal, usesConfiguredPrefab);

        float size = Random.Range(sizeRange.x, sizeRange.y);
        decal.transform.localScale = Vector3.one * size;

        Collider collider = decal.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;

        decals.Enqueue(decal);
        while (decals.Count > maxDecals)
        {
            GameObject old = decals.Dequeue();
            if (old != null)
                GameObjectPoolManager.Despawn(old);
        }
    }

    private void EnsureRoot()
    {
        if (decalRoot != null)
            return;

        GameObject root = new GameObject("BloodSplatterDecals");
        root.transform.SetParent(null, true);
        decalRoot = root.transform;
    }

    private GameObject GetOrCreateFallbackDecalPrefab()
    {
        if (fallbackDecalPrefab != null)
            return fallbackDecalPrefab;

        fallbackDecalPrefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fallbackDecalPrefab.name = "BloodSplatter";
        fallbackDecalPrefab.SetActive(false);

        Collider collider = fallbackDecalPrefab.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;

        EnsureRoot();
        fallbackDecalPrefab.transform.SetParent(decalRoot, false);
        return fallbackDecalPrefab;
    }

    private Quaternion CreateSurfaceRotation(Vector3 normal, bool usesPrefab)
    {
        Vector3 safeNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        DecalNormalAxis normalAxis = usesPrefab ? prefabNormalAxis : DecalNormalAxis.ForwardZ;
        Vector3 sourceAxis = normalAxis == DecalNormalAxis.UpY ? Vector3.up : Vector3.forward;

        Quaternion alignToSurface = Quaternion.FromToRotation(sourceAxis, safeNormal);
        Quaternion randomSurfaceSpin = Quaternion.AngleAxis(Random.Range(0f, 360f), safeNormal);
        return randomSurfaceSpin * alignToSurface;
    }

    private GameObject GetRandomBloodPrefab()
    {
        if (bloodPrefabs == null || bloodPrefabs.Length == 0)
            return null;

        int startIndex = Random.Range(0, bloodPrefabs.Length);
        for (int i = 0; i < bloodPrefabs.Length; i++)
        {
            GameObject prefab = bloodPrefabs[(startIndex + i) % bloodPrefabs.Length];
            if (prefab != null)
                return prefab;
        }

        return null;
    }

    private GameObject ResolveOwner()
    {
        PlayerMovement player = GetComponentInParent<PlayerMovement>();
        return player != null ? player.gameObject : gameObject;
    }
}
