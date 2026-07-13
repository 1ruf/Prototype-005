using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerNetworkPowerBridge : MonoBehaviour
{
    private PlayerMovement playerMovement;

    public void Initialize(PlayerMovement movement)
    {
        playerMovement = movement;
    }

    public void RequestNetworkPowerChange(string key, bool value)
    {
        if (playerMovement == null)
        {
            NetworkPowerRuntime.ApplyPower(key, value);
            return;
        }

        if (!playerMovement.HasNetworkPowerObject)
        {
            NetworkPowerRuntime.ApplyPower(key, value);
            return;
        }

        NetworkString<_64> networkKey = key;
        if (playerMovement.HasNetworkPowerStateAuthority)
        {
            NetworkPowerRuntime.ApplyPower(key, value);
            playerMovement.BroadcastNetworkPower(networkKey, value);
            return;
        }

        playerMovement.SendNetworkPowerRequest(networkKey, value);
    }

    public void RequestNetworkPowerToggle(string key)
    {
        if (playerMovement == null || !playerMovement.HasNetworkPowerObject)
        {
            NetworkPowerRuntime.ApplyPower(key, !NetworkPowerRuntime.GetPower(key));
            return;
        }

        NetworkString<_64> networkKey = key;
        if (playerMovement.HasNetworkPowerStateAuthority)
        {
            bool nextState = !NetworkPowerRuntime.GetPower(key);
            NetworkPowerRuntime.ApplyPower(key, nextState);
            playerMovement.BroadcastNetworkPower(networkKey, nextState);
            return;
        }

        playerMovement.SendNetworkPowerToggle(networkKey);
    }

    public void RequestSnapshotIfNeeded()
    {
        if (playerMovement == null ||
            !playerMovement.HasNetworkPowerInputAuthority ||
            playerMovement.HasNetworkPowerStateAuthority)
            return;

        playerMovement.SendNetworkPowerSnapshotRequest();
    }

    public void ReceiveRequestAtStateAuthority(NetworkString<_64> key, NetworkBool value)
    {
        NetworkPowerRuntime.ApplyPower(key.ToString(), value);
        playerMovement?.BroadcastNetworkPower(key, value);
    }

    public void ReceiveToggleRequestAtStateAuthority(NetworkString<_64> key)
    {
        string powerKey = key.ToString();
        bool nextState = !NetworkPowerRuntime.GetPower(powerKey);
        NetworkPowerRuntime.ApplyPower(powerKey, nextState);
        playerMovement?.BroadcastNetworkPower(key, nextState);
    }

    public void ReceiveReplicatedState(NetworkString<_64> key, NetworkBool value)
    {
        NetworkPowerRuntime.ApplyPower(key.ToString(), value);
    }

    public void ReceiveSnapshotRequestAtStateAuthority()
    {
        if (playerMovement == null)
            return;

        NetworkPowerRuntime.ForEachPowerState((key, value) =>
        {
            playerMovement.BroadcastNetworkPower(key, value);
        });
    }
}
