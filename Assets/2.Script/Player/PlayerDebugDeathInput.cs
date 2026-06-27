using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDebugDeathInput : MonoBehaviour
{
    [SerializeField] private NetworkHealthComponent health;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private KeyCode killKey = KeyCode.F;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<NetworkHealthComponent>() ?? GetComponentInParent<NetworkHealthComponent>() ?? GetComponentInParent<PlayerMovement>()?.GetComponentInChildren<NetworkHealthComponent>(true);

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>() ?? GetComponentInParent<PlayerMovement>();
    }

    private void Update()
    {
        if (health == null || !IsLocalPlayer())
            return;

        if (Input.GetKeyDown(killKey))
            health.Kill();
    }

    private bool IsLocalPlayer()
    {
        return playerMovement == null || playerMovement.IsLocalNetworkPlayer;
    }
}
