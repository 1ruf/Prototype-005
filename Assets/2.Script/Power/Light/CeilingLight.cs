using UnityEngine;

public class CeilingLight : PoweredObject
{
    [SerializeField] private Material enableMat;
    [SerializeField] private Material disableMat;

    [SerializeField] private UnityEngine.Light mainLight;

    private MeshRenderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
    }

    protected override void SupplyPower(bool value)
    {
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();

        if (_renderer != null)
            _renderer.material = value ? enableMat : disableMat;

        if (mainLight != null)
            mainLight.enabled = value;
    }

    protected override bool GetInitialPowerState()
    {
        return mainLight != null && mainLight.enabled;
    }
}
