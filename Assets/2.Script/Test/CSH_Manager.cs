using TMPro;
using UnityEngine;

public class CSH_Manager : MonoBehaviour
{
    public static CSH_Manager Instance;

    [SerializeField] private Transform _plr;
    [SerializeField] private GameObject _enemy;
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject inGame;
    [SerializeField] private TextMeshProUGUI partAmoutText;

    public CSHGameController Controller { get; private set; }
    public Transform Player => _plr;
    public GameObject EnemyPrefab => _enemy;
    public Animator Animator => animator;
    public GameObject InGame => inGame;
    public TextMeshProUGUI PartAmountText => partAmoutText;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Controller = GameManager.RegisterController(new CSHGameController(this));
    }

    public void AddFacePart(FacePart part)
    {
        Controller?.AddFacePart(part);
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        GameManager.UnregisterController<CSHGameController>();
        Instance = null;
    }
}

public enum FacePart
{
    Eye,
    Nose,
    Ear,
    Mouth
}
