using UnityEngine;

[CreateAssetMenu(fileName = "PlayerItemSO", menuName = "SO/Player/ItemSO")]
public class PlayerItemSO : ScriptableObject
{
    public string name; //������ �̸�
    public string description; //������ ����

    public Vector3 localPosition;

    public bool useable; //��� ������ �������ΰ�?

    public GameObject ItemPrefab; //�� �������� prefab
}
