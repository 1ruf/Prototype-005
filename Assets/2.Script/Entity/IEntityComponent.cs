using UnityEngine;

public interface IEntityComponent
{
    GameObject Owner { get; }
    void Initialize(GameObject owner);
}
