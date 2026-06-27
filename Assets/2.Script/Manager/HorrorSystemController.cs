using UnityEngine;

public sealed class HorrorSystemController : GameControllerBase
{
    public bool IsChasing { get; private set; }

    public override GameControllerBase Init()
    {
        return this;
    }

    public void SetChase(bool value)
    {
        IsChasing = value;
    }

    public void JumpScare()
    {
        Debug.Log("워!");
    }
}
