using UnityEngine;

public class LightController : GameControllerBase
{
    private readonly static string _lightContainerKey = "_LIGHT_POWER_F1";

    private PowerContainer _lightContainer;
    public override GameControllerBase Init()
    {
        _lightContainer = GameManager.Instance.GetController<PowerController>().RegistContainer(_lightContainerKey);
        return this;
    }

    public void SetLights(bool value)
    {
        _lightContainer.SupplyPowers(value);
    }
}
