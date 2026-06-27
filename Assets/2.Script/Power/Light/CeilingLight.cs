using UnityEngine;

public class CeilingLight : PoweredObject
{
    [SerializeField] private Material enableMat;
    [SerializeField] private Material disableMat;

    [SerializeField] private Light mainLight;

    private readonly static string _lightContainerKey = "_LIGHT_POWER_F1";

    private MeshRenderer _renderer;

    private void Start()
    {
        GameManager.Instance.GetController<PowerController>().GetContainer(_lightContainerKey);
        _renderer = GetComponent<MeshRenderer>();
    }
    protected override void SupplyPower(bool value)
    {
        _renderer.material = value ? enableMat : disableMat;
        mainLight.enabled = value;
    }
}
