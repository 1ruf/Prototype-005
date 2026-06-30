using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EmoteWheelController : MonoBehaviour
{
    [Serializable]
    public sealed class EmoteCommand
    {
        public string label;
        public Sprite icon;
        public UnityEvent Executed;
    }

    [SerializeField] private RectTransform wheelRoot;
    [SerializeField] private Sprite sectorSprite;
    [SerializeField] private List<EmoteCommand> commands = new List<EmoteCommand>();
    [SerializeField] private float innerDeadZone = 42f;
    [SerializeField] private float labelRadius = 165f;
    [SerializeField] private float highlightRotateDuration = 0.045f;
    [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.82f);
    [SerializeField] private Color textColor = Color.white;

    private readonly List<Graphic> commandGraphics = new List<Graphic>();
    private CanvasGroup canvasGroup;
    private RectTransform generatedRoot;
    private RectTransform highlightRect;
    private Image highlightImage;
    private Tween highlightRotateTween;
    private int selectedIndex = -1;
    private bool wasOpen;
    private bool cursorReleaseActive;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;

    public static bool IsOpen { get; private set; }
    public static bool IsBlockingGameplayInput => IsOpen || IsEmoteInputActive || IsCursorReleaseKeyPressed;
    public static event Action<int> CommandSelected;

    private static bool IsEmoteInputActive => !IsEmoteBlockedByHiding && IsEmoteKeyPressed;

    private static bool IsEmoteKeyPressed => Keyboard.current != null && Keyboard.current.gKey.isPressed;

    private static bool IsEmoteBlockedByHiding => IsLocalPlayerHidingInteractionBlocked();

    private static bool IsCursorReleaseKeyPressed =>
        Keyboard.current != null &&
        ((Keyboard.current.leftAltKey != null && Keyboard.current.leftAltKey.isPressed) ||
         (Keyboard.current.rightAltKey != null && Keyboard.current.rightAltKey.isPressed));

    private RectTransform WheelRoot => wheelRoot != null ? wheelRoot : transform as RectTransform;

    private void Awake()
    {
        EnsureCanvasGroup();
        BuildWheel();
        SetOpen(false);
    }

    private void OnDisable()
    {
        highlightRotateTween?.Kill();
        highlightRotateTween = null;

        if (wasOpen)
            SetOpen(false);

        if (cursorReleaseActive)
            SetCursorRelease(false);
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        bool emoteBlocked = IsEmoteBlockedByHiding;
        if (emoteBlocked && wasOpen)
            SetOpen(false);

        bool wantsOpen = !emoteBlocked && IsEmoteKeyPressed;
        bool wantsCursorRelease = IsCursorReleaseKeyPressed;
        if (wantsOpen && !wasOpen)
        {
            if (cursorReleaseActive)
                SetCursorRelease(false);

            SetOpen(true);
        }

        if (wantsOpen)
        {
            UpdateSelection();
            return;
        }

        if (wasOpen)
        {
            ExecuteSelectedCommand();
            SetOpen(false);
        }

        if (wantsCursorRelease)
        {
            if (!cursorReleaseActive)
                SetCursorRelease(true);

            return;
        }

        if (cursorReleaseActive)
            SetCursorRelease(false);
    }

    private static bool IsLocalPlayerHidingInteractionBlocked()
    {
        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player == null || !player.IsLocalNetworkPlayer)
                continue;

            NetworkPlayerHidingComponent hiding = player.GetComponent<NetworkPlayerHidingComponent>();
            if (hiding == null)
                hiding = player.GetComponentInChildren<NetworkPlayerHidingComponent>(true);

            return hiding != null && hiding.BlocksPlayerInput;
        }

        return false;
    }

    private void EnsureCanvasGroup()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void BuildWheel()
    {
        RectTransform root = WheelRoot;
        if (root == null)
            return;

        if (generatedRoot != null)
            Destroy(generatedRoot.gameObject);

        highlightRotateTween?.Kill();
        highlightRotateTween = null;
        commandGraphics.Clear();
        highlightRect = null;
        highlightImage = null;
        selectedIndex = -1;

        GameObject generatedObject = new GameObject("GeneratedCommands", typeof(RectTransform));
        generatedObject.layer = gameObject.layer;
        generatedRoot = generatedObject.GetComponent<RectTransform>();
        generatedRoot.SetParent(root, false);
        generatedRoot.anchorMin = Vector2.zero;
        generatedRoot.anchorMax = Vector2.one;
        generatedRoot.offsetMin = Vector2.zero;
        generatedRoot.offsetMax = Vector2.zero;

        int count = commands != null ? commands.Count : 0;
        if (count <= 0)
            return;

        float fillAmount = 1f / count;
        CreateHighlightSector(fillAmount);

        for (int i = 0; i < count; i++)
            CreateCommandLabel(i, count);

        RefreshHighlight();
    }

    private void CreateHighlightSector(float fillAmount)
    {
        GameObject sectorObject = new GameObject("SelectedSector", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        sectorObject.layer = gameObject.layer;
        highlightRect = sectorObject.GetComponent<RectTransform>();
        highlightRect.SetParent(generatedRoot, false);
        highlightRect.anchorMin = Vector2.zero;
        highlightRect.anchorMax = Vector2.one;
        highlightRect.offsetMin = Vector2.zero;
        highlightRect.offsetMax = Vector2.zero;

        highlightImage = sectorObject.GetComponent<Image>();
        highlightImage.sprite = sectorSprite;
        highlightImage.color = highlightColor;
        highlightImage.raycastTarget = false;
        highlightImage.type = Image.Type.Filled;
        highlightImage.fillMethod = Image.FillMethod.Radial360;
        highlightImage.fillOrigin = (int)Image.Origin360.Top;
        highlightImage.fillClockwise = true;
        highlightImage.fillAmount = Mathf.Clamp01(fillAmount);
        highlightImage.enabled = false;
    }

    private void CreateCommandLabel(int index, int count)
    {
        EmoteCommand command = commands[index];
        float angle = GetSegmentCenterAngle(index, count) * Mathf.Deg2Rad;
        Vector2 position = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * labelRadius;

        if (command.icon != null)
            commandGraphics.Add(CreateIcon(index, position, command.icon));
        else
            commandGraphics.Add(CreateText(index, position, command.label));
    }

    private Graphic CreateIcon(int index, Vector2 position, Sprite icon)
    {
        GameObject iconObject = new GameObject($"Command_{index + 1}_Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.layer = gameObject.layer;
        RectTransform rectTransform = iconObject.GetComponent<RectTransform>();
        rectTransform.SetParent(generatedRoot, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = new Vector2(52f, 52f);

        Image image = iconObject.GetComponent<Image>();
        image.sprite = icon;
        image.color = textColor;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private Graphic CreateText(int index, Vector2 position, string label)
    {
        GameObject textObject = new GameObject($"Command_{index + 1}_Text", typeof(RectTransform), typeof(CanvasRenderer));
        textObject.layer = gameObject.layer;
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.SetParent(generatedRoot, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = new Vector2(130f, 38f);

        TextMeshProUGUI tmp = textObject.AddComponent<TextMeshProUGUI>();
        tmp.text = string.IsNullOrWhiteSpace(label) ? $"Command {index + 1}" : label;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 24f;
        tmp.raycastTarget = false;
        return tmp;
    }

    private void SetOpen(bool open)
    {
        wasOpen = open;
        IsOpen = open;
        selectedIndex = -1;
        RefreshHighlight();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = open ? 1f : 0f;
            canvasGroup.interactable = open;
            canvasGroup.blocksRaycasts = open;
        }

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    private void SetCursorRelease(bool active)
    {
        if (cursorReleaseActive == active)
            return;

        cursorReleaseActive = active;

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

    private void UpdateSelection()
    {
        RectTransform root = WheelRoot;
        if (root == null || Mouse.current == null || commands == null || commands.Count <= 0)
        {
            SetSelectedIndex(-1);
            return;
        }

        Canvas canvas = root.GetComponentInParent<Canvas>();
        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 center = RectTransformUtility.WorldToScreenPoint(canvasCamera, root.position);
        Vector2 delta = Mouse.current.position.ReadValue() - center;

        if (delta.magnitude < innerDeadZone)
        {
            SetSelectedIndex(-1);
            return;
        }

        float angle = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
        if (angle < 0f)
            angle += 360f;

        int index = Mathf.FloorToInt(angle / (360f / commands.Count));
        SetSelectedIndex(Mathf.Clamp(index, 0, commands.Count - 1));
    }

    private void SetSelectedIndex(int index)
    {
        if (selectedIndex == index)
            return;

        selectedIndex = index;
        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        bool hasSelection = commands != null && selectedIndex >= 0 && selectedIndex < commands.Count;
        if (highlightImage != null)
            highlightImage.enabled = hasSelection;

        if (hasSelection && highlightRect != null)
        {
            float segmentSize = 360f / commands.Count;
            Vector3 targetEuler = new Vector3(0f, 0f, -selectedIndex * segmentSize);
            highlightRotateTween?.Kill();

            if (highlightRotateDuration > 0f && wasOpen)
            {
                highlightRotateTween = highlightRect
                    .DOLocalRotate(targetEuler, highlightRotateDuration, RotateMode.Fast)
                    .SetEase(Ease.OutQuad);
            }
            else
            {
                highlightRect.localEulerAngles = targetEuler;
                highlightRotateTween = null;
            }
        }

        for (int i = 0; i < commandGraphics.Count; i++)
            commandGraphics[i].transform.localScale = i == selectedIndex ? Vector3.one * 1.18f : Vector3.one;
    }

    private void ExecuteSelectedCommand()
    {
        if (commands == null || selectedIndex < 0 || selectedIndex >= commands.Count)
            return;

        commands[selectedIndex].Executed?.Invoke();
        CommandSelected?.Invoke(selectedIndex);
    }

    private static float GetSegmentCenterAngle(int index, int count)
    {
        if (count <= 0)
            return 0f;

        return (index + 0.5f) * (360f / count);
    }
}
