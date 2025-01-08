using UnityEngine;

[CreateAssetMenu(fileName = "PlayerItemSO", menuName = "SO/Player/ItemSO")]
public class PlayerItemSO : ScriptableObject
{
    public string name; //아이템 이름
    public string description; //아이템 설명

    public Vector3 localPosition;

    public bool useable; //사용 가능한 아이템인가?

    public GameObject ItemPrefab; //이 아이템의 prefab
}
