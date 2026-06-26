using Fusion;
using UnityEngine;

public struct NetworkPlayerInput : INetworkInput
{
    public Vector2 Move;
    public Vector2 LookDelta;
    public NetworkBool Sprint;
}
