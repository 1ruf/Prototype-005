using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEngine.Rendering.DebugUI;

public class ScreenManager : MonoBehaviour
{
    [SerializeField] private float _interactableRadius;
    [SerializeField] private Texture2D _mouseCursor;
    [SerializeField] private GameObject _lockedMouseCursor;

    private void OnEnable()
    {
        MousepointerLock(true);
        SetCursor(_mouseCursor);
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
        CheckInteract();
        ReverseRealCursorVisible();
    }

    private void CheckInteract()
    {
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);

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
        Manager.Instance.UIManager.SetInteractUI(value,time);
    }

    public void ReverseRealCursorVisible()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.visible = false;
            _lockedMouseCursor.SetActive(true);
        }
        else
        {
            Cursor.visible = true;
            _lockedMouseCursor.SetActive(false);
        }

    }
}
