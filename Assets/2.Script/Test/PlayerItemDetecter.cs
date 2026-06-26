using UnityEngine;

public class PlayerItemDetecter : MonoBehaviour
{
    private ICollectable collectable;
    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponentInParent<PlayerMovement>();
    }

    private void Update()
    {
        if (playerMovement != null && !playerMovement.IsLocalNetworkPlayer)
            return;

        Collider[] collisions = Physics.OverlapSphere(transform.position, 1.5f);
        Check(collisions);
    }

    private void Check(Collider[] collisions)
    {
        foreach (Collider item in collisions)
        {
            if (item.gameObject.TryGetComponent(out collectable))
                collectable.Get();
        }
    }
}
