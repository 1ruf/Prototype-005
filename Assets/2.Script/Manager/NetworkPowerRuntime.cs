using Fusion;
using UnityEngine;

public static class NetworkPowerRuntime
{
    public static event System.Action<string, bool> PowerStateChanged;

    public static void RegisterPowerable(string key, IPowerable powerable)
    {
        PowerController controller = GetPowerController();
        controller?.GetContainer(key).RegistPowerObject(powerable);
    }

    public static void UnregisterPowerable(string key, IPowerable powerable)
    {
        PowerController controller = FindPowerController();
        if (controller == null || !controller.TryGetContainer(key, out PowerContainer container))
            return;

        container.RemovePowerObject(powerable);
    }

    public static void RequestToggle(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        PlayerMovement localPlayer = GetLocalPlayer();
        if (localPlayer != null && localPlayer.Object != null)
        {
            localPlayer.RequestNetworkPowerToggle(key);
            return;
        }

        ApplyPower(key, !GetPower(key));
    }

    public static void RequestSetPower(string key, bool value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        PlayerMovement localPlayer = GetLocalPlayer();
        if (localPlayer != null && localPlayer.Object != null)
        {
            localPlayer.RequestNetworkPowerChange(key, value);
            return;
        }

        ApplyPower(key, value);
    }

    public static void ApplyPower(string key, bool value)
    {
        PowerController controller = GetPowerController();
        if (controller == null)
            return;

        controller.SetPower(key, value);
        PowerStateChanged?.Invoke(key, value);
    }

    public static bool GetPower(string key)
    {
        PowerController controller = GetPowerController();
        return controller != null && controller.GetPower(key);
    }

    public static bool HasPowerState(string key)
    {
        PowerController controller = FindPowerController();
        return controller != null && controller.HasPowerState(key);
    }

    public static void InitializePowerIfUnknown(string key, bool value)
    {
        PowerController controller = GetPowerController();
        if (controller == null)
            return;

        bool wasKnown = controller.HasPowerState(key);
        controller.SetInitialPower(key, value);

        if (!wasKnown)
            PowerStateChanged?.Invoke(key, controller.GetPower(key));
    }

    public static void ForEachPowerState(System.Action<string, bool> callback)
    {
        PowerController controller = GetPowerController();
        controller?.ForEachState(callback);
    }

    private static PowerController GetPowerController()
    {
        GameManager gameManager = GameManager.EnsureInstance();
        if (gameManager.TryGetController(out PowerController controller))
            return controller;

        return GameManager.RegisterController(new PowerController());
    }

    private static PowerController FindPowerController()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager != null && gameManager.TryGetController(out PowerController controller))
            return controller;

        return null;
    }

    private static PlayerMovement GetLocalPlayer()
    {
        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.Object != null && player.Object.HasInputAuthority)
                return player;
        }

        return null;
    }
}
