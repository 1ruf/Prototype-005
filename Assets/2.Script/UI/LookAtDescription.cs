using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this to an object that has a Collider (or is a parent of one).
/// Its description is shown while the local player's camera is looking directly at that collider.
/// </summary>
[DisallowMultipleComponent]
public sealed class LookAtDescription : MonoBehaviour
{
    [TextArea(2, 5)]
    [SerializeField] private string description = "물체 설명";
    [SerializeField, Min(0.1f)] private float maximumViewDistance = 5f;

    private static int lastCheckedFrame = -1;
    private static CanvasGroup descriptionCanvas;
    private static TMP_Text descriptionText;
    private static LookAtDescription displayedTarget;

    private void Update()
    {
        // Several describable objects can exist in a scene, but the ray only needs
        // to be evaluated once per frame.
        if (lastCheckedFrame == Time.frameCount)
            return;

        lastCheckedFrame = Time.frameCount;
        RefreshDescription();
    }

    private void OnDisable()
    {
        if (displayedTarget == this)
            SetVisible(false);
    }

    private static void RefreshDescription()
    {
        Camera playerCamera = Camera.main;
        if (playerCamera == null)
        {
            SetVisible(false);
            return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, float.PositiveInfinity, ~0, QueryTriggerInteraction.Collide))
        {
            SetVisible(false);
            return;
        }

        LookAtDescription target = hit.collider.GetComponentInParent<LookAtDescription>();
        if (target == null || !target.isActiveAndEnabled || hit.distance > target.maximumViewDistance)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true, target);
    }

    private static void SetVisible(bool visible, LookAtDescription target = null)
    {
        if (!visible)
        {
            displayedTarget = null;
            if (descriptionCanvas != null)
                descriptionCanvas.alpha = 0f;

            return;
        }

        EnsureUI();
        displayedTarget = target;
        descriptionText.text = string.IsNullOrWhiteSpace(target.description) ? string.Empty : target.description;
        descriptionCanvas.alpha = 1f;
    }

    private static void EnsureUI()
    {
        if (descriptionCanvas != null && descriptionText != null)
            return;

        GameObject canvasObject = new GameObject("Look At Description UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        descriptionCanvas = canvasObject.GetComponent<CanvasGroup>();
        descriptionCanvas.interactable = false;
        descriptionCanvas.blocksRaycasts = false;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0f, 0f, 0f, 0.72f);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 85f);
        panelRect.sizeDelta = new Vector2(760f, 110f);

        GameObject textObject = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);
        descriptionText = textObject.GetComponent<TextMeshProUGUI>();
        descriptionText.alignment = TextAlignmentOptions.Center;
        descriptionText.textWrappingMode = TextWrappingModes.Normal;
        descriptionText.fontSize = 30f;
        descriptionText.color = Color.white;

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 14f);
        textRect.offsetMax = new Vector2(-24f, -14f);

        SetVisible(false);
    }
}
