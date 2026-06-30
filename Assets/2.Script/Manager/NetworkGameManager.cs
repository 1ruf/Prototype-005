using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using Photon.Voice.Unity.UtilityScripts;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private enum EnemySpawnDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Hardcore = 3
    }

    public static NetworkGameManager Instance { get; private set; }

    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private NetworkObject enemyPrefab;
    [SerializeField] private List<Transform> playerSpawnPoints = new List<Transform>();
    [SerializeField] private List<Transform> enemySpawnPoints = new List<Transform>();
    [SerializeField] private string sessionName = "Prototype005";
    [SerializeField] private EnemySpawnDifficulty enemySpawnDifficulty = EnemySpawnDifficulty.Hard;

    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private readonly List<NetworkObject> spawnedEnemies = new List<NetworkObject>();
    private readonly HashSet<int> usedPlayerSpawnPointIndices = new HashSet<int>();
    private readonly HashSet<int> usedEnemySpawnPointIndices = new HashSet<int>();
    private NetworkRunner runner;
    private FusionVoiceClient registeredVoiceClient;

    public NetworkRunner Runner => runner;
    public bool IsServer => runner != null && runner.IsServer;
    public NetworkObject EnemyPrefab => enemyPrefab;

    private async void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        await StartNetwork();
    }

    private async System.Threading.Tasks.Task StartNetwork()
    {
        runner = GetComponent<NetworkRunner>();
        if (runner == null)
            runner = gameObject.AddComponent<NetworkRunner>();

        runner.ProvideInput = true;
        runner.AddCallbacks(this);
        EnsureVoiceRuntimeComponents();

        NetworkSceneManagerDefault sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null)
            sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

        NetworkObjectProviderDefault objectProvider = GetComponent<NetworkObjectProviderDefault>();
        if (objectProvider == null)
            objectProvider = gameObject.AddComponent<NetworkObjectProviderDefault>();

        NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
        int buildIndex = SceneManager.GetActiveScene().buildIndex;
        if (buildIndex >= 0)
            sceneInfo.AddSceneRef(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single);

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.AutoHostOrClient,
            SessionName = sessionName,
            Scene = sceneInfo,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider
        });

        if (!result.Ok)
            Debug.LogError($"Fusion start failed: {result.ShutdownReason}");
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        if (runner != null && registeredVoiceClient != null)
            runner.RemoveCallbacks(registeredVoiceClient);

        Instance = null;
    }

    private void EnsureVoiceRuntimeComponents()
    {
        Recorder recorder = GetComponentInChildren<Recorder>(true);
        if (recorder == null)
            recorder = gameObject.AddComponent<Recorder>();

        FusionVoiceClient voiceClient = GetComponent<FusionVoiceClient>();
        if (voiceClient == null)
            voiceClient = gameObject.AddComponent<FusionVoiceClient>();

        VoiceChatManager voiceChatManager = GetComponent<VoiceChatManager>();
        if (voiceChatManager == null)
            voiceChatManager = gameObject.AddComponent<VoiceChatManager>();

        VoiceChatDiagnostics diagnostics = GetComponent<VoiceChatDiagnostics>();
        if (diagnostics == null)
            diagnostics = gameObject.AddComponent<VoiceChatDiagnostics>();

        if (recorder.GetComponent<MicAmplifier>() == null)
            recorder.gameObject.AddComponent<MicAmplifier>();

        if (recorder.GetComponent<MicrophonePermission>() == null)
            recorder.gameObject.AddComponent<MicrophonePermission>();

        voiceClient.PrimaryRecorder = recorder;
        voiceClient.UseFusionAppSettings = true;
        voiceClient.UseFusionAuthValues = true;

        if (registeredVoiceClient != voiceClient)
        {
            if (registeredVoiceClient != null)
                runner.RemoveCallbacks(registeredVoiceClient);

            runner.AddCallbacks(voiceClient);
            registeredVoiceClient = voiceClient;
        }
    }

    public void SpawnEnemyNear(Vector3 origin)
    {
        if (runner == null || !runner.IsServer || enemyPrefab == null)
            return;

        Transform enemySpawnPoint = GetRandomEnemySpawnPoint();
        if (enemySpawnPoint != null)
        {
            spawnedEnemies.Add(runner.Spawn(enemyPrefab, enemySpawnPoint.position, enemySpawnPoint.rotation));
            return;
        }

        Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
        if (randomDirection == Vector2.zero)
            randomDirection = Vector2.right;

        Vector2 offset = randomDirection * UnityEngine.Random.Range(8f, 16f);
        Vector3 position = origin + new Vector3(offset.x, 0f, offset.y);
        spawnedEnemies.Add(runner.Spawn(enemyPrefab, position, Quaternion.identity));
    }

    public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
    {
        if (!networkRunner.IsServer || playerPrefab == null)
            return;

        Transform spawnPoint = GetPlayerSpawnPoint(player);
        Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(player.RawEncoded * 2f, 2.27f, 0f);
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        NetworkObject playerObject = networkRunner.Spawn(playerPrefab, position, rotation, player);
        spawnedPlayers[player] = playerObject;
        networkRunner.SetPlayerObject(player, playerObject);

        ReconcileEnemyCount();
    }

    public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
    {
        if (!networkRunner.IsServer)
            return;

        if (!spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
            playerObject = networkRunner.GetPlayerObject(player);

        spawnedPlayers.Remove(player);
        networkRunner.SetPlayerObject(player, null);

        if (playerObject != null)
            networkRunner.Despawn(playerObject);

        ReconcileEnemyCount();
    }

    public void OnInput(NetworkRunner networkRunner, NetworkInput input)
    {
        Vector2 lookDelta = EmoteWheelController.IsBlockingGameplayInput
            ? Vector2.zero
            : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3.5f;

        NetworkPlayerInput data = new NetworkPlayerInput
        {
            Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            LookDelta = lookDelta,
            Sprint = Input.GetKey(KeyCode.LeftShift)
        };

        input.Set(data);
    }

    private Transform GetPlayerSpawnPoint(PlayerRef player)
    {
        int preferredStartIndex = Mathf.Abs(player.RawEncoded);
        return GetUnusedSpawnPoint(playerSpawnPoints, usedPlayerSpawnPointIndices, preferredStartIndex);
    }

    private void ReconcileEnemyCount()
    {
        if (runner == null || !runner.IsServer || enemyPrefab == null)
            return;

        spawnedEnemies.RemoveAll(enemy => enemy == null);

        int targetEnemyCount = GetTargetEnemyCount(spawnedPlayers.Count);
        while (spawnedEnemies.Count < targetEnemyCount)
        {
            Transform spawnPoint = GetRandomEnemySpawnPoint();
            Vector3 position = spawnPoint != null ? spawnPoint.position : GetFallbackEnemySpawnPosition();
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            spawnedEnemies.Add(runner.Spawn(enemyPrefab, position, rotation));
        }

        while (spawnedEnemies.Count > targetEnemyCount)
        {
            int lastIndex = spawnedEnemies.Count - 1;
            NetworkObject enemy = spawnedEnemies[lastIndex];
            spawnedEnemies.RemoveAt(lastIndex);

            if (enemy != null)
                runner.Despawn(enemy);
        }
    }

    private int GetTargetEnemyCount(int playerCount)
    {
        if (playerCount <= 0)
            return 0;

        return enemySpawnDifficulty switch
        {
            EnemySpawnDifficulty.Hardcore => playerCount * 2,
            EnemySpawnDifficulty.Hard => Mathf.CeilToInt(playerCount * 1.5f),
            EnemySpawnDifficulty.Normal => playerCount,
            EnemySpawnDifficulty.Easy => Mathf.Max(1, Mathf.CeilToInt(playerCount * 0.5f)),
            _ => Mathf.CeilToInt(playerCount * 1.5f)
        };
    }

    private Transform GetRandomEnemySpawnPoint()
    {
        int randomStartIndex = enemySpawnPoints != null && enemySpawnPoints.Count > 0
            ? UnityEngine.Random.Range(0, enemySpawnPoints.Count)
            : 0;

        return GetUnusedSpawnPoint(enemySpawnPoints, usedEnemySpawnPointIndices, randomStartIndex);
    }

    private Transform GetUnusedSpawnPoint(List<Transform> spawnPoints, HashSet<int> usedIndices, int startIndex)
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return null;

        RemoveInvalidUsedSpawnPointIndices(spawnPoints, usedIndices);

        if (!HasUnusedSpawnPoint(spawnPoints, usedIndices))
            usedIndices.Clear();

        int safeStartIndex = Mathf.Abs(startIndex) % spawnPoints.Count;
        for (int offset = 0; offset < spawnPoints.Count; offset++)
        {
            int index = (safeStartIndex + offset) % spawnPoints.Count;
            Transform spawnPoint = spawnPoints[index];
            if (spawnPoint != null && !usedIndices.Contains(index))
            {
                usedIndices.Add(index);
                return spawnPoint;
            }
        }

        return null;
    }

    private bool HasUnusedSpawnPoint(List<Transform> spawnPoints, HashSet<int> usedIndices)
    {
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] != null && !usedIndices.Contains(i))
                return true;
        }

        return false;
    }

    private void RemoveInvalidUsedSpawnPointIndices(List<Transform> spawnPoints, HashSet<int> usedIndices)
    {
        usedIndices.RemoveWhere(index => index < 0 || index >= spawnPoints.Count || spawnPoints[index] == null);
    }

    private Vector3 GetFallbackEnemySpawnPosition()
    {
        Vector2 randomPosition = UnityEngine.Random.insideUnitCircle * 12f;
        return new Vector3(randomPosition.x, 2.27f, randomPosition.y);
    }

    public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason) { }
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner networkRunner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner networkRunner) { }
    public void OnSceneLoadStart(NetworkRunner networkRunner) { }
    public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
}
