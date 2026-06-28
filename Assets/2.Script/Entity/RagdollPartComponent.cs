using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class RagdollPartComponent : MonoBehaviour
{
    [SerializeField] private Rigidbody partRigidbody;
    [SerializeField] private Collider partCollider;

    private RagdollEntityComponent owner;

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

        if (owner == null)
            owner = GetComponentInParent<RagdollEntityComponent>();
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

    private void OnCollisionEnter(Collision collision)
    {
        NotifySurfaceContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        NotifySurfaceExit(collision);
    }

    private void NotifySurfaceContact(Collision collision)
    {
        CacheComponents();

        if (owner == null || collision == null || collision.contactCount <= 0)
            return;

        ContactPoint contact = collision.GetContact(0);
        owner.NotifyRagdollSurfaceContact(this, contact.point, contact.normal, collision.gameObject.layer);
    }

    private void NotifySurfaceExit(Collision collision)
    {
        CacheComponents();

        if (owner == null || collision == null)
            return;

        owner.NotifyRagdollSurfaceExit(this, collision.gameObject.layer);
    }
}
