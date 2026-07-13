using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class InventoryViewController : MonoBehaviour
{
    [SerializeField] private InventoryContainerView[] slots;
    [SerializeField] private KeyCode tooltipKey = KeyCode.LeftAlt;
    [SerializeField] private KeyCode tooltipAlternateKey = KeyCode.RightAlt;
    [SerializeField] private bool unlockCursorWhileTooltipKey = true;
    [SerializeField] private Vector2 tooltipOffset = new Vector2(12f, -12f);
    [SerializeField] private Color tooltipBackgroundColor = new Color(0f, 0f, 0f, 0.82f);
    [SerializeField] private Color tooltipTextColor = Color.white;
    [SerializeField] private Vector2 tooltipPadding = new Vector2(18f, 10f);
    [SerializeField] private float localInventorySearchInterval = 0.25f;

    private NetworkInventory localInventory;
    private Canvas rootCanvas;
    private RectTransform canvasRectTransform;
    private RectTransform tooltipRectTransform;
    private TextMeshProUGUI tooltipText;
    private float nextInventorySearchTime;
    private bool cursorOverrideActive;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;

    private void Awake()
    {
        CacheSlots();
        CacheCanvas();
        EnsureTooltip();
        RefreshView();
    }

    private void OnEnable()
    {
        BindLocalInventory(FindLocalInventory());
        RefreshView();
    }

    private void OnDisable()
    {
        BindLocalInventory(null);
        SetTooltipCursorOverride(false);
        HideTooltip();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextInventorySearchTime)
        {
            nextInventorySearchTime = Time.unscaledTime + localInventorySearchInterval;
            NetworkInventory foundInventory = FindLocalInventory();
            if (foundInventory != localInventory)
                BindLocalInventory(foundInventory);
        }

        RefreshView();
        SetTooltipCursorOverride(IsTooltipKeyHeld());
        UpdateTooltip();
    }

    private void BindLocalInventory(NetworkInventory inventory)
    {
        if (localInventory == inventory)
            return;

        if (localInventory != null)
            localInventory.InventoryChanged -= RefreshView;

        localInventory = inventory;

        if (localInventory != null)
            localInventory.InventoryChanged += RefreshView;

        RefreshView();
    }

    private void RefreshView()
    {
        CacheSlots();

        int heldSlotIndex = localInventory != null ? localInventory.HighlightedSlotIndex : -1;
        for (int i = 0; i < slots.Length; i++)
        {
            PlayerItemSO item = null;
            if (localInventory != null && localInventory.TryGetSlot(i, out int itemId, out int count) && count > 0)
                InventoryItemRegistry.TryGet(itemId, out item);

            if (slots[i] == null)
                continue;

            slots[i].SetItem(item);
            slots[i].SetOutline(item != null && i == heldSlotIndex);
        }
    }

    private void UpdateTooltip()
    {
        if (!IsTooltipKeyHeld())
        {
            HideTooltip();
            return;
        }

        InventoryContainerView hoveredSlot = GetHoveredSlot(Input.mousePosition);
        if (hoveredSlot == null || !hoveredSlot.HasItem)
        {
            HideTooltip();
            return;
        }

        string itemName = hoveredSlot.GetCurrentItemName();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            HideTooltip();
            return;
        }

        ShowTooltip(itemName, Input.mousePosition);
    }

    private bool IsTooltipKeyHeld()
    {
        return Input.GetKey(tooltipKey) || Input.GetKey(tooltipAlternateKey);
    }

    private void SetTooltipCursorOverride(bool active)
    {
        if (!unlockCursorWhileTooltipKey)
            active = false;

        if (cursorOverrideActive == active)
            return;

        cursorOverrideActive = active;

        if (active)
        {
            previousCursorLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisible;
    }

    private InventoryContainerView GetHoveredSlot(Vector2 screenPoint)
    {
        CacheCanvas();
        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;

        for (int i = 0; i < slots.Length; i++)
        {
            InventoryContainerView slot = slots[i];
            if (slot != null && slot.ContainsScreenPoint(screenPoint, eventCamera))
                return slot;
        }

        return null;
    }

    private void ShowTooltip(string itemName, Vector2 screenPoint)
    {
        EnsureTooltip();
        if (tooltipRectTransform == null || tooltipText == null || canvasRectTransform == null)
            return;

        tooltipText.text = itemName;
        tooltipText.ForceMeshUpdate();

        Vector2 preferredSize = tooltipText.GetPreferredValues(itemName);
        tooltipRectTransform.sizeDelta = preferredSize + tooltipPadding * 2f;

        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTransform, screenPoint, eventCamera, out Vector2 localPoint))
            tooltipRectTransform.anchoredPosition = ClampTooltipPosition(localPoint + tooltipOffset);

        tooltipRectTransform.gameObject.SetActive(true);
    }

    private Vector2 ClampTooltipPosition(Vector2 anchoredPosition)
    {
        Rect canvasRect = canvasRectTransform.rect;
        Vector2 size = tooltipRectTransform.sizeDelta;

        float minX = canvasRect.xMin;
        float maxX = canvasRect.xMax - size.x;
        float minY = canvasRect.yMin + size.y;
        float maxY = canvasRect.yMax;

        return new Vector2(
            Mathf.Clamp(anchoredPosition.x, minX, maxX),
            Mathf.Clamp(anchoredPosition.y, minY, maxY));
    }

    private void HideTooltip()
    {
        if (tooltipRectTransform != null)
            tooltipRectTransform.gameObject.SetActive(false);
    }

    private void CacheSlots()
    {
        if (slots == null || slots.Length == 0)
            slots = GetComponentsInChildren<InventoryContainerView>(true);
    }

    private void CacheCanvas()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        if (rootCanvas != null && canvasRectTransform == null)
            canvasRectTransform = rootCanvas.transform as RectTransform;
    }

    private void EnsureTooltip()
    {
        CacheCanvas();
        if (tooltipRectTransform != null)
            return;

        Transform parent = rootCanvas != null ? rootCanvas.transform : transform;
        GameObject tooltipObject = new GameObject("InventoryItemTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tooltipObject.transform.SetParent(parent, false);
        tooltipObject.transform.SetAsLastSibling();

        tooltipRectTransform = tooltipObject.GetComponent<RectTransform>();
        tooltipRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRectTransform.pivot = new Vector2(0f, 1f);

        Image background = tooltipObject.GetComponent<Image>();
        background.color = tooltipBackgroundColor;
        background.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(tooltipObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = tooltipPadding;
        textRect.offsetMax = -tooltipPadding;

        tooltipText = textObject.GetComponent<TextMeshProUGUI>();
        tooltipText.color = tooltipTextColor;
        tooltipText.fontSize = 18f;
        tooltipText.raycastTarget = false;
        tooltipText.alignment = TextAlignmentOptions.MidlineLeft;
        tooltipText.textWrappingMode = TextWrappingModes.NoWrap;

        HideTooltip();
    }

    private static NetworkInventory FindLocalInventory()
    {
        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player == null || !player.IsLocalNetworkPlayer)
                continue;

            NetworkInventory inventory = player.GetComponent<NetworkInventory>();
            if (inventory == null)
                inventory = player.GetComponentInChildren<NetworkInventory>(true);

            if (inventory != null)
                return inventory;
        }

        return null;
    }
}
