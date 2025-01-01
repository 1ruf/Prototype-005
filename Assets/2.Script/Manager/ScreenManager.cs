using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.InputSystem;

public class ScreenManager : MonoBehaviour
{
    [SerializeField] private Texture2D _mouseCursor;

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
    }

    private void CheckInteract()
    {
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);

        RaycastHit hitInfo;
        if (Physics.Raycast(ray, out hitInfo, 5))
        {
            if (hitInfo.transform.TryGetComponent(out IInteractable interaction))
            {
                if (Mouse.current.press.wasPressedThisFrame)
                {
                    interaction.Interact();
                }
                Highlight(true);
                return;
            }
        }
        Highlight(false);
    }

    private void Highlight(bool value)
    {
        Manager.Instance.UIManager.SetInteractUI(value,0.1f);
    }
}
