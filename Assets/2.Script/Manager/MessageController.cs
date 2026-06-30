using DG.Tweening;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MessageController : MonoBehaviour
{
    private static readonly Color FailureColor = new Color(0.7568628f, 0f, 0f, 1f);

    [SerializeField] private TMP_Text messageText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float defaultVisibleDuration = 3f;
    [SerializeField] private float defaultFadeDuration = 0.65f;

    private Tween fadeTween;

    public static MessageController Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;

        ResolveReferences();
        HideInstant();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        fadeTween?.Kill();
    }

    public static bool TryShowFailure(string message)
    {
        MessageController controller = ResolveInstance();
        if (controller == null)
            return false;

        controller.ShowFailure(message);
        return true;
    }

    public void ShowFailure(string message)
    {
        ShowMessage(message, FailureColor, defaultVisibleDuration, defaultFadeDuration);
    }

    public void ShowMessage(string message, Color color)
    {
        ShowMessage(message, color, defaultVisibleDuration, defaultFadeDuration);
    }

    public void ShowMessage(string message, Color color, float visibleDuration, float fadeDuration)
    {
        ResolveReferences();
        if (messageText == null || canvasGroup == null || string.IsNullOrWhiteSpace(message))
            return;

        fadeTween?.Kill();
        messageText.text = message;
        messageText.color = color;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float safeVisibleDuration = Mathf.Max(0f, visibleDuration);
        float safeFadeDuration = Mathf.Max(0f, fadeDuration);
        if (safeFadeDuration <= 0f)
        {
            fadeTween = DOVirtual.DelayedCall(safeVisibleDuration, HideInstant);
            return;
        }

        fadeTween = DOVirtual.DelayedCall(safeVisibleDuration, () =>
        {
            fadeTween = canvasGroup
                .DOFade(0f, safeFadeDuration)
                .SetEase(Ease.OutQuad);
        });
    }

    private void HideInstant()
    {
        ResolveReferences();
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (messageText != null)
            messageText.text = string.Empty;
    }

    private void ResolveReferences()
    {
        if (messageText == null)
            messageText = GetComponent<TMP_Text>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private static MessageController ResolveInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindFirstObjectByType<MessageController>(FindObjectsInactive.Include);
        return Instance;
    }
}
