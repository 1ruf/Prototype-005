using UnityEngine;

public class PlayerStaminaHudBinder : MonoBehaviour
{
    [SerializeField] private PlayerStaminaBarView view;
    [SerializeField] private float rebindInterval = 0.25f;

    private IReadOnlyPlayerStamina currentStamina;
    private float nextBindTime;

    private void Awake()
    {
        if (view == null)
            view = GetComponent<PlayerStaminaBarView>() ?? GetComponentInChildren<PlayerStaminaBarView>(true);
    }

    private void OnEnable()
    {
        TryBindLocalPlayer(true);
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (currentStamina != null && IsBoundToLocalPlayer())
            return;

        if (Time.unscaledTime < nextBindTime)
            return;

        nextBindTime = Time.unscaledTime + rebindInterval;
        TryBindLocalPlayer(false);
    }

    private void TryBindLocalPlayer(bool force)
    {
        PlayerMovement localPlayer = ResolveLocalPlayer();
        IReadOnlyPlayerStamina stamina = ResolveStamina(localPlayer);
        if (!force && ReferenceEquals(currentStamina, stamina))
            return;

        Bind(stamina);
    }

    private void Bind(IReadOnlyPlayerStamina stamina)
    {
        Unbind();
        currentStamina = stamina;

        if (currentStamina == null)
        {
            view?.Clear();
            return;
        }

        currentStamina.Changed += HandleStaminaChanged;
        view?.Render(currentStamina.Snapshot);
    }

    private void Unbind()
    {
        if (currentStamina != null)
            currentStamina.Changed -= HandleStaminaChanged;

        currentStamina = null;
    }

    private void HandleStaminaChanged(PlayerStaminaSnapshot snapshot)
    {
        view?.Render(snapshot);
    }

    private bool IsBoundToLocalPlayer()
    {
        PlayerMovement localPlayer = ResolveLocalPlayer();
        IReadOnlyPlayerStamina stamina = ResolveStamina(localPlayer);
        return ReferenceEquals(currentStamina, stamina);
    }

    private static IReadOnlyPlayerStamina ResolveStamina(PlayerMovement player)
    {
        return player != null ? player.GetComponent<PlayerStamina>() : null;
    }

    private static PlayerMovement ResolveLocalPlayer()
    {
        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.IsLocalNetworkPlayer)
                return player;
        }

        PlayerMovement fallback = FindFirstObjectByType<PlayerMovement>();
        return fallback != null && fallback.IsLocalNetworkPlayer ? fallback : null;
    }
}
