using UnityEngine;

public class Light : MonoBehaviour
{
    [SerializeField] private GameObject m_Light;

    private bool _on;
    public void LightSwitch()
    {
        _on = !_on;
        m_Light.SetActive(_on);
    }
}
