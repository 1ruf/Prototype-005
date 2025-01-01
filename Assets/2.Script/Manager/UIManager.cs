using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class UIManager : MonoBehaviour
{
    [SerializeField] private Image _interacableIcon;

    public void SetInteractUI(bool value, float time = 0)
    {
        if(value) _interacableIcon.DOFade(1, time);
        else _interacableIcon.DOFade(0, time);
    }
}
