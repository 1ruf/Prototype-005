using UnityEngine;

[CreateAssetMenu(fileName = "PlayerItemSO", menuName = "SO/Player/ItemSO")]
public class PlayerItemSO : ScriptableObject
{
    [Tooltip("Network inventory id. Keep this unique and stable once used in a saved scene.")]
    public int itemId;

    public string itemName;
    public string description; 

    public Vector3 localPosition;

    public bool useable; 

    [Tooltip("Network/world prefab used when this item is dropped.")]
    public GameObject ItemPrefab;

    [Tooltip("Optional visual-only prefab shown in the player's right hand. Falls back to ItemPrefab.")]
    public GameObject HeldVisualPrefab;
}
