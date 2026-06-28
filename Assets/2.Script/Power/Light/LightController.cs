using UnityEngine;

public class LightController : GameControllerBase
{
    public override GameControllerBase Init()
    {
        GameManager gameManager = GameManager.EnsureInstance();
        if (!gameManager.TryGetController(out PowerController powerController))
            powerController = GameManager.RegisterController(new PowerController());

        powerController.GetContainer(PowerKeys.LightFloor1);
        return this;
    }

    public void SetLights(bool value)
    {
        NetworkPowerRuntime.RequestSetPower(PowerKeys.LightFloor1, value);
    }
}
