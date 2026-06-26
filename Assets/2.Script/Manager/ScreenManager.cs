using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEngine.Rendering.DebugUI;

public class ScreenManager : MonoBehaviour
{
    [SerializeField] private float _interactableRadius;
    [SerializeField] private Texture2D _mouseCursor;
    [SerializeField] private GameObject _lockedMouseCursor;

    private PlayerMovement localPlayer;
    private Camera localCamera;
    private bool hasLocalPlayer;

    private void OnEnable()
    {
        MousepointerLock(true);
        SetCursor(_mouseCursor);
    }

    public void SetLocalPlayer(PlayerMovement player)
    {
        localPlayer = player;
        hasLocalPlayer = player != null;
        localCamera = player != null ? player.GetComponentInChildren<Camera>(true) : null;

        if (localCamera != null)
            localCamera.enabled = true;
    }

    public void ClearLocalPlayer(PlayerMovement player)
    {
        if (localPlayer != player)
            return;

        localPlayer = null;
        localCamera = null;
        hasLocalPlayer = false;
        Highlight(false, 0f);
    }

    public void MousepointerLock(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            return;
        }
        Cursor.lockState = CursorLockMode.None;
    }
    public void SetCursor(Texture2D cursorTexture)
    {
        Cursor.SetCursor(cursorTexture, Vector2.zero, CursorMode.ForceSoftware);
    }

    private void Update()
    {
        ReverseRealCursorVisible();

        if (!ResolveLocalCamera())
        {
            Highlight(false, 0f);
            return;
        }

        CheckInteract();
    }

    private void CheckInteract()
    {
        Ray ray = new Ray(localCamera.transform.position, localCamera.transform.forward);

        RaycastHit hitInfo;
        if (Physics.Raycast(ray, out hitInfo, _interactableRadius))
        {
            if (hitInfo.transform.TryGetComponent(out IInteractable interaction))
            {
                if (Input.GetKeyDown(KeyCode.E)/*Mouse.current.press.wasPressedThisFrame*/)
                {
                    interaction.Interact();
                }
                Highlight(true, 0);
                return;
            }
        }
        Highlight(false, 0.1f);
    }

    private void Highlight(bool value, float time = 0)
    {
        if (Manager.Instance == null || Manager.Instance.UIManager == null)
            return;

        Manager.Instance.UIManager.SetInteractUI(value,time);
    }

    public void ReverseRealCursorVisible()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.visible = false;
            if (_lockedMouseCursor != null)
                _lockedMouseCursor.SetActive(true);
        }
        else
        {
            Cursor.visible = true;
            if (_lockedMouseCursor != null)
                _lockedMouseCursor.SetActive(false);
        }

    }

    private bool ResolveLocalCamera()
    {
        if (hasLocalPlayer && localPlayer == null)
        {
            localCamera = null;
            hasLocalPlayer = false;
        }

        if (localCamera != null && localCamera.isActiveAndEnabled)
            return true;

        if (localPlayer != null)
            localCamera = localPlayer.GetComponentInChildren<Camera>(true);

        if (localCamera == null)
            localCamera = Camera.main;

        return localCamera != null && localCamera.isActiveAndEnabled;
    }
}
