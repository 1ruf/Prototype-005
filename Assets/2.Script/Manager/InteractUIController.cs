using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class InteractUIController : MonoBehaviour
{
    [SerializeField] private GameObject _interacableIcon;
    [SerializeField] private Image _pressedImage;
    [SerializeField] private float _fillTweenDuration = 0.08f;

    private void Awake()
    {
        SetInteractProgressVisible(false);
    }

    public void SetInteractUI(bool value)
    {
        _interacableIcon.SetActive(value);

        if (!value)
        {
            SetInteractTime(0f);
            SetInteractProgressVisible(false);
        }
    }

    public void SetInteractProgressVisible(bool value)
    {
        if (_pressedImage == null)
            return;

        _pressedImage.gameObject.SetActive(value);
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
