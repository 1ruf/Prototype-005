using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuScript : MonoBehaviour
{
    public void Play()
    {
        SceneManager.LoadScene("CSHObunga");
    }
    public void Exit()
    {
        Application.Quit();
    }
}
