using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuScript : MonoBehaviour
{
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AnimationClip clip;
    [SerializeField] private Animator introAnimation;
    [SerializeField] private Image imageBlock;
    public void Play()
    {
        bgmSource.DOPitch(0, 1.5f);
        introAnimation.Play("Start");
        Invoke("EnterScene", clip.length);
    }

    private void EnterScene() => imageBlock.DOFade(1,2f).OnComplete(()=>SceneManager.LoadScene("CSHObunga"));
    public void Exit()
    {
        Application.Quit();
    }
}
