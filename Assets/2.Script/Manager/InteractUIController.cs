using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InteractUIController : MonoBehaviour
{
    [SerializeField] private GameObject _interacableIcon;
    [SerializeField] private Image _pressedImage;
    [SerializeField] private TMP_Text _interactionText;
    [SerializeField] private TMP_Text _targetNameText;
    [SerializeField] private float _fillTweenDuration = 0.08f;

    private void Awake()
    {
        SetInteractProgressVisible(false);
    }

    public void SetInteractUI(bool value, string targetName, string interactionText = null)
    {
        if (_interacableIcon != null)
            _interacableIcon.SetActive(value);

        SetInteractionText(value ? targetName : null, value ? interactionText : null);

        if (!value)
        {
            SetInteractTime(0f);
            SetInteractProgressVisible(false);
        }
    }

    private void SetInteractionText(string targetName, string interactionText)
    {
        VerifySet(_targetNameText, targetName);
        VerifySet(_interactionText, interactionText);

        void VerifySet(TMP_Text targetTmp, string targetText)
        {
            if (targetTmp != null)
            {
                bool hasText = !string.IsNullOrWhiteSpace(targetText);
                targetTmp.text = hasText ? targetText : string.Empty;
                targetTmp.gameObject.SetActive(hasText);
            }
        }
    }

    public void SetInteractProgressVisible(bool value)
    {
        if (_pressedImage == null)
            return;

        _pressedImage.enabled = value;
    }

    public void SetInteractTime(float amount)
    {
        if (_pressedImage == null)
            return;

        amount = Mathf.Clamp01(amount);
        _pressedImage.DOKill();

        if (_fillTweenDuration <= 0f)
        {
            _pressedImage.fillAmount = amount;
            return;
        }

        _pressedImage.DOFillAmount(amount, _fillTweenDuration).SetEase(Ease.OutQuad);
    }
}
