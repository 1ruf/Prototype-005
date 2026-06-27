using System.Collections.Generic;

public static class PlayerRuntimeRegistry
{
    private static readonly List<PlayerMovement> players = new List<PlayerMovement>(8);

    public static IReadOnlyList<PlayerMovement> Players => players;

    public static void Register(PlayerMovement player)
    {
        if (player != null && !players.Contains(player))
            players.Add(player);
    }

    public static void Unregister(PlayerMovement player)
    {
        if (player != null)
            players.Remove(player);
    }
}
