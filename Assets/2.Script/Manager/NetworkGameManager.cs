using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using Photon.Voice.Fusion;
using UnityEngine;

/// <summary>
/// Compatibility facade for the network runtime. Serialized configuration and the established
/// public API remain here while lifecycle, input, and spawn responsibilities live in services.
/// </summary>
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

    // Keep these fields on the facade: existing scenes and prefabs serialize against their names.
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private NetworkObject enemyPrefab;
    [SerializeField] private List<Transform> playerSpawnPoints = new List<Transform>();
    [SerializeField] private List<Transform> enemySpawnPoints = new List<Transform>();
    [SerializeField] private string sessionName = "Prototype005";
    [SerializeField] private EnemySpawnDifficulty enemySpawnDifficulty = EnemySpawnDifficulty.Hard;

    private NetworkRunner runner;
    private NetworkSessionService sessionService;
    private NetworkPlayerSpawnService playerSpawnService;
    private NetworkEnemySpawnDirector enemySpawnDirector;
    private NetworkInputProvider inputProvider;
    private NetworkVoiceRuntimeInstaller voiceRuntimeInstaller;

    public NetworkRunner Runner => runner;
    public bool IsServer => runner != null && runner.IsServer;
    public NetworkObject EnemyPrefab => enemyPrefab;

    /// <summary>
    /// Extension point for a future explicit host-migration policy. No local migration is faked.
    /// </summary>
    public event Action<NetworkRunner, HostMigrationToken> HostMigrationRequested;

    public event Action<ShutdownReason> SessionEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _ = StartNetwork();
    }

    private async System.Threading.Tasks.Task StartNetwork()
    {
        try
        {
            enemySpawnDirector = new NetworkEnemySpawnDirector(
                enemyPrefab,
                enemySpawnPoints,
                GetTargetEnemyCount);

            playerSpawnService = new NetworkPlayerSpawnService(playerPrefab, playerSpawnPoints);
            playerSpawnService.PlayerCountChanged += HandlePlayerCountChanged;

            inputProvider = new NetworkInputProvider(
                () => EmoteWheelController.IsBlockingGameplayInput);

            sessionService = new NetworkSessionService(gameObject, sessionName);
            sessionService.SessionEnded += HandleSessionEnded;
            sessionService.HostMigrationRequested += HandleHostMigrationRequested;
            runner = sessionService.Runner;

            voiceRuntimeInstaller = new NetworkVoiceRuntimeInstaller(gameObject);
            voiceRuntimeInstaller.Install();

            var callbacks = new List<INetworkRunnerCallbacks>
            {
                playerSpawnService,
                inputProvider
            };

            FusionVoiceClient voiceClient = voiceRuntimeInstaller.VoiceClient;
            if (voiceClient != null)
                callbacks.Add(voiceClient);

            StartGameResult result = await sessionService.StartAsync(callbacks);
            if (!result.Ok)
            {
                Debug.LogError($"Fusion start failed: {result.ShutdownReason}");
                voiceRuntimeInstaller.UninstallAddedComponents();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            voiceRuntimeInstaller?.UninstallAddedComponents();
        }
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        if (playerSpawnService != null)
            playerSpawnService.PlayerCountChanged -= HandlePlayerCountChanged;

        if (sessionService != null)
        {
            sessionService.SessionEnded -= HandleSessionEnded;
            sessionService.HostMigrationRequested -= HandleHostMigrationRequested;
            sessionService.Shutdown();
        }

        voiceRuntimeInstaller?.Deactivate();
        Instance = null;
    }

    public void SpawnEnemyNear(Vector3 origin)
    {
        enemySpawnDirector?.SpawnNear(runner, origin);
    }

    private void HandlePlayerCountChanged(NetworkRunner activeRunner, int playerCount)
    {
        enemySpawnDirector?.Reconcile(activeRunner, playerCount);
    }

    private void HandleSessionEnded(ShutdownReason reason)
    {
        playerSpawnService?.ClearTracking();
        enemySpawnDirector?.ClearTracking();
        voiceRuntimeInstaller?.Deactivate();
        SessionEnded?.Invoke(reason);
    }

    private void HandleHostMigrationRequested(NetworkRunner activeRunner, HostMigrationToken token)
    {
        HostMigrationRequested?.Invoke(activeRunner, token);
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

    // Compatibility callbacks: the facade is no longer registered by its own startup path, but
    // these forwarders preserve the existing INetworkRunnerCallbacks surface for external callers.
    public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
    {
        playerSpawnService?.OnPlayerJoined(networkRunner, player);
    }

    public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
    {
        playerSpawnService?.OnPlayerLeft(networkRunner, player);
    }

    public void OnInput(NetworkRunner networkRunner, NetworkInput input)
    {
        inputProvider?.OnInput(networkRunner, input);
    }

    public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
    {
        sessionService?.OnShutdown(networkRunner, shutdownReason);
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner networkRunner)
    {
        sessionService?.OnConnectedToServer(networkRunner);
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
    {
        sessionService?.OnDisconnectedFromServer(networkRunner, reason);
    }

    public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }

    public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        sessionService?.OnConnectFailed(networkRunner, remoteAddress, reason);
    }

    public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken)
    {
        sessionService?.OnHostMigration(networkRunner, hostMigrationToken);
    }

    public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner networkRunner) { }
    public void OnSceneLoadStart(NetworkRunner networkRunner) { }
    public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
}
