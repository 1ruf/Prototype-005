using UnityEngine;
using UnityEngine.UI;

public class InventoryContainerView : MonoBehaviour
{
    [SerializeField] private Image itemImage;
    [SerializeField] private Outline outline;

    [Header("OutlineSetting")]
    [SerializeField] private Color defaultColor;
    [SerializeField] private Color highlightedColor;

    private PlayerItemSO _currentItem;
    private RectTransform _rectTransform;

    public PlayerItemSO CurrentItem => _currentItem;
    public bool HasItem => _currentItem != null;

    private void Awake()
    {
        CacheReferences();
        UpdateView();
        SetOutline(false);
    }

    public void SetItem(PlayerItemSO item)
    {
        CacheReferences();
        _currentItem = item;
        UpdateView();
    }

    private void UpdateView()
    {
        if (itemImage == null)
            return;

        itemImage.sprite = _currentItem != null ? _currentItem.ItemSprite : null;
        itemImage.enabled = _currentItem != null && _currentItem.ItemSprite != null;
        itemImage.preserveAspect = true;
    }

    public string GetCurrentItemName()
    {
        return _currentItem != null ? _currentItem.ItemName : string.Empty;
    }

    public void SetOutline(bool value)
    {
        CacheReferences();

        if (outline == null)
            return;

        outline.enabled = value;
        outline.effectColor = value ? highlightedColor : defaultColor;
    }

    public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera)
    {
        CacheReferences();
        return _rectTransform != null && RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, screenPoint, eventCamera);
    }

    private void CacheReferences()
    {
        if (_rectTransform == null)
            _rectTransform = transform as RectTransform;

        if (itemImage == null)
            itemImage = GetComponentInChildren<Image>(true);

        if (outline == null)
            outline = GetComponentInChildren<Outline>(true);
    }
}
