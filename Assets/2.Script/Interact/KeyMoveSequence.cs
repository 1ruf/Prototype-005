using Fusion;
using UnityEngine;

/// <summary>
/// Releases the red key only after the linked pipe is open. The visual travels
/// from point 1 to point 2, while the real pickup is spawned at KeySpawn.
/// </summary>
[DisallowMultipleComponent]
public class KeyMoveSequence : NetworkBehaviour
{
    [Header("Sequence")]
    [SerializeField] private PipeHandle pipeHandle;
    [SerializeField] private Transform point1;
    [SerializeField] private Transform point2;
    [SerializeField] private GameObject redKeyVisual;
    [SerializeField, Min(0.01f)] private float moveSpeed = 0.3f;

    [Header("Spawn")]
    [SerializeField] private Transform keySpawn;
    [SerializeField] private NetworkObject itemPrefab;

    private readonly System.Collections.Generic.HashSet<NetworkHealthComponent> handledDeaths = new System.Collections.Generic.HashSet<NetworkHealthComponent>();

    private NetworkInventoryItem spawnedItem;

    // These are authored by the server. Every client renders the same visual from them.
    [Networked] private float Progress { get; set; }
    [Networked] private NetworkBool HasReleasedItem { get; set; }

    private int ItemId
    {
        get
        {
            NetworkInventoryItem item = itemPrefab != null ? itemPrefab.GetComponent<NetworkInventoryItem>() : null;
            return item != null ? item.ItemId : 0;
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            Progress = 0f;
            HasReleasedItem = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        HandlePlayerDeaths();

        if (HasReleasedItem || point1 == null || point2 == null)
            return;

        if (pipeHandle == null || !pipeHandle.IsOpen)
            return;

        float distance = Vector3.Distance(point1.position, point2.position);
        if (distance <= Mathf.Epsilon)
        {
            Progress = 1f;
        }
        else
        {
            Progress = Mathf.MoveTowards(Progress, 1f, moveSpeed * Runner.DeltaTime / distance);
        }

        if (Progress < 1f)
            return;

        HasReleasedItem = true;
        SpawnItemAt(keySpawn);
    }

    public override void Render()
    {
        RenderVisual();
    }

    private void HandlePlayerDeaths()
    {
        foreach (PlayerMovement player in PlayerRuntimeRegistry.Players)
        {
            if (player == null)
                continue;

            NetworkHealthComponent health = player.GetComponentInChildren<NetworkHealthComponent>(true);
            if (health == null)
                continue;

            if (!health.IsDead)
            {
                handledDeaths.Remove(health);
                continue;
            }

            HandlePlayerDeath(health);
        }
    }

    private void HandlePlayerDeath(NetworkHealthComponent health)
    {
        if (health == null || !handledDeaths.Add(health) || !HasReleasedItem)
            return;

        NetworkInventory inventory = health.Owner.GetComponentInChildren<NetworkInventory>(true);
        if (inventory == null || !inventory.IsHoldingItem(ItemId))
            return;

        if (!inventory.TryConsumeHeldItem(ItemId))
            return;

        DespawnReleasedItem();

        // A death returns the key to route 1 when the pipe is closed; otherwise it is
        // immediately recreated at route 3 (KeySpawn).
        if (pipeHandle != null && pipeHandle.IsOpen)
        {
            Progress = 1f;
            HasReleasedItem = true;
            SpawnItemAt(keySpawn);
        }
        else
        {
            Progress = 0f;
            HasReleasedItem = false;
        }
    }

    private void RenderVisual()
    {
        if (redKeyVisual == null || point1 == null || point2 == null)
            return;

        bool visible = !HasReleasedItem;
        if (redKeyVisual.activeSelf != visible)
            redKeyVisual.SetActive(visible);

        if (!visible)
            return;

        redKeyVisual.transform.SetPositionAndRotation(
            Vector3.Lerp(point1.position, point2.position, Progress),
            Quaternion.Slerp(point1.rotation, point2.rotation, Progress));
    }

    private void SpawnItemAt(Transform spawnPoint)
    {
        if (!Object.HasStateAuthority || itemPrefab == null || spawnPoint == null)
            return;

        NetworkRunner runner = GetRunner();
        if (runner != null && runner.IsRunning)
        {
            NetworkObject spawnedObject = runner.Spawn(itemPrefab, spawnPoint.position, spawnPoint.rotation);
            spawnedItem = spawnedObject != null ? spawnedObject.GetComponent<NetworkInventoryItem>() : null;
            return;
        }

        GameObject localItem = Instantiate(itemPrefab.gameObject, spawnPoint.position, spawnPoint.rotation);
        spawnedItem = localItem.GetComponent<NetworkInventoryItem>();
    }

    private void DespawnReleasedItem()
    {
        if (spawnedItem == null)
            return;

        NetworkRunner runner = GetRunner();
        if (runner != null && runner.IsRunning && spawnedItem.Object != null && spawnedItem.Object.IsValid)
            runner.Despawn(spawnedItem.Object);
        else
            Destroy(spawnedItem.gameObject);

        spawnedItem = null;
    }

    private static NetworkRunner GetRunner()
    {
        return NetworkGameManager.Instance != null
            ? NetworkGameManager.Instance.Runner
            : FindFirstObjectByType<NetworkRunner>();
    }
}
