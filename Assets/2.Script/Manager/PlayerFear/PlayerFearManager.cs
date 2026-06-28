using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerFearManager : MonoBehaviour
{
    private const string RuntimeManagerName = "PlayerFearManager";

    public static PlayerFearManager Instance { get; private set; }

    [SerializeField] private EnemySightFearDetector detector;
    [SerializeField] private PlayerFearFeedback feedback;
    [SerializeField] private PlayerHeartbeatAudioController heartbeat;
    [SerializeField] private float triggerCooldown = 8f;
    [SerializeField] private float chaseStartedCooldown = 8f;

    public PlayerFearController Controller { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveComponents();
        Controller = GameManager.RegisterController(new PlayerFearController(detector, feedback, triggerCooldown, chaseStartedCooldown));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSceneInstance()
    {
        if (Instance != null || FindFirstObjectByType<PlayerFearManager>() != null)
            return;

        GameObject managerObject = new GameObject(RuntimeManagerName);
        managerObject.AddComponent<PlayerFearManager>();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        GameManager.UnregisterController<PlayerFearController>();
        Instance = null;
    }

    private void Reset()
    {
        ResolveComponents();
    }

    private void OnValidate()
    {
        if (detector == null)
            detector = GetComponent<EnemySightFearDetector>();

        if (feedback == null)
            feedback = GetComponent<PlayerFearFeedback>();

        if (heartbeat == null)
            heartbeat = GetComponent<PlayerHeartbeatAudioController>();

        triggerCooldown = Mathf.Max(0f, triggerCooldown);
        chaseStartedCooldown = Mathf.Max(0f, chaseStartedCooldown);
    }

    private void ResolveComponents()
    {
        if (detector == null)
            detector = GetComponent<EnemySightFearDetector>();

        if (detector == null)
            detector = gameObject.AddComponent<EnemySightFearDetector>();

        if (feedback == null)
            feedback = GetComponent<PlayerFearFeedback>();

        if (feedback == null)
            feedback = gameObject.AddComponent<PlayerFearFeedback>();

        if (heartbeat == null)
            heartbeat = GetComponent<PlayerHeartbeatAudioController>();

        if (heartbeat == null)
            heartbeat = gameObject.AddComponent<PlayerHeartbeatAudioController>();
    }
}
