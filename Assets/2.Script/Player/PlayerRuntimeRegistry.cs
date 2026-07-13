using System.Collections.Generic;
using UnityEngine;

public static class PlayerRuntimeRegistry
{
    private static readonly RuntimeEntityRegistry<PlayerMovement> registry = new(8);

    public static IReadOnlyList<PlayerMovement> Players => registry.Items;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        registry.Clear();
    }

    public static void Register(PlayerMovement player)
    {
        registry.Register(player);
    }

    public static void Unregister(PlayerMovement player)
    {
        registry.Unregister(player);
    }
}
