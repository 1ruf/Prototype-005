using UnityEngine;

public class Manager : MonoBehaviour
{
    public Manager Instance { get; set; }

    public ScreenManager ScreenManager;
    public UIManager UIManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(this);
    }
}
