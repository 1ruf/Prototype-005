using UnityEngine;

public class Enemy : MonoBehaviour
{
    [SerializeField] private Transform target;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        rb.AddForce(target.position-transform.position);
    }
}
