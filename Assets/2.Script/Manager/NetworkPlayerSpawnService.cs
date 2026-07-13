using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Owns the server-side player object lifecycle and player-to-object registration.
/// </summary>
public sealed class NetworkPlayerSpawnService : NetworkRunnerCallbacksAdapter
{
    private readonly NetworkObject playerPrefab;
    private readonly List<Transform> spawnPoints;
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new();
    // Fusion can dispatch the same join notification through more than one callback path
    // (notably after an editor domain reload while a runner is still alive).  Spawning is
    // authoritative and must be idempotent per PlayerRef.
    private readonly HashSet<PlayerRef> pendingSpawnRequests = new();
    private readonly HashSet<Transform> usedLegacySpawnPoints = new();
    private readonly HashSet<NetworkSpawnPoint> usedMarkerSpawnPoints = new();
    private readonly List<NetworkSpawnPoint> markerSpawnPoints = new();

    public NetworkPlayerSpawnService(NetworkObject playerPrefab, List<Transform> spawnPoints)
    {
        this.playerPrefab = playerPrefab;
        this.spawnPoints = spawnPoints;
    }

    public event Action<NetworkRunner, int> PlayerCountChanged;

    public int PlayerCount => spawnedPlayers.Count;

    public override void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer || playerPrefab == null)
            return;

        if (TryTrackExistingPlayerObject(runner, player))
            return;

        // Protect the narrow window inside Spawn as well: callbacks may be delivered again
        // before SetPlayerObject has completed.
        if (!pendingSpawnRequests.Add(player))
            return;

        try
        {
            Transform spawnPoint = GetSpawnPoint(player);
            Vector3 position = spawnPoint != null
                ? spawnPoint.position
                : new Vector3(player.RawEncoded * 2f, 2.27f, 0f);
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            NetworkObject playerObject = runner.Spawn(playerPrefab, position, rotation, player);
            if (playerObject == null)
            {
                Debug.LogError($"Failed to spawn a player object for {player}.");
                return;
            }

            spawnedPlayers[player] = playerObject;
            runner.SetPlayerObject(player, playerObject);
            PlayerCountChanged?.Invoke(runner, spawnedPlayers.Count);
        }
        finally
        {
            pendingSpawnRequests.Remove(player);
        }
    }

    public override void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
            return;

        if (!spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
            playerObject = runner.GetPlayerObject(player);

        spawnedPlayers.Remove(player);
        pendingSpawnRequests.Remove(player);
        runner.SetPlayerObject(player, null);

        if (playerObject != null)
            runner.Despawn(playerObject);

        PlayerCountChanged?.Invoke(runner, spawnedPlayers.Count);
    }

    public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        ClearTracking();
    }

    public void ClearTracking()
    {
        spawnedPlayers.Clear();
        pendingSpawnRequests.Clear();
        usedLegacySpawnPoints.Clear();
        usedMarkerSpawnPoints.Clear();
        markerSpawnPoints.Clear();
    }

    private Transform GetSpawnPoint(PlayerRef player)
    {
        RefreshSpawnPointCandidates();

        if (!HasUnusedLegacySpawnPoint() && !HasUnusedMarkerSpawnPoint())
        {
            usedLegacySpawnPoints.Clear();
            usedMarkerSpawnPoints.Clear();
        }

        Transform legacySpawnPoint = GetUnusedLegacySpawnPoint(player.RawEncoded);
        if (legacySpawnPoint != null)
            return legacySpawnPoint;

        NetworkSpawnPoint marker = NetworkSpawnPoint.SelectWeighted(
            markerSpawnPoints,
            usedMarkerSpawnPoints,
            player.RawEncoded);
        if (marker == null)
            return null;

        usedMarkerSpawnPoints.Add(marker);
        return marker.transform;
    }

    private bool TryTrackExistingPlayerObject(NetworkRunner runner, PlayerRef player)
    {
        if (spawnedPlayers.TryGetValue(player, out NetworkObject trackedPlayer))
        {
            if (trackedPlayer != null)
            {
                if (runner.GetPlayerObject(player) != trackedPlayer)
                    runner.SetPlayerObject(player, trackedPlayer);

                return true;
            }

            spawnedPlayers.Remove(player);
        }

        NetworkObject registeredPlayer = runner.GetPlayerObject(player);
        if (registeredPlayer == null)
            return false;

        spawnedPlayers[player] = registeredPlayer;
        return true;
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
        NetworkSpawnPoint.Collect(NetworkSpawnPointKind.Player, markerSpawnPoints);

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
}
