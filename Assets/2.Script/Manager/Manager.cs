using UnityEngine;

public class Manager : MonoBehaviour
{
    public static Manager Instance { get; set; }

    public ScreenManager ScreenManager;
    public UIManager UIManager;
    public PlayerManager PlayerManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void RegisterLocalPlayer(PlayerMovement player)
    {
        if (player == null)
            return;

        if (PlayerManager != null)
            PlayerManager.SetLocalPlayer(player);

        if (ScreenManager != null)
            ScreenManager.SetLocalPlayer(player);
    }

    public void ClearLocalPlayer(PlayerMovement player)
    {
        if (PlayerManager != null)
            PlayerManager.ClearLocalPlayer(player);

        if (ScreenManager != null)
            ScreenManager.ClearLocalPlayer(player);
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        Instance = null;
    }
}
