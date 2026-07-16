using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared, session-local patrol coverage. Enemies reserve destinations before travelling so a group spreads
/// across the map, then record the cells they actually visit.
/// </summary>
public static class EnemyPatrolCoverageRegistry
{
    private const float CellSize = 8f;
    private const float ReservationLifetime = 18f;

    private struct Reservation
    {
        public CSHEnemy Enemy;
        public float ExpiresAt;
    }

    private static readonly Dictionary<Vector2Int, float> visitedAt = new Dictionary<Vector2Int, float>();
    private static readonly Dictionary<Vector2Int, Reservation> reservations = new Dictionary<Vector2Int, Reservation>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        visitedAt.Clear();
        reservations.Clear();
    }

    public static float GetDestinationScore(CSHEnemy enemy, Vector3 from, Vector3 candidate)
    {
        Vector2Int cell = ToCell(candidate);
        float now = Time.time;
        float score = FlatDistance(from, candidate) * 0.12f;

        if (!visitedAt.TryGetValue(cell, out float lastVisited))
            score += 100f;
        else
            score += Mathf.Min(60f, (now - lastVisited) * 0.5f);

        if (reservations.TryGetValue(cell, out Reservation reservation)
            && reservation.ExpiresAt > now
            && reservation.Enemy != null
            && reservation.Enemy != enemy)
            score -= 200f;

        return score + Random.Range(0f, 2f);
    }

    public static void ReserveDestination(CSHEnemy enemy, Vector3 destination)
    {
        if (enemy == null)
            return;

        reservations[ToCell(destination)] = new Reservation
        {
            Enemy = enemy,
            ExpiresAt = Time.time + ReservationLifetime
        };
    }

    public static void RecordPatrolPosition(Vector3 position)
    {
        visitedAt[ToCell(position)] = Time.time;
    }

    private static Vector2Int ToCell(Vector3 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
