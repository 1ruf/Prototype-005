using UnityEngine;

public class HorrorSystemManager : MonoBehaviour
{
    public static HorrorSystemManager Instance;

    public HorrorSystemController Controller { get; private set; }
    public bool IsChasing => Controller != null && Controller.IsChasing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Controller = GameManager.RegisterController(new HorrorSystemController());
    }

    public void SetChase(bool value)
    {
        Controller?.SetChase(value);
    }

    public void JumpScare()
    {
        Controller?.JumpScare();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        GameManager.UnregisterController<HorrorSystemController>();
        Instance = null;
    }
}
