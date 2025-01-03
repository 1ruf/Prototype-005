using UnityEngine;

public class HorrorSystemManager : MonoBehaviour
{
    public static HorrorSystemManager Instance;

    public bool IsChasing { get; private set; }


    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(this);
    }

    public void SetChase(bool value)
    {
        IsChasing = value;
    }

    public void JumpScare()
    {
        print("¿ö!");
    }
}
