using UnityEngine;
using UnityEngine.InputSystem;

public class Manager : MonoBehaviour
{
    public static Manager Instance { get; set; }

    public ScreenManager ScreenManager;
    public UIManager UIManager;
    public PlayerManager PlayerManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(this);
    }
}
