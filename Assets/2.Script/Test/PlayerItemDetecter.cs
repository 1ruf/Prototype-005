using System;
using UnityEngine;

public class PlayerItemDetecter : MonoBehaviour
{
    private ICollectable collectable;

    private void Update()
    {
        Collider[] collisions = Physics.OverlapSphere(transform.position, 1.5f);
        Check(collisions);
    }

    private void Check(Collider[] collisions)
    {
        foreach (var item in collisions)
        {
            if (item.gameObject.TryGetComponent<ICollectable>(out collectable))
            {
                collectable.Get();
            }
        }
    }
}
