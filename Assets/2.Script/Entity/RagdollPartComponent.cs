using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class RagdollPartComponent : MonoBehaviour
{
    [SerializeField] private Rigidbody partRigidbody;
    [SerializeField] private Collider partCollider;

    public Rigidbody Rigidbody => partRigidbody;
    public Collider Collider => partCollider;

    private void Awake()
    {
        CacheComponents();
    }

    public void CacheComponents()
    {
        if (partRigidbody == null)
            partRigidbody = GetComponent<Rigidbody>();

        if (partCollider == null)
            partCollider = GetComponent<Collider>();
    }

    public void SetRagdollActive(bool active)
    {
        CacheComponents();

        if (partCollider != null)
            partCollider.enabled = active;

        if (partRigidbody == null)
            return;

        partRigidbody.isKinematic = !active;
        partRigidbody.detectCollisions = active;

        if (!active)
        {
            partRigidbody.linearVelocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
        }
    }
}
