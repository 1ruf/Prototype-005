using System.Collections.Generic;

public static class EnemyRuntimeRegistry
{
    private static readonly List<CSHEnemy> enemies = new List<CSHEnemy>(16);

    public static IReadOnlyList<CSHEnemy> Enemies => enemies;

    public static void Register(CSHEnemy enemy)
    {
        if (enemy != null && !enemies.Contains(enemy))
            enemies.Add(enemy);
    }

    public static void Unregister(CSHEnemy enemy)
    {
        if (enemy != null)
            enemies.Remove(enemy);
    }
}
