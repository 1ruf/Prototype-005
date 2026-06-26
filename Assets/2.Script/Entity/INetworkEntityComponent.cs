using UnityEngine;

public interface INetworkEntityComponent
{
    GameObject Owner { get; }
    void Initialize(GameObject owner);
}
