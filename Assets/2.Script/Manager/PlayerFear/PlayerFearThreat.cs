using UnityEngine;

public readonly struct PlayerFearThreat
{
    public PlayerFearThreat(CSHEnemy enemy, Vector3 position, float distance, bool isChasingLocalPlayer)
    {
        Enemy = enemy;
        Position = position;
        Distance = distance;
        IsChasingLocalPlayer = isChasingLocalPlayer;
    }

    public CSHEnemy Enemy { get; }
    public Vector3 Position { get; }
    public float Distance { get; }
    public bool IsChasingLocalPlayer { get; }
}
