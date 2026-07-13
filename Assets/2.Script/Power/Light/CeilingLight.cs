using UnityEngine;

public class CeilingLight : PoweredObject
{
    [SerializeField] private Material enableMat;
    [SerializeField] private Material disableMat;

    [SerializeField] private Light mainLight;

    [SerializeField] private MeshRenderer renderer;


    protected override void SupplyPower(bool value)
    {
        renderer.material = value ? enableMat : disableMat;
        mainLight.enabled = value;
    }

    protected override bool GetInitialPowerState()
    {
        return mainLight != null && mainLight.enabled;
    }
}
