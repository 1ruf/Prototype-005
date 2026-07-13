using System.Collections.Generic;
using UnityEngine;

public static class EnemyRuntimeRegistry
{
    private static readonly RuntimeEntityRegistry<CSHEnemy> registry = new(16);

    public static IReadOnlyList<CSHEnemy> Enemies => registry.Items;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        registry.Clear();
    }

    public static void Register(CSHEnemy enemy)
    {
        registry.Register(enemy);
    }

    public static void Unregister(CSHEnemy enemy)
    {
        registry.Unregister(enemy);
    }
}
