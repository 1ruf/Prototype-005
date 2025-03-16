using System.Collections;
using UnityEngine;

public class CSHEnemy : MonoBehaviour
{
    [SerializeField] private Transform target;  // 목표 (플레이어)
    [SerializeField] private Transform visual;  // 회전할 모델
    [SerializeField] private GameObject ui;
    [SerializeField] private float moveSpeed = 5f; // 이동 속도
    [SerializeField] private float rotationSpeed = 3f; // 회전 속도

    private Rigidbody rb;
    private Vector3 moveDirection; // 현재 이동 방향

    private void Awake()
    {
        ui.SetActive(false);
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // 물리 회전 방지
    }

    private void Start()
    {
        if (target != null)
        {
            moveDirection = (target.position - transform.position).normalized;
            moveDirection.y = 0; // Y축 이동 방지
        }
    }

    private void FixedUpdate()
    {
        PursueTarget();
    }

    private void PursueTarget()
    {
        if (target == null) return;

        Vector3 targetDirection = (target.position - transform.position).normalized;
        targetDirection.y = 0; // Y축 이동 방지

        moveDirection = Vector3.Lerp(moveDirection, targetDirection, rotationSpeed * Time.deltaTime).normalized;

        rb.linearVelocity = moveDirection * moveSpeed;

        if (moveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            visual.rotation = targetRotation;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            ui.SetActive(true);
            StartCoroutine(Exit());
        }
    }

    private IEnumerator Exit()
    {
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }
}
