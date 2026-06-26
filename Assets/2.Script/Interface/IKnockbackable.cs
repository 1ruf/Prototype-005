using UnityEngine;

public interface IKnockbackable
{
    void ApplyKnockback(Vector3 direction, float force, float upwardForce);
    void ApplyKnockbackFrom(Vector3 sourcePosition, float force, float upwardForce);
}
