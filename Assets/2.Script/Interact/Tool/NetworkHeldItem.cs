using Fusion;
using UnityEngine;

public abstract class NetworkHeldItem : MonoBehaviour, IItem
{
    [Header("Presentation")]
    [SerializeField] private Transform itemRoot;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Collider[] heldColliders;
    [SerializeField] private GameObject[] activeObjects;
    [SerializeField] private Vector3 firstPersonPosition = new Vector3(0.28f, -0.22f, 0.52f);
    [SerializeField] private Vector3 firstPersonEuler = Vector3.zero;
    [SerializeField] private Vector3 thirdPersonPosition = new Vector3(-0.34f, 1.28f, 0.48f);
    [SerializeField] private Vector3 thirdPersonEuler = Vector3.zero;

    private bool visible = true;
    private bool active;
    private bool firstPerson;

    public Vector3 FirstPersonPosition => firstPersonPosition;
    public Quaternion FirstPersonRotation => Quaternion.Euler(firstPersonEuler);
    public Vector3 ThirdPersonPosition => thirdPersonPosition;
    public Quaternion ThirdPersonRotation => Quaternion.Euler(thirdPersonEuler);
    public bool IsActive => active;

    protected Transform ItemRoot => itemRoot != null ? itemRoot : transform;

    protected virtual void Awake()
    {
        EnsureReferences();
        ApplyVisible();
        ApplyActive();
    }

    public virtual void Initialize(NetworkPlayerItemHolder holder)
    {
        EnsureReferences();
        SetHeldCollisions(false);
    }

    public void SetFirstPerson(bool value)
    {
        firstPerson = value;
        OnPerspectiveChanged(firstPerson);
    }

    public void SetVisible(bool value)
    {
        visible = value;
        ApplyVisible();
    }

    public void SetActiveState(bool value, bool force = false)
    {
        if (!force && active == value)
            return;

        active = value;
        ApplyActive();
    }

    public virtual void TickPresentation(float deltaTime, bool moving, bool sprinting, float normalizedMoveSpeed, float lookPitch)
    {
    }

    protected virtual void OnPerspectiveChanged(bool isFirstPerson)
    {
    }

    protected virtual void OnActiveStateChanged(bool isActive)
    {
    }

    private void ApplyVisible()
    {
        EnsureReferences();

        if (renderers == null)
            return;

        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
                itemRenderer.enabled = visible;
        }

        ApplyActiveObjects();
    }

    private void ApplyActive()
    {
        ApplyActiveObjects();
        OnActiveStateChanged(active);
    }

    private void ApplyActiveObjects()
    {
        if (activeObjects != null)
        {
            foreach (GameObject activeObject in activeObjects)
            {
                if (activeObject != null)
                    activeObject.SetActive(visible && active);
            }
        }
    }

    private void EnsureReferences()
    {
        if (itemRoot == null)
            itemRoot = transform;

        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (heldColliders == null || heldColliders.Length == 0)
            heldColliders = GetComponentsInChildren<Collider>(true);
    }

    private void SetHeldCollisions(bool enabled)
    {
        EnsureReferences();

        if (heldColliders == null)
            return;

        foreach (Collider heldCollider in heldColliders)
        {
            if (heldCollider != null)
                heldCollider.enabled = enabled;
        }
    }
}
