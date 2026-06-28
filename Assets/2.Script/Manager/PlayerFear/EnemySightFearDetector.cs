using UnityEngine;

public sealed class EnemySightFearDetector : MonoBehaviour, IPlayerFearDetector
{
    [SerializeField] private float detectionDistance = 18f;
    [SerializeField, Range(0f, 1f)] private float viewportPadding = 0.02f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;
    [SerializeField] private float threatEyeHeight = 1.4f;
    [SerializeField] private bool requireLineOfSight = true;

    public bool TryGetVisibleThreat(out PlayerFearThreat threat)
    {
        threat = default;

        PlayerMovement localPlayer = ResolveLocalPlayer();
        Camera playerCamera = ResolveLocalCamera(localPlayer);
        if (playerCamera == null)
            return false;

        CSHEnemy closestEnemy = null;
        Vector3 closestPosition = Vector3.zero;
        float closestDistance = float.PositiveInfinity;

        foreach (CSHEnemy enemy in EnemyRuntimeRegistry.Enemies)
        {
            if (!IsVisibleThreat(playerCamera, enemy, out Vector3 threatPosition, out float distance))
                continue;

            if (distance >= closestDistance)
                continue;

            closestEnemy = enemy;
            closestPosition = threatPosition;
            closestDistance = distance;
        }

        if (closestEnemy == null)
            return false;

        threat = new PlayerFearThreat(
            closestEnemy,
            closestPosition,
            closestDistance,
            closestEnemy.IsChasingTarget(localPlayer));
        return true;
    }

    private bool IsVisibleThreat(Camera playerCamera, CSHEnemy enemy, out Vector3 threatPosition, out float distance)
    {
        threatPosition = Vector3.zero;
        distance = 0f;

        if (enemy == null || !enemy.isActiveAndEnabled)
            return false;

        threatPosition = enemy.transform.position + Vector3.up * threatEyeHeight;
        Vector3 viewportPoint = playerCamera.WorldToViewportPoint(threatPosition);
        if (viewportPoint.z <= 0f)
            return false;

        if (viewportPoint.x < -viewportPadding || viewportPoint.x > 1f + viewportPadding)
            return false;

        if (viewportPoint.y < -viewportPadding || viewportPoint.y > 1f + viewportPadding)
            return false;

        Vector3 cameraPosition = playerCamera.transform.position;
        distance = Vector3.Distance(cameraPosition, threatPosition);
        if (distance > detectionDistance)
            return false;

        return !requireLineOfSight || HasLineOfSight(cameraPosition, threatPosition, enemy);
    }

    private bool HasLineOfSight(Vector3 cameraPosition, Vector3 threatPosition, CSHEnemy enemy)
    {
        Vector3 direction = threatPosition - cameraPosition;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(cameraPosition, direction / distance, distance, lineOfSightMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Transform enemyRoot = enemy.transform;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null)
                continue;

            if (hit.transform == enemyRoot || hit.transform.IsChildOf(enemyRoot))
                return true;

            PlayerMovement player = hit.transform.GetComponentInParent<PlayerMovement>();
            if (player != null && player.IsLocalNetworkPlayer)
                continue;

            return false;
        }

        return true;
    }

    private static PlayerMovement ResolveLocalPlayer()
    {
        if (Manager.Instance != null && Manager.Instance.PlayerManager != null && Manager.Instance.PlayerManager.LocalPlayer != null)
            return Manager.Instance.PlayerManager.LocalPlayer;

        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.IsLocalNetworkPlayer)
                return player;
        }

        return null;
    }

    private static Camera ResolveLocalCamera(PlayerMovement localPlayer)
    {
        if (localPlayer != null && localPlayer.PlayerCamera != null)
            return localPlayer.PlayerCamera;

        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player != null && player.IsLocalNetworkPlayer && player.PlayerCamera != null)
                return player.PlayerCamera;
        }

        return Camera.main;
    }
}
