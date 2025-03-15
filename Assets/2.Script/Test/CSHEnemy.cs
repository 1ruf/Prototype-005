using System.Collections;
using UnityEngine;

public class CSHEnemy : MonoBehaviour
{
    [SerializeField] private Transform target;  // ��ǥ (�÷��̾�)
    [SerializeField] private Transform visual;  // ȸ���� ��
    [SerializeField] private GameObject ui;
    [SerializeField] private float moveSpeed = 5f; // �̵� �ӵ�
    [SerializeField] private float rotationSpeed = 3f; // ȸ�� �ӵ�

    private Rigidbody rb;
    private Vector3 moveDirection; // ���� �̵� ����

    private void Awake()
    {
        ui.SetActive(false);
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ���� ȸ�� ����
    }

    private void Start()
    {
        if (target != null)
        {
            moveDirection = (target.position - transform.position).normalized;
            moveDirection.y = 0; // Y�� �̵� ����
        }
    }

    private void FixedUpdate()
    {
        PursueTarget();
    }

    private void PursueTarget()
    {
        if (target == null) return;

        // ��ǥ ���� ���
        Vector3 targetDirection = (target.position - transform.position).normalized;
        targetDirection.y = 0; // Y�� �̵� ����

        // �̵� ������ ���������� �÷��̾� �������� ����
        moveDirection = Vector3.Lerp(moveDirection, targetDirection, rotationSpeed * Time.deltaTime).normalized;

        // �̵� ���� (�ε巯�� �ӵ� ����)
        rb.linearVelocity = moveDirection * moveSpeed;

        // �ε巯�� ȸ�� ����
        if (moveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            visual.rotation = Quaternion.Slerp(visual.rotation, targetRotation, rotationSpeed * Time.deltaTime);
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
