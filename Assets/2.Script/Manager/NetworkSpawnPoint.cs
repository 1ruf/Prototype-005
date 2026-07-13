using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum NetworkSpawnPointKind
{
    Player = 0,
    Enemy = 1
}

[DisallowMultipleComponent]
public sealed class NetworkSpawnPoint : MonoBehaviour
{
    [Tooltip("Determines which spawn service can discover this marker.")]
    [SerializeField] private NetworkSpawnPointKind kind;
    [Tooltip("Higher-priority markers are exhausted before lower-priority markers.")]
    [SerializeField] private int priority;
    [Tooltip("Relative deterministic selection weight among unused markers at the same priority. Zero disables this marker.")]
    [SerializeField, Min(0f)] private float weight = 1f;

    public NetworkSpawnPointKind Kind => kind;
    public int Priority => priority;
    public float Weight => Mathf.Max(0f, weight);
    public bool IsUsable => IsTransformUsable(transform) && isActiveAndEnabled && Weight > 0f;

    public static void Collect(NetworkSpawnPointKind requestedKind, List<NetworkSpawnPoint> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        NetworkSpawnPoint[] discovered = FindObjectsByType<NetworkSpawnPoint>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (NetworkSpawnPoint spawnPoint in discovered)
        {
            if (spawnPoint != null && spawnPoint.kind == requestedKind && spawnPoint.IsUsable)
                results.Add(spawnPoint);
        }

        results.Sort(CompareDeterministically);
    }

    public static NetworkSpawnPoint SelectWeighted(
        IReadOnlyList<NetworkSpawnPoint> orderedCandidates,
        HashSet<NetworkSpawnPoint> excluded,
        int deterministicSeed)
    {
        if (orderedCandidates == null || orderedCandidates.Count == 0)
            return null;

        int selectedPriority = int.MinValue;
        float totalWeight = 0f;
        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            NetworkSpawnPoint candidate = orderedCandidates[i];
            if (!IsSelectable(candidate, excluded))
                continue;

            if (selectedPriority == int.MinValue)
                selectedPriority = candidate.Priority;

            if (candidate.Priority != selectedPriority)
                break;

            totalWeight += candidate.Weight;
        }

        if (selectedPriority == int.MinValue || totalWeight <= 0f)
            return null;

        float selection = ToUnitInterval(deterministicSeed) * totalWeight;
        NetworkSpawnPoint lastEligible = null;
        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            NetworkSpawnPoint candidate = orderedCandidates[i];
            if (!IsSelectable(candidate, excluded))
                continue;

            if (candidate.Priority != selectedPriority)
                break;

            lastEligible = candidate;
            selection -= candidate.Weight;
            if (selection <= 0f)
                return candidate;
        }

        return lastEligible;
    }

    public static bool IsTransformUsable(Transform candidate)
    {
        return IsTransformAlive(candidate) && candidate.gameObject.activeInHierarchy;
    }

    public static bool IsTransformAlive(Transform candidate)
    {
        if (candidate == null || candidate.gameObject == null)
            return false;

        Scene scene = candidate.gameObject.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    private static bool IsSelectable(NetworkSpawnPoint candidate, HashSet<NetworkSpawnPoint> excluded)
    {
        return candidate != null
            && candidate.IsUsable
            && (excluded == null || !excluded.Contains(candidate));
    }

    private static int CompareDeterministically(NetworkSpawnPoint left, NetworkSpawnPoint right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        int priorityComparison = right.Priority.CompareTo(left.Priority);
        if (priorityComparison != 0)
            return priorityComparison;

        Scene leftScene = left.gameObject.scene;
        Scene rightScene = right.gameObject.scene;
        string leftSceneKey = string.IsNullOrEmpty(leftScene.path) ? leftScene.name : leftScene.path;
        string rightSceneKey = string.IsNullOrEmpty(rightScene.path) ? rightScene.name : rightScene.path;
        int sceneComparison = string.CompareOrdinal(leftSceneKey, rightSceneKey);
        if (sceneComparison != 0)
            return sceneComparison;

        int hierarchyComparison = string.CompareOrdinal(
            GetHierarchySortKey(left.transform),
            GetHierarchySortKey(right.transform));
        if (hierarchyComparison != 0)
            return hierarchyComparison;

        int nameComparison = string.CompareOrdinal(left.name, right.name);
        return nameComparison != 0
            ? nameComparison
            : left.GetInstanceID().CompareTo(right.GetInstanceID());
    }

    private static string GetHierarchySortKey(Transform target)
    {
        if (target == null)
            return string.Empty;

        string key = target.GetSiblingIndex().ToString("D6");
        Transform parent = target.parent;
        while (parent != null)
        {
            key = parent.GetSiblingIndex().ToString("D6") + "/" + key;
            parent = parent.parent;
        }

        return key;
    }

    private static float ToUnitInterval(int seed)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777216f;
        }
    }
}
