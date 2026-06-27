using UnityEngine;

[DisallowMultipleComponent]
public class DeadCameraController : MonoBehaviour
{
    [SerializeField] private GameObject cameraRoot;
    [SerializeField] private Behaviour[] controlledBehaviours;

    private void Awake()
    {
        EnsureReferences();
        SetDeadCameraActive(false);
    }

    public void SetDeadCameraActive(bool active)
    {
        EnsureReferences();

        if (controlledBehaviours != null)
        {
            foreach (Behaviour behaviour in controlledBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = active;
            }
        }

        if (cameraRoot != null && cameraRoot.activeSelf != active)
            cameraRoot.SetActive(active);
    }

    private void EnsureReferences()
    {
        if (cameraRoot == null)
            cameraRoot = gameObject;

        if (controlledBehaviours == null || controlledBehaviours.Length == 0)
            controlledBehaviours = cameraRoot.GetComponents<Behaviour>();
    }
}
