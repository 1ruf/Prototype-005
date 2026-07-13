using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Reconciles the server-owned enemy population independently from connection callbacks.
/// </summary>
public sealed class NetworkEnemySpawnDirector
{
    private readonly NetworkObject enemyPrefab;
    private readonly List<Transform> spawnPoints;
    private readonly Func<int, int> targetCountResolver;
    private readonly List<NetworkObject> spawnedEnemies = new();
    private readonly HashSet<Transform> usedLegacySpawnPoints = new();
    private readonly HashSet<NetworkSpawnPoint> usedMarkerSpawnPoints = new();
    private readonly List<NetworkSpawnPoint> markerSpawnPoints = new();
    private int spawnSelectionSequence;

    public NetworkEnemySpawnDirector(
        NetworkObject enemyPrefab,
        List<Transform> spawnPoints,
        Func<int, int> targetCountResolver)
    {
        this.enemyPrefab = enemyPrefab;
        this.spawnPoints = spawnPoints;
        this.targetCountResolver = targetCountResolver;
    }

    public void Reconcile(NetworkRunner runner, int playerCount)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer || enemyPrefab == null)
            return;

        spawnedEnemies.RemoveAll(enemy => enemy == null);
        int targetEnemyCount = targetCountResolver != null
            ? Mathf.Max(0, targetCountResolver(playerCount))
            : 0;

        while (spawnedEnemies.Count < targetEnemyCount)
        {
            Transform spawnPoint = GetNextSpawnPoint();
            Vector3 position = spawnPoint != null ? spawnPoint.position : GetFallbackSpawnPosition();
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            spawnedEnemies.Add(runner.Spawn(enemyPrefab, position, rotation));
        }

        while (spawnedEnemies.Count > targetEnemyCount)
        {
            int lastIndex = spawnedEnemies.Count - 1;
            NetworkObject enemy = spawnedEnemies[lastIndex];
            spawnedEnemies.RemoveAt(lastIndex);
            if (enemy != null)
                runner.Despawn(enemy);
        }
    }

    public void SpawnNear(NetworkRunner runner, Vector3 origin)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer || enemyPrefab == null)
            return;

        Transform spawnPoint = GetNextSpawnPoint();
        if (spawnPoint != null)
        {
            spawnedEnemies.Add(runner.Spawn(enemyPrefab, spawnPoint.position, spawnPoint.rotation));
            return;
        }

        Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
        if (randomDirection == Vector2.zero)
            randomDirection = Vector2.right;

        Vector2 offset = randomDirection * UnityEngine.Random.Range(8f, 16f);
        Vector3 position = origin + new Vector3(offset.x, 0f, offset.y);
        spawnedEnemies.Add(runner.Spawn(enemyPrefab, position, Quaternion.identity));
    }

    public void ClearTracking()
    {
        spawnedEnemies.Clear();
        usedLegacySpawnPoints.Clear();
        usedMarkerSpawnPoints.Clear();
        markerSpawnPoints.Clear();
        spawnSelectionSequence = 0;
    }

    private Transform GetNextSpawnPoint()
    {
        int selectionSeed = spawnSelectionSequence++;
        RefreshSpawnPointCandidates();

        if (!HasUnusedLegacySpawnPoint() && !HasUnusedMarkerSpawnPoint())
        {
            usedLegacySpawnPoints.Clear();
            usedMarkerSpawnPoints.Clear();
        }

        Transform legacySpawnPoint = GetUnusedLegacySpawnPoint(selectionSeed);
        if (legacySpawnPoint != null)
            return legacySpawnPoint;

        NetworkSpawnPoint marker = NetworkSpawnPoint.SelectWeighted(
            markerSpawnPoints,
            usedMarkerSpawnPoints,
            selectionSeed);
        if (marker == null)
            return null;

        usedMarkerSpawnPoints.Add(marker);
        return marker.transform;
    }

    private Transform GetUnusedLegacySpawnPoint(int startIndex)
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return null;

        int safeStartIndex = GetPositiveIndex(startIndex, spawnPoints.Count);
        for (int offset = 0; offset < spawnPoints.Count; offset++)
        {
            int index = (safeStartIndex + offset) % spawnPoints.Count;
            Transform spawnPoint = spawnPoints[index];
            if (!NetworkSpawnPoint.IsTransformUsable(spawnPoint)
                || usedLegacySpawnPoints.Contains(spawnPoint))
                continue;

            usedLegacySpawnPoints.Add(spawnPoint);
            return spawnPoint;
        }

        return null;
    }

    private void RefreshSpawnPointCandidates()
    {
        RemoveStaleLegacySpawnPoints();
        NetworkSpawnPoint.Collect(NetworkSpawnPointKind.Enemy, markerSpawnPoints);

        if (spawnPoints != null && spawnPoints.Count > 0)
        {
            markerSpawnPoints.RemoveAll(marker =>
                marker == null || spawnPoints.Contains(marker.transform));
        }

        usedLegacySpawnPoints.RemoveWhere(spawnPoint =>
            spawnPoint == null || spawnPoints == null || !spawnPoints.Contains(spawnPoint));
        usedMarkerSpawnPoints.RemoveWhere(spawnPoint =>
            spawnPoint == null || !markerSpawnPoints.Contains(spawnPoint));
    }

    private void RemoveStaleLegacySpawnPoints()
    {
        if (spawnPoints == null)
            return;

        for (int i = spawnPoints.Count - 1; i >= 0; i--)
        {
            if (!NetworkSpawnPoint.IsTransformAlive(spawnPoints[i]))
                spawnPoints.RemoveAt(i);
        }
    }

    private bool HasUnusedLegacySpawnPoint()
    {
        if (spawnPoints == null)
            return false;

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Transform spawnPoint = spawnPoints[i];
            if (NetworkSpawnPoint.IsTransformUsable(spawnPoint)
                && !usedLegacySpawnPoints.Contains(spawnPoint))
                return true;
        }

        return false;
    }

    private bool HasUnusedMarkerSpawnPoint()
    {
        foreach (NetworkSpawnPoint spawnPoint in markerSpawnPoints)
        {
            if (spawnPoint != null
                && spawnPoint.IsUsable
                && !usedMarkerSpawnPoints.Contains(spawnPoint))
                return true;
        }

        return false;
    }

    private static int GetPositiveIndex(int value, int count)
    {
        if (count <= 0)
            return 0;

        int remainder = value % count;
        return remainder < 0 ? remainder + count : remainder;
    }

    private static Vector3 GetFallbackSpawnPosition()
    {
        Vector2 randomPosition = UnityEngine.Random.insideUnitCircle * 12f;
        return new Vector3(randomPosition.x, 2.27f, randomPosition.y);
    }
}
