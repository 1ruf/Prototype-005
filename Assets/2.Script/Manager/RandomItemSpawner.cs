using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class RandomItemSpawner : MonoBehaviour
{
    [SerializeField] private List<NetworkObject> itemPrefabs = new List<NetworkObject>();
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
    [SerializeField] private bool spawnOnGameStart = true;

    private readonly List<NetworkObject> spawnedItems = new List<NetworkObject>();
    private bool hasSpawned;

    private void Start()
    {
        if (spawnOnGameStart)
            StartCoroutine(SpawnWhenServerIsReady());
    }

    public void SpawnItems()
    {
        if (hasSpawned)
            return;

        NetworkRunner runner = GetServerRunner();
        if (runner == null)
            return;

        List<NetworkObject> validItemPrefabs = GetValidItemPrefabs();
        List<Transform> availableSpawnPoints = GetAvailableSpawnPoints();
        int spawnCount = Mathf.Min(validItemPrefabs.Count, availableSpawnPoints.Count);

        if (spawnCount == 0)
        {
            Debug.LogWarning($"{nameof(RandomItemSpawner)} on {name} has no valid item prefabs or spawn points.");
            hasSpawned = true;
            return;
        }

        Shuffle(validItemPrefabs);
        Shuffle(availableSpawnPoints);

        for (int i = 0; i < spawnCount; i++)
        {
            Transform spawnPoint = availableSpawnPoints[i];
            NetworkObject spawnedItem = runner.Spawn(
                validItemPrefabs[i],
                spawnPoint.position,
                spawnPoint.rotation);

            if (spawnedItem != null)
                spawnedItems.Add(spawnedItem);
        }

        hasSpawned = true;
    }

    private IEnumerator SpawnWhenServerIsReady()
    {
        while (!hasSpawned)
        {
            NetworkRunner runner = GetRunner();
            if (runner != null && runner.IsRunning && !runner.IsServer)
                yield break;

            if (runner != null && runner.IsRunning && runner.IsServer)
            {
                SpawnItems();
                yield break;
            }

            yield return null;
        }
    }

    private NetworkRunner GetServerRunner()
    {
        NetworkRunner runner = GetRunner();

        if (runner == null || !runner.IsRunning || !runner.IsServer)
            return null;

        return runner;
    }

    private NetworkRunner GetRunner()
    {
        NetworkGameManager gameManager = NetworkGameManager.Instance;
        return gameManager != null ? gameManager.Runner : FindFirstObjectByType<NetworkRunner>();
    }

    private List<NetworkObject> GetValidItemPrefabs()
    {
        List<NetworkObject> validItemPrefabs = new List<NetworkObject>();

        foreach (NetworkObject itemPrefab in itemPrefabs)
        {
            if (itemPrefab != null)
                validItemPrefabs.Add(itemPrefab);
        }

        return validItemPrefabs;
    }

    private List<Transform> GetAvailableSpawnPoints()
    {
        List<Transform> availableSpawnPoints = new List<Transform>();

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint != null)
                availableSpawnPoints.Add(spawnPoint);
        }

        return availableSpawnPoints;
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            (values[i], values[randomIndex]) = (values[randomIndex], values[i]);
        }
    }
}
