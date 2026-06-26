using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public Transform HandTransform;
    public PlayerMovement LocalPlayer { get; private set; }

    public void SetLocalPlayer(PlayerMovement player)
    {
        LocalPlayer = player;

        Transform hand = FindChildByName(player.transform, "Hand");
        if (hand != null)
            HandTransform = hand;
    }

    public void ClearLocalPlayer(PlayerMovement player)
    {
        if (LocalPlayer != player)
            return;

        LocalPlayer = null;
        HandTransform = null;
    }

    private Transform FindChildByName(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }
}
