using System;
using Fusion;
using UnityEngine;

/// <summary>
/// Captures local legacy-input state for Fusion without coupling input collection to session startup.
/// </summary>
public sealed class NetworkInputProvider : NetworkRunnerCallbacksAdapter
{
    private readonly Func<bool> isLookInputBlocked;
    private readonly float lookSensitivity;

    public NetworkInputProvider(Func<bool> isLookInputBlocked, float lookSensitivity = 3.5f)
    {
        this.isLookInputBlocked = isLookInputBlocked;
        this.lookSensitivity = lookSensitivity;
    }

    public override void OnInput(NetworkRunner runner, NetworkInput input)
    {
        Vector2 lookDelta = isLookInputBlocked != null && isLookInputBlocked()
            ? Vector2.zero
            : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * lookSensitivity;

        input.Set(new NetworkPlayerInput
        {
            Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            LookDelta = lookDelta,
            Sprint = Input.GetKey(KeyCode.LeftShift)
        });
    }
}
