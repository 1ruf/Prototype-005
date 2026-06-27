using UnityEngine;

[CreateAssetMenu(fileName = "PlayerItemSO", menuName = "SO/Player/ItemSO")]
public class PlayerItemSO : ScriptableObject
{
    public string itemName;
    public string description; 

    public Vector3 localPosition;

    public bool useable; 

    public GameObject ItemPrefab; 
}
