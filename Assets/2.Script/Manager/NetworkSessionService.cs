using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns NetworkRunner startup, callback registration, shutdown, and failure cleanup.
/// Host migration is intentionally surfaced as an extension hook rather than simulated locally.
/// </summary>
public sealed class NetworkSessionService : NetworkRunnerCallbacksAdapter
{
    private readonly GameObject host;
    private readonly string sessionName;
    private readonly List<INetworkRunnerCallbacks> registeredCallbacks = new();

    private Task<StartGameResult> startTask;
    private bool shutdownRequested;
    private bool callbacksAttached;
    private bool sessionEndPublished;

    public NetworkSessionService(GameObject host, string sessionName)
    {
        this.host = host;
        this.sessionName = sessionName;
        EnsureRuntimeComponents();
    }

    public NetworkRunner Runner { get; private set; }
    public NetworkSceneManagerDefault SceneManager { get; private set; }
    public NetworkObjectProviderDefault ObjectProvider { get; private set; }

    public event Action<ShutdownReason> SessionEnded;
    public event Action<NetworkRunner, HostMigrationToken> HostMigrationRequested;
    public event Action<NetworkRunner, NetDisconnectReason> Disconnected;
    public event Action<NetworkRunner, NetAddress, NetConnectFailedReason> ConnectFailed;

    public Task<StartGameResult> StartAsync(IEnumerable<INetworkRunnerCallbacks> callbacks)
    {
        if (startTask != null)
            return startTask;

        startTask = StartCoreAsync(callbacks);
        return startTask;
    }

    public void Shutdown(ShutdownReason reason = ShutdownReason.Ok)
    {
        shutdownRequested = true;
        PublishSessionEnded(reason);
        DetachCallbacks();

        if (Runner != null && Runner.IsRunning)
            Runner.Shutdown(false, reason);
    }

    public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        PublishSessionEnded(shutdownReason);
        DetachCallbacks();
    }

    public override void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Disconnected?.Invoke(runner, reason);
    }

    public override void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        ConnectFailed?.Invoke(runner, remoteAddress, reason);
    }

    public override void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        HostMigrationRequested?.Invoke(runner, hostMigrationToken);
    }

    private async Task<StartGameResult> StartCoreAsync(IEnumerable<INetworkRunnerCallbacks> callbacks)
    {
        EnsureRuntimeComponents();
        AttachCallbacks(callbacks);

        try
        {
            StartGameResult result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.AutoHostOrClient,
                SessionName = sessionName,
                Scene = BuildActiveSceneInfo(),
                SceneManager = SceneManager,
                ObjectProvider = ObjectProvider
            });

            if (!result.Ok)
            {
                CleanupFailedStart(result.ShutdownReason);
                return result;
            }

            if (shutdownRequested)
                Shutdown();

            return result;
        }
        catch
        {
            CleanupFailedStart(ShutdownReason.Error);
            throw;
        }
    }

    private void EnsureRuntimeComponents()
    {
        if (host == null)
            throw new InvalidOperationException("A session host GameObject is required.");

        Runner = host.GetComponent<NetworkRunner>();
        if (Runner == null)
            Runner = host.AddComponent<NetworkRunner>();

        Runner.ProvideInput = true;

        SceneManager = host.GetComponent<NetworkSceneManagerDefault>();
        if (SceneManager == null)
            SceneManager = host.AddComponent<NetworkSceneManagerDefault>();

        ObjectProvider = host.GetComponent<NetworkObjectProviderDefault>();
        if (ObjectProvider == null)
            ObjectProvider = host.AddComponent<NetworkObjectProviderDefault>();
    }

    private void AttachCallbacks(IEnumerable<INetworkRunnerCallbacks> callbacks)
    {
        if (callbacksAttached || Runner == null)
            return;

        AddCallback(this);
        if (callbacks != null)
        {
            foreach (INetworkRunnerCallbacks callback in callbacks)
                AddCallback(callback);
        }

        callbacksAttached = true;
    }

    private void AddCallback(INetworkRunnerCallbacks callback)
    {
        if (callback == null || registeredCallbacks.Contains(callback))
            return;

        Runner.AddCallbacks(callback);
        registeredCallbacks.Add(callback);
    }

    private void DetachCallbacks()
    {
        if (!callbacksAttached)
            return;

        if (Runner != null)
        {
            for (int i = registeredCallbacks.Count - 1; i >= 0; i--)
            {
                INetworkRunnerCallbacks callback = registeredCallbacks[i];
                if (callback != null)
                    Runner.RemoveCallbacks(callback);
            }
        }

        registeredCallbacks.Clear();
        callbacksAttached = false;
    }

    private void CleanupFailedStart(ShutdownReason reason)
    {
        PublishSessionEnded(reason);
        DetachCallbacks();

        if (Runner != null && Runner.IsRunning)
            Runner.Shutdown(false, reason);
    }

    private void PublishSessionEnded(ShutdownReason reason)
    {
        if (sessionEndPublished)
            return;

        sessionEndPublished = true;
        SessionEnded?.Invoke(reason);
    }

    private static NetworkSceneInfo BuildActiveSceneInfo()
    {
        NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
        int buildIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
        if (buildIndex >= 0)
            sceneInfo.AddSceneRef(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single);

        return sceneInfo;
    }
}
