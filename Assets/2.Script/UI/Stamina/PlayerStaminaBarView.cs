using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStaminaBarView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image[] fillImages;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color lowColor = new Color(1f, 0.55f, 0.55f, 1f);
    [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.25f;
    [SerializeField] private float fillTweenDuration = 0.08f;
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float fullHoldDuration = 1.2f;
    [SerializeField] private float fadeOutDuration = 0.45f;
    [SerializeField] private float lowBlinkDuration = 0.3f;

    private Tween fadeTween;
    private Tween hideDelayTween;
    private Tween blinkTween;

    private void Awake()
    {
        EnsureReferences();
        SetVisibleInstant(false);
        SetFillInstant(1f);
        SetFillColor(normalColor);
    }

    private void OnDisable()
    {
        KillTweens();
    }

    public void Render(PlayerStaminaSnapshot snapshot)
    {
        float normalized = snapshot.Normalized;
        SetFill(normalized);

        if (!snapshot.IsFull || snapshot.IsSprinting)
            Show();
        else
            HideAfterHold();

        if (normalized <= lowThreshold && !snapshot.IsFull)
            StartLowBlink();
        else
            StopLowBlink();
    }

    public void Clear()
    {
        SetFillInstant(1f);
        StopLowBlink();
        SetVisibleInstant(false);
    }

    private void Show()
    {
        hideDelayTween?.Kill();
        FadeTo(1f, fadeInDuration);
    }

    private void HideAfterHold()
    {
        hideDelayTween?.Kill();
        hideDelayTween = DOVirtual.DelayedCall(fullHoldDuration, () => FadeTo(0f, fadeOutDuration), false);
    }

    private void FadeTo(float alpha, float duration)
    {
        EnsureReferences();
        if (canvasGroup == null)
            return;

        fadeTween?.Kill();
        fadeTween = canvasGroup.DOFade(alpha, Mathf.Max(0f, duration)).SetEase(Ease.OutQuad);
    }

    private void SetVisibleInstant(bool visible)
    {
        EnsureReferences();
        if (canvasGroup != null)
            canvasGroup.alpha = visible ? 1f : 0f;
    }

    private void SetFill(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        foreach (Image image in fillImages)
        {
            if (image == null)
                continue;

            image.DOKill();
            if (fillTweenDuration <= 0f)
                image.fillAmount = normalized;
            else
                image.DOFillAmount(normalized, fillTweenDuration).SetEase(Ease.OutQuad);
        }
    }

    private void SetFillInstant(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        foreach (Image image in fillImages)
        {
            if (image != null)
                image.fillAmount = normalized;
        }
    }

    private void StartLowBlink()
    {
        if (blinkTween != null && blinkTween.IsActive())
            return;

        SetFillColor(normalColor);
        blinkTween = DOVirtual.Float(0f, 1f, lowBlinkDuration, value => SetFillColor(Color.Lerp(normalColor, lowColor, value)))
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    private void StopLowBlink()
    {
        blinkTween?.Kill();
        blinkTween = null;
        SetFillColor(normalColor);
    }

    private void SetFillColor(Color color)
    {
        foreach (Image image in fillImages)
        {
            if (image != null)
                image.color = color;
        }
    }

    private void KillTweens()
    {
        fadeTween?.Kill();
        hideDelayTween?.Kill();
        blinkTween?.Kill();

        if (fillImages == null)
            return;

        foreach (Image image in fillImages)
        {
            if (image != null)
                image.DOKill();
        }
    }

    private void EnsureReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (fillImages == null || fillImages.Length == 0)
            fillImages = GetComponentsInChildren<Image>(true);
    }
}
