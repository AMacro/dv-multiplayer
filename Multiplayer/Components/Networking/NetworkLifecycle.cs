using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using DV.Scenarios.Common;
using DV.Utils;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.UI;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Managers;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Utils;
using Newtonsoft.Json;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multiplayer.Components.Networking;

// https://revenantx.github.io/LiteNetLib/index.html
public class NetworkLifecycle : SingletonBehaviour<NetworkLifecycle>
{
    public const byte TICK_RATE = 24;
    private const float TICK_INTERVAL = 1.0f / TICK_RATE;

    public LobbyServerData serverData;
    public bool IsPublicGame { get; set; } = false;
    public bool IsSinglePlayer { get; set; } = true;


    public NetworkServer Server { get; private set; }
    public NetworkClient Client { get; private set; }

    public uint Tick { get; internal set; }
    public Action<uint> OnTick;

    public bool IsServerRunning => Server?.IsRunning ?? false;
    public bool IsClientRunning => Client?.IsRunning ?? false;

    public bool IsProcessingPacket => Client?.IsProcessingPacket ?? false;

    private PlayerListGUI playerList;
    private NetworkStatsGui Stats;
    private readonly ExecutionTimer tickTimer = new();
    private readonly ExecutionTimer tickWatchdog = new(0.25f);

    /// <summary>
    ///     Whether the provided ITransportPeer is the host.
    ///     Note that this does NOT check authority, and should only be used for client-only logic.
    /// </summary>
    public bool IsHost(ITransportPeer peer)
    {
        return Server?.IsRunning == true && Client?.IsRunning == true && Client?.SelfPeer?.Id == peer?.Id;
    }

    /// <summary>
    ///     Whether the caller is the host.
    ///     Note that this does NOT check authority, and should only be used for client-only logic.
    /// </summary>
    public bool IsHost()
    {
        return IsHost(Client?.SelfPeer);
    }

    private readonly Queue<Action> mainMenuLoadedQueue = new();

    protected override void Awake()
    {
        base.Awake();
        playerList = gameObject.AddComponent<PlayerListGUI>();
        Stats = gameObject.AddComponent<NetworkStatsGui>();
        //RegisterPackets();
        WorldStreamingInit.LoadingFinished += () => { playerList.RegisterListeners(); };
        Settings.OnSettingsUpdated += OnSettingsUpdated;
        SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.buildIndex != (int)DVScenes.MainMenu)
                return;

            playerList.UnRegisterListeners();
            TriggerMainMenuEventLater();
        };
        StartCoroutine(PollEvents());
    }

    private void OnSettingsUpdated(Settings settings)
    {
        if (!IsClientRunning && !IsServerRunning)
            return;
        if (settings.ShowStats)
            Stats.Show(Client.Statistics, Server?.Statistics);
        else
            Stats.Hide();
    }

    public void TriggerMainMenuEventLater()
    {
        SingletonBehaviour<CoroutineManager>.Instance.StartCoroutine(TriggerMainMenuEvent());
    }

    private IEnumerator TriggerMainMenuEvent()
    {
        yield return null;
        while (mainMenuLoadedQueue.Count > 0)
            mainMenuLoadedQueue.Dequeue().Invoke();
    }

    public void QueueMainMenuEvent(Action action)
    {
        mainMenuLoadedQueue.Enqueue(action);
    }

    public bool StartServer(IDifficulty difficulty)
    {
        int port = Multiplayer.Settings.Port;

        if (Server != null)
            throw new InvalidOperationException("NetworkManager already exists!");

        if (!IsSinglePlayer)
        {
            if (serverData != null)
            {
                port = serverData.port;
            }
        }

        Multiplayer.Log($"Starting server on port {port}");
        NetworkServer server = new(difficulty, Multiplayer.Settings, IsSinglePlayer, serverData);

        //reset for next game
        IsSinglePlayer = true;
        serverData = null;

        if (!server.Start(port))
            return false;

        Server = server;
        StartClient(IPAddress.Loopback.ToString(), port, Multiplayer.Settings.Password, IsSinglePlayer, null/* (DisconnectReason dr,string msg) =>{ }*/);
        return true;
    }

    public void StartClient(string address, int port, string password, bool isSinglePlayer, Action<DisconnectReason,string> onDisconnect )
    {
        if (Client != null)
            throw new InvalidOperationException("NetworkManager already exists!");
        NetworkClient client = new(Multiplayer.Settings);
        client.Start(address, port, password, isSinglePlayer, onDisconnect);
        Client = client;
        OnSettingsUpdated(Multiplayer.Settings); // Show stats if enabled
    }

    private IEnumerator PollEvents()
    {
        while (!UnloadWatcher.isQuitting)
        {
            Tick++;
            tickTimer.Start();

            tickWatchdog.Start();
            try
            {
                if (!UnloadWatcher.isUnloading)
                    OnTick?.Invoke(Tick);
            }
            catch (Exception e)
            {
                Multiplayer.LogError($"Exception while processing OnTick: {e}");
            }
            finally
            {
                tickWatchdog.Stop(time => Multiplayer.LogWarning($"OnTick took {time} ms!"));
            }

            if(Client != null)
                TickManager(Client);

            if(Server != null)
                TickManager(Server);

            float elapsedTime = tickTimer.Stop();
            float remainingTime = Mathf.Max(0f, TICK_INTERVAL - elapsedTime);
            yield return remainingTime < 0.001f ? null : new WaitForSecondsRealtime(remainingTime);
        }
    }

    private void TickManager(NetworkManager manager)
    {
        if (manager == null)
            return;
        tickWatchdog.Start();
        try
        {
            manager.PollEvents();
        }
        catch (Exception e)
        {
            manager.LogError($"Exception while polling events: {e}");
        }
        finally
        {
            tickWatchdog.Stop(time => manager.LogWarning($"PollEvents took {time} ms!"));
        }
    }

    public void Stop()
    {
        Stats?.Hide();
        Server?.Stop();
        Client?.Stop();
        Server = null;
        Client = null;
    }

    private void OnApplicationQuit()
    {
        Stop();
    }

    public static void CreateLifecycle()
    {
        if (FindObjectOfType<NetworkLifecycle>() != null)
            throw new InvalidOperationException($"{nameof(NetworkLifecycle)} already exists!");
        GameObject gameObject = new($"[{nameof(NetworkLifecycle)}]");
        gameObject.AddComponent<NetworkLifecycle>();
        DontDestroyOnLoad(gameObject);
    }

}
