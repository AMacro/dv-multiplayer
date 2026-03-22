using DV;
using DV.Common;
using DV.Customization.Paint;
using DV.Damage;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.MultipleUnit;
using DV.ServicePenalty.UI;
using DV.ThingTypes;
using DV.UI;
using DV.UserManagement;
using DV.WeatherSystem;
using LiteNetLib;
using LiteNetLib.Utils;
using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using Multiplayer.API;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.UI;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.Data.World;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Clientbound.Jobs;
using Multiplayer.Networking.Packets.Clientbound.SaveGame;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Networking.Packets.Clientbound.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.Packets.Serverbound.Jobs;
using Multiplayer.Networking.Packets.Serverbound.Train;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Patches.MainMenu;
using Multiplayer.Patches.SaveGame;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Multiplayer.Networking.Managers.Client;

public class NetworkClient : NetworkManager
{
    protected override string LogPrefix => "[Client]";

    private Action<DisconnectReason, string> onDisconnect;
    private string disconnectMessage;

    private ITransportPeer selfPeer;
    public byte PlayerId { get; private set; }
    public string Username { get; private set; }
    public string CrewName { get; private set; }
    public string DisplayName => string.IsNullOrEmpty(CrewName) ? Username : $"[{CrewName}] {Username}";

    public readonly ClientPlayerManager ClientPlayerManager;
    public readonly Dictionary<byte, ClientPlayerWrapper> PlayerWrapperCache = [];
    public IReadOnlyCollection<ClientPlayerWrapper> ClientPlayerWrappers => PlayerWrapperCache.Values;

    // One way ping in milliseconds
    public int Ping { get; private set; }
    private ITransportPeer serverPeer;
    public float RPC_Timeout => (Ping * 8f) / 1000;

    private ChatGUI chatGUI;
    private readonly bool isSinglePlayer;

    private bool isAlsoHost;
    IGameSession originalSession;

    // Allow mods to add to the wait Queue
    private readonly List<string> readyBlocks = [];

    public NetworkClient(Settings settings, bool singlePlayer) : base(settings)
    {
        Log($"Client created for {(singlePlayer ? "single player" : "multiplayer")} game");
        isSinglePlayer = singlePlayer;
        ClientPlayerManager = new ClientPlayerManager();

        WorldStreamingInit.LoadingFinished += () =>
        {
            NetworkedPlayer.CaptureItemAnchorOffset();
        };

        Username = Multiplayer.Settings.GetUserName();
    }

    public void Start(string address, int port, string password, bool isSinglePlayer, Action<DisconnectReason, string> onDisconnect)
    {
        LogDebug(() => $"NetworkClient Constructor");

        this.onDisconnect = onDisconnect;
        //netManager.Start();
        base.Start();

        ServerboundClientLoginPacket serverboundClientLoginPacket = new()
        {
            Username = this.Username,
            Guid = Multiplayer.Settings.GetGuid().ToByteArray(),
            Password = password,
            BuildVersion = MainMenuControllerPatch.MenuProvider.BuildVersionString,
            Mods = ModCompatibilityManager.Instance.GetLocalMods()
        };

        Log("Sending Login Packet");
        netPacketProcessor.Write(cachedWriter, serverboundClientLoginPacket);
        selfPeer = Connect(address, port, cachedWriter);

        isAlsoHost = NetworkLifecycle.Instance.IsServerRunning;
        originalSession = UserManager.Instance.CurrentUser.CurrentSession;

        LogDebug(() => $"NetworkClient.Start() isAlsoHost: {isAlsoHost}, Original session is Null: {originalSession == null}");
    }

    public override void Stop()
    {
        Log("Stopping client");
        if (!isAlsoHost && originalSession != null)
        {
            LogDebug(() => $"NetworkClient.Stop() destroying session... Original session is Null: {originalSession == null}");
            Client_GameSession.SetCurrent(originalSession);
        }

        base.Stop();
    }

    protected override void Subscribe()
    {
        netPacketProcessor.SubscribeReusable<ClientboundServerLoadingPacket>(OnClientboundServerLoadingPacket);
        netPacketProcessor.SubscribeReusable<ClientboundLoginResponsePacket>(OnClientboundLoginResponsePacket);
        netPacketProcessor.SubscribeReusable<ClientboundDisconnectPacket>(OnClientboundDisconnectPacket);
        netPacketProcessor.SubscribeReusable<ClientboundRemoveLoadingScreenPacket>(OnClientboundRemoveLoadingScreen);
        netPacketProcessor.SubscribeNetSerializable<ClientboundRpcResponsePacket>(OnClientboundRpcResponsePacket);

        netPacketProcessor.SubscribeNetSerializable<ClientboundRpcResponsePacket>(OnClientboundRpcResponsePacket);
        netPacketProcessor.SubscribeReusable<ClientboundTickSyncPacket>(OnClientboundTickSyncPacket);
        netPacketProcessor.SubscribeReusable<ClientboundBeginWorldSyncPacket>(OnClientboundBeginWorldSyncPacket);
        netPacketProcessor.SubscribeReusable<ClientboundGameParamsPacket>(OnClientboundGameParamsPacket);
        netPacketProcessor.SubscribeReusable<ClientboundSaveGameDataPacket>(OnClientboundSaveGameDataPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWeatherPacket>(OnClientboundWeatherPacket);
        netPacketProcessor.SubscribeReusable<ClientboundRailwayStatePacket>(OnClientboundRailwayStatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundStationControllerLookupPacket>(OnClientboundStationControllerLookupPacket);


        netPacketProcessor.SubscribeReusable<ClientboundPlayerJoinedPacket>(OnClientboundPlayerJoinedPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerDisconnectPacket>(OnClientboundPlayerDisconnectPacket);

        netPacketProcessor.SubscribeReusable<ClientboundPlayerPositionPacket>(OnClientboundPlayerPositionPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerPreferencesUpdatePacket>(OnClientboundPlayerPreferencesUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundPingUpdatePacket>(OnClientboundPingUpdatePacket);

        netPacketProcessor.SubscribeReusable<ClientboundTimeAdvancePacket>(OnClientboundTimeAdvancePacket);
        netPacketProcessor.SubscribeReusable<CommonChangeJunctionPacket>(OnCommonChangeJunctionPacket);
        netPacketProcessor.SubscribeReusable<CommonRotateTurntablePacket>(OnCommonRotateTurntablePacket);

        netPacketProcessor.SubscribeReusable<ClientboundSpawnTrainSetPacket>(OnClientboundSpawnTrainSetPacket);
        netPacketProcessor.SubscribeReusable<ClientboundDestroyTrainCarPacket>(OnClientboundDestroyTrainCarPacket);
        netPacketProcessor.SubscribeReusable<ClientboundTrainsetPhysicsPacket>(OnClientboundTrainPhysicsPacket);
        netPacketProcessor.SubscribeReusable<CommonCouplerInteractionPacket>(OnCommonCouplerInteractionPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainUncouplePacket>(OnCommonTrainUncouplePacket);
        netPacketProcessor.SubscribeReusable<CommonHoseConnectedPacket>(OnCommonHoseConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonHoseDisconnectedPacket>(OnCommonHoseDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuConnectedPacket>(OnCommonMuConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuDisconnectedPacket>(OnCommonMuDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonCockFiddlePacket>(OnCommonCockFiddlePacket);
        netPacketProcessor.SubscribeReusable<CommonBrakeCylinderReleasePacket>(OnCommonBrakeCylinderReleasePacket);
        netPacketProcessor.SubscribeReusable<CommonHandbrakePositionPacket>(OnCommonHandbrakePositionPacket);
        netPacketProcessor.SubscribeReusable<CommonPaintThemePacket>(OnCommonPaintThemePacket);
        netPacketProcessor.SubscribeReusable<CommonTrainPortsPacket>(OnCommonSimFlowPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainFusesPacket>(OnCommonTrainFusesPacket);
        netPacketProcessor.SubscribeReusable<ClientboundTrainControlAuthorityUpdatePacket>(OnClientboundTrainControlAuthorityUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundBrakeStateUpdatePacket>(OnClientboundBrakeStateUpdatePacket);

        netPacketProcessor.SubscribeReusable<ClientboundCargoStatePacket>(OnClientboundCargoStatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundCargoHealthUpdatePacket>(OnClientboundCargoHealthUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundCarHealthUpdatePacket>(OnClientboundCarHealthUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundWarehouseControllerUpdatePacket>(OnClientboundWarehouseControllerUpdatePacket);

        netPacketProcessor.SubscribeReusable<ClientboundRerailTrainPacket>(OnClientboundRerailTrainPacket);
        netPacketProcessor.SubscribeReusable<ClientboundMoveTrainPacket>(OnClientboundMoveTrainPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWindowsBrokenPacket>(OnClientboundWindowsBrokenPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWindowsRepairedPacket>(OnClientboundWindowsRepairedPacket);
        netPacketProcessor.SubscribeReusable<ClientboundMoneyPacket>(OnClientboundMoneyPacket);
        netPacketProcessor.SubscribeReusable<ClientboundLicenseAcquiredPacket>(OnClientboundLicenseAcquiredPacket);
        netPacketProcessor.SubscribeReusable<ClientboundGarageUnlockPacket>(OnClientboundGarageUnlockPacket);
        netPacketProcessor.SubscribeReusable<ClientboundDebtStatusPacket>(OnClientboundDebtStatusPacket);
        netPacketProcessor.SubscribeReusable<ClientboundJobsUpdatePacket>(OnClientboundJobsUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundJobsCreatePacket>(OnClientboundJobsCreatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundJobValidateResponsePacket>(OnClientboundJobValidateResponsePacket);
        netPacketProcessor.SubscribeReusable<ClientboundTaskUpdatePacket>(OnClientboundTaskUpdatePacket);

        netPacketProcessor.SubscribeReusable<CommonChatPacket>(OnCommonChatPacket);

        netPacketProcessor.SubscribeNetSerializable<CommonItemChangePacket>(OnCommonItemChangePacket);

        netPacketProcessor.SubscribeReusable<CommonPitStopInteractionPacket>(OnCommonPitStopInteractionPacket);
        netPacketProcessor.SubscribeNetSerializable<CommonPitStopPlugInteractionPacket>(OnCommonPitStopPlugInteractionPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPitStopBulkUpdatePacket>(OnClientboundPitStopBulkUpdatePacket);

        netPacketProcessor.SubscribeReusable<CommonCashRegisterWithModulesActionPacket>(OnCommonCashRegisterWithModulesActionPacket);

        netPacketProcessor.SubscribeReusable<CommonGenericSwitchStatePacket>(OnCommonGenericSwitchStatePacket);

    }

    // Allow mods to register their own packets
    public void RegisterExternalPacket<T>(ClientPacketHandler<T> handler) where T : class, IPacket, new()
    {
        netPacketProcessor.SubscribeReusable<T>((packet) =>
        {
            handler(packet);
        });
    }

    public void RegisterExternalSerializablePacket<T>(ClientPacketHandler<T> handler) where T : class, ISerializablePacket, new()
    {
        netPacketProcessor.SubscribeNetSerializable<ExternalSerializablePacketWrapper<T>>((wrapper) =>
        {
            handler(wrapper.Packet);
        },
        () => new ExternalSerializablePacketWrapper<T>()
        );
    }

    // Allow mods to register ready blocks
    internal void RegisterReadyBlock(string modName)
    {
        Log($"Ready Block has been registered by {modName}");

        if (readyBlocks.Contains(modName))
            return;

        readyBlocks.Add(modName);
    }

    internal void CancelReadyBlock(string modName)
    {
        Log($"Ready Block has been cleared by {modName}");

        if (readyBlocks.Contains(modName))
        {
            readyBlocks.Remove(modName);
            DisplayLoadingInfo displayLoadingInfo = Object.FindObjectOfType<DisplayLoadingInfo>();
            displayLoadingInfo?.OnLoadingStatusChanged($"Mod {modName} loaded", false, 100);
        }
    }

    private void OnLoaded()
    {
        Log($"WorldStreamingInit.LoadingFinished()");
        NetworkedItemManager.Instance.CheckInstance();
        Log($"WorldStreamingInit.LoadingFinished() CacheWorldItems()");
        NetworkedItemManager.Instance.CacheWorldItems();
        Log($"WorldStreamingInit.LoadingFinished() InitialiseCashRegisters()");
        NetworkedCashRegisterWithModules.InitialiseCashRegisters();
        Log($"WorldStreamingInit.LoadingFinished() InitialisePitStops()");
        NetworkedPitStopStation.InitialisePitStops();
        Log($"WorldStreamingInit.LoadingFinished() SendReadyPacket()");
        CoroutineManager.Instance.StartCoroutine(WaitForReadyBlocks());

        WorldStreamingInit.LoadingFinished -= OnLoaded;
    }

    private IEnumerator WaitForReadyBlocks()
    {
        DisplayLoadingInfo displayLoadingInfo = Object.FindObjectOfType<DisplayLoadingInfo>();
        foreach (string modName in readyBlocks)
            displayLoadingInfo?.OnLoadingStatusChanged($"Waiting for mod {modName} to load", false, 100);

        while (readyBlocks.Count > 0)
        {
            yield return null;
        }

        SendReadyPacket();
    }

    public ClientPlayerWrapper GetWrapper(NetworkedPlayer networkedPlayer)
    {
        if (!PlayerWrapperCache.TryGetValue(networkedPlayer.PlayerId, out var wrapper))
        {
            wrapper = new ClientPlayerWrapper(networkedPlayer);
            PlayerWrapperCache[networkedPlayer.PlayerId] = wrapper;
        }
        return wrapper;
    }

    #region Net Events

    public override void OnPeerConnected(ITransportPeer peer)
    {
        serverPeer = peer;
    }

    public override void OnPeerDisconnected(ITransportPeer peer, DisconnectReason disconnectReason)
    {

        LogDebug(() => $"OnPeerDisconnected({peer.Id}, {disconnectReason}) disconnect message: {disconnectMessage}");

        NetworkLifecycle.Instance.Stop();

        TrainStress.globalIgnoreStressCalculation = false;

        if (MainMenuThingsAndStuff.Instance != null)
        {
            //MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
            NetworkLifecycle.Instance.TriggerMainMenuEventLater();
        }
        else
        {
            MainMenu.GoBackToMainMenu();
        }

        onDisconnect(disconnectReason, disconnectMessage);
    }

    public override void OnNetworkLatencyUpdate(ITransportPeer peer, int latency)
    {
        Ping = latency;

        if (latency > LATENCY_FLAG)
            LogWarning($"High Ping Detected! {latency}ms");
    }

    public override void OnConnectionRequest(NetDataReader dataReader, IConnectionRequest request)
    {
        // Clients don't receive incomming requests.
        request.Reject();
    }

    #endregion

    #region Listeners

    private void OnClientboundLoginResponsePacket(ClientboundLoginResponsePacket packet)
    {

        if (packet.Accepted)
        {
            Log($"Player accepted");
            PlayerId = packet.PlayerId;

            if (!string.IsNullOrEmpty(packet.OverrideUsername))
            {
                Log($"A player with username '{Username}' already exists, your temporary username is '{packet.OverrideUsername}'");
                Username = packet.OverrideUsername;
            }

            if (NetworkLifecycle.Instance.IsHost())
                SendReadyPacket();
            else
                SendSaveGameDataRequest();

            return;
        }

        string text = Locale.Get(packet.ReasonKey, packet.ReasonArgs);

        if (packet.Missing.Length != 0 || packet.Extra.Length != 0)
        {
            text += "\n\n";

            if (packet.Missing.Length != 0)
            {
                text += Locale.Get(Locale.DISCONN_REASON__MODS_MISSING_KEY, placeholders: string.Join("\n - ", packet.Missing));
            }

            if (packet.Extra.Length != 0)
            {
                if (packet.Missing.Length != 0)
                    text += "\n";
                text += Locale.Get(Locale.DISCONN_REASON__MODS_EXTRA_KEY, placeholders: string.Join("\n - ", packet.Extra));
            }
        }

        Log($"Player denied: {text}");
        onDisconnect(DisconnectReason.ConnectionRejected, text);
    }

    private void OnClientboundRpcResponsePacket(ClientboundRpcResponsePacket packet)
    {
        LogDebug(() => $"Received RPC response for ticket: {packet.TicketId}, response type: {packet.ResponseType}");
        RpcManager.Instance.ResolveTicket(packet.TicketId, packet.Response);
    }

    private void OnClientboundPlayerJoinedPacket(ClientboundPlayerJoinedPacket packet)
    {
        //Guid guid = new(packet.Guid);
        ClientPlayerManager.AddPlayer(packet.PlayerId, packet.Username, packet.CrewName);

        ClientPlayerManager.UpdatePosition(packet.PlayerId, packet.Position, Vector3.zero, packet.Rotation, false, packet.CarID != 0, packet.CarID);
    }

    //For other player left the game
    private void OnClientboundPlayerDisconnectPacket(ClientboundPlayerDisconnectPacket packet)
    {
        Log($"Received player disconnect packet for player id: {packet.PlayerId}");
        ClientPlayerManager.RemovePlayer(packet.PlayerId);
    }

    //For server shutting down / player kicked
    private void OnClientboundDisconnectPacket(ClientboundDisconnectPacket packet)
    {
        if (packet.Kicked)
        {
            Log($"Player was kicked!");
            disconnectMessage = "You were kicked!";
        }
        else
        {
            Log($"Server Shutting Down");
            disconnectMessage = "Server Shutting Down";
        }
    }

    private void OnClientboundPlayerPositionPacket(ClientboundPlayerPositionPacket packet)
    {
        ClientPlayerManager.UpdatePosition(packet.PlayerId, packet.Position, packet.MoveDir, packet.RotationY, packet.IsJumping, packet.IsOnCar, packet.CarID);
    }

    private void OnClientboundPlayerPreferencesUpdatePacket(ClientboundPlayerPreferencesUpdatePacket packet)
    {
        Log($"Received player preferences update for '{packet.PlayerId}'");

        if (packet.PlayerId == PlayerId)
        {
            CrewName = packet.CrewName;
        }

        ClientPlayerManager.UpdatePreferences(packet.PlayerId, packet.CrewName);
    }

    private void OnClientboundPingUpdatePacket(ClientboundPingUpdatePacket packet)
    {
        ClientPlayerManager.UpdatePing(packet.PlayerId, packet.Ping);
    }

    private void OnClientboundTickSyncPacket(ClientboundTickSyncPacket packet)
    {
        NetworkLifecycle.Instance.Tick = (uint)(packet.ServerTick + Ping / 2.0f * (1f / NetworkLifecycle.TICK_RATE));
    }

    private void OnClientboundServerLoadingPacket(ClientboundServerLoadingPacket packet)
    {
        Log("Waiting for server to load");

        DisplayLoadingInfo displayLoadingInfo = Object.FindObjectOfType<DisplayLoadingInfo>();
        if (displayLoadingInfo == null)
        {
            LogDebug(() => $"Received {nameof(ClientboundServerLoadingPacket)} but couldn't find {nameof(DisplayLoadingInfo)}!");
            return;
        }

        displayLoadingInfo.OnLoadingStatusChanged(Locale.LOADING_INFO__WAIT_FOR_SERVER, false, 100);
    }

    private void OnClientboundGameParamsPacket(ClientboundGameParamsPacket packet)
    {
        LogDebug(() => $"Received {nameof(ClientboundGameParamsPacket)} ({packet.SerializedGameParams.Length} chars)");
        if (Globals.G.GameParams != null)
            packet.Apply(Globals.G.GameParams);
        if (Globals.G.gameParamsInstance != null)
            packet.Apply(Globals.G.gameParamsInstance);
    }

    private void OnClientboundSaveGameDataPacket(ClientboundSaveGameDataPacket packet)
    {
        if (WorldStreamingInit.isLoaded)
        {
            LogWarning("Received save game data packet while already in game!");
            return;
        }

        Log("Received save game data, loading world");

        AStartGameData.DestroyAllInstances();

        GameObject go = new("Server Start Game Data");

        //create a new save and load it
        go.AddComponent<StartGameData_ServerSave>().SetFromPacket(packet);

        //ensure save is not destroyed on scene switch
        Object.DontDestroyOnLoad(go);

        SceneSwitcher.SwitchToScene(DVScenes.Game);
        WorldStreamingInit.LoadingFinished += OnLoaded;

        TrainStress.globalIgnoreStressCalculation = true;

    }

    private void OnClientboundBeginWorldSyncPacket(ClientboundBeginWorldSyncPacket packet)
    {
        Log("Syncing world state");

        DisplayLoadingInfo displayLoadingInfo = Object.FindObjectOfType<DisplayLoadingInfo>();
        if (displayLoadingInfo == null)
        {
            LogDebug(() => $"Received {nameof(ClientboundBeginWorldSyncPacket)} but couldn't find {nameof(DisplayLoadingInfo)}!");
            return;
        }

        displayLoadingInfo.OnLoadingStatusChanged(Locale.LOADING_INFO__SYNC_WORLD_STATE, false, 100);
    }

    private void OnClientboundWeatherPacket(ClientboundWeatherPacket packet)
    {
        Log("Received weather state");

        WeatherDriver.Instance.LoadSaveData(JObject.FromObject(packet), Globals.G.GameParams.WeatherEditorAlwaysAllowed);
    }

    private void OnClientboundRemoveLoadingScreen(ClientboundRemoveLoadingScreenPacket packet)
    {
        Log("World sync finished, removing loading screen");

        DisplayLoadingInfo displayLoadingInfo = Object.FindObjectOfType<DisplayLoadingInfo>();
        if (displayLoadingInfo == null)
        {
            LogDebug(() => $"Received {nameof(ClientboundRemoveLoadingScreenPacket)} but couldn't find {nameof(DisplayLoadingInfo)}!");
            return;
        }

        displayLoadingInfo.OnLoadingFinished();

        //if not single player, add in chat
        if (!isSinglePlayer)
        {
            GameObject common = GameObject.Find("[MAIN]/[GameUI]/[NewCanvasController]/Auxiliary Canvas, EventSystem, Input Module");
            if (common != null)
            {
                //
                GameObject chat = new("Chat GUI", typeof(ChatGUI));
                chat.transform.SetParent(common.transform, false);
                chatGUI = chat.GetComponent<ChatGUI>();
            }
        }
    }

    private void OnClientboundTimeAdvancePacket(ClientboundTimeAdvancePacket packet)
    {
        TimeAdvance.AdvanceTime(packet.amountOfTimeToSkipInSeconds);
    }

    //Force stations to be mapped to same netId across all clients and server - probably should implement for junctions, etc.
    private void OnClientboundStationControllerLookupPacket(ClientboundStationControllerLookupPacket packet)
    {

        if (packet == null)
        {
            LogError("OnClientBoundStationControllerLookupPacket received null packet");
            return;
        }

        if (packet.NetID == null || packet.StationID == null)
        {
            LogError($"OnClientBoundStationControllerLookupPacket received packet with null arrays: NetID is null: {packet.NetID == null}, StationID is null: {packet.StationID == null}");
            return;
        }

        for (int i = 0; i < packet.NetID.Length; i++)
        {
            if (!NetworkedStationController.GetFromStationId(packet.StationID[i], out NetworkedStationController netStationCont))
            {
                LogError($"OnClientBoundStationControllerLookupPacket() could not find station: {packet.StationID[i]}");
            }
            else if (packet.NetID[i] > 0)
            {
                netStationCont.NetId = packet.NetID[i];
            }
            else
            {
                LogError($"OnClientBoundStationControllerLookupPacket() station: {packet.StationID[i]} mapped to NetID 0");
            }
        }
    }

    private void OnClientboundRailwayStatePacket(ClientboundRailwayStatePacket packet)
    {
        Log("Received railway state");

        for (int i = 0; i < packet.SelectedJunctionBranches.Length; i++)
        {
            if (!NetworkedJunction.Get((ushort)(i + 1), out NetworkedJunction junction))
                return;
            junction.Switch((byte)Junction.SwitchMode.NO_SOUND, packet.SelectedJunctionBranches[i], true);
        }

        for (int i = 0; i < packet.TurntableRotations.Length; i++)
        {
            if (!NetworkedTurntable.Get((byte)(i + 1), out NetworkedTurntable turntable))
                return;
            turntable.SetRotation(packet.TurntableRotations[i], true, true);
        }
    }

    private void OnCommonChangeJunctionPacket(CommonChangeJunctionPacket packet)
    {
        if (!NetworkedJunction.Get(packet.NetId, out NetworkedJunction junction))
            return;
        junction.Switch(packet.Mode, packet.SelectedBranch);
    }

    private void OnCommonRotateTurntablePacket(CommonRotateTurntablePacket packet)
    {
        if (!NetworkedTurntable.Get(packet.NetId, out NetworkedTurntable turntable))
            return;
        turntable.SetRotation(packet.rotation);
    }

    private void OnClientboundSpawnTrainSetPacket(ClientboundSpawnTrainSetPacket packet)
    {
        LogDebug(() => $"Spawning trainset consisting of {string.Join(", ", packet.SpawnParts.Select(p => $"{p.CarId} ({p.LiveryId}) with netId: {p.NetId}"))}");

        foreach (var part in packet.SpawnParts)
        {
            if (NetworkedTrainCar.GetTrainCarFromTrainId(part.CarId, out TrainCar car))
            {
                LogError($"ClientboundSpawnTrainSetPacket() Tried to spawn trainset with carId: {part.CarId}, but car already exists!");
                return;
            }
        }

        NetworkedCarSpawner.SpawnCars(packet.SpawnParts, packet.AutoCouple);
    }

    private void OnClientboundDestroyTrainCarPacket(ClientboundDestroyTrainCarPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar netTrainCar))
        {
            LogWarning($"Received DestroyTrainCarPacket for netId: {packet.NetId}, but NetworkedTrainCar was not found.");
            return;
        }

        Log($"Received DestroyTrainCarPacket for [{netTrainCar.CurrentID} {packet.NetId}]");

        //Protect myself from getting deleted in race conditions
        if (PlayerManager.Car == netTrainCar.TrainCar)
        {
            LogWarning($"Server attempted to delete car I'm on: {PlayerManager.Car?.ID}, netId: {packet?.NetId}");
            PlayerManager.SetCar(null);
        }

        //Protect other players from getting deleted in race conditions - this should be a temporary fix, if another playe's game object is deleted we should just recreate it
        if (netTrainCar == null || netTrainCar.gameObject == null || netTrainCar.TrainCar == null)
        {
            LogDebug(() => $"OnClientboundDestroyTrainCarPacket({packet?.NetId}) networkedTrainCar: {netTrainCar != null}, trainCar: {netTrainCar?.TrainCar != null}");
        }
        else
        {
            NetworkedPlayer[] componentsInChildren = (netTrainCar?.gameObject != null) ? netTrainCar.GetComponentsInChildren<NetworkedPlayer>() : [];

            foreach (NetworkedPlayer networkedPlayer in componentsInChildren)
            {
                networkedPlayer.UpdateCar(0);
            }

            netTrainCar.TrainCar.UpdateJobIdOnCarPlates(string.Empty);
            CarSpawner.Instance.DeleteCar(netTrainCar.TrainCar);
        }
    }

    public void OnClientboundTrainPhysicsPacket(ClientboundTrainsetPhysicsPacket packet)
    {
        //LogDebug(() => $"Received Physics packet for netId: {packet.FirstNetId}, tick: {packet.Tick}");
        NetworkTrainsetWatcher.Instance.Client_HandleTrainsetPhysicsUpdate(packet);
    }

    private void OnCommonCouplerInteractionPacket(CommonCouplerInteractionPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar netTrainCar))
        {
            LogError($"OnCommonCouplerInteractionPacket netId: {packet.NetId}, TrainCar not found!");
            return;
        }

        netTrainCar.Common_ReceiveCouplerInteraction(packet);
    }

    //private void OnCommonTrainCouplePacket(CommonTrainCouplePacket packet)
    //{
    //    TrainCar trainCar = null;
    //    TrainCar otherTrainCar = null;

    //    if (!NetworkedTrainCar.TryGet(packet.NetId, out trainCar) || !NetworkedTrainCar.TryGet(packet.OtherNetId, out otherTrainCar))
    //    {
    //        LogDebug(() => $"OnCommonTrainCouplePacket() netId: {packet.NetId}, trainCar found?: {trainCar != null}, otherNetId: {packet.OtherNetId}, otherTrainCar found?: {otherTrainCar != null}");
    //        return;
    //    }

    //    LogDebug(() => $"OnCommonTrainCouplePacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, otherNetId: {packet.OtherNetId}, otherTrainCar: {otherTrainCar.ID}");

    //    Coupler coupler = packet.IsFrontCoupler ? trainCar.frontCoupler : trainCar.rearCoupler;
    //    Coupler otherCoupler = packet.OtherCarIsFrontCoupler ? otherTrainCar.frontCoupler : otherTrainCar.rearCoupler;

    //    if (coupler.CoupleTo(otherCoupler, packet.PlayAudio, false/*B99 packet.ViaChainInteraction*/) == null)
    //        LogDebug(() => $"OnCommonTrainCouplePacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, otherNetId: {packet.OtherNetId}, otherTrainCar: {otherTrainCar.ID} Failed to couple!");
    //}

    private void OnCommonTrainUncouplePacket(CommonTrainUncouplePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
        {
            LogDebug(() => $"OnCommonTrainUncouplePacket() netId: {packet.NetId}, trainCar found?: {trainCar != null}");
            return;
        }

        //LogDebug(() => $"OnCommonTrainUncouplePacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, isFront: {packet.IsFrontCoupler}, playAudio: {packet.PlayAudio}, DueToBrokenCouple: {packet.DueToBrokenCouple}, viaChainInteraction: {packet.ViaChainInteraction}");

        Coupler coupler = packet.IsFrontCoupler ? trainCar.frontCoupler : trainCar.rearCoupler;
        coupler.Uncouple(packet.PlayAudio, false, packet.DueToBrokenCouple, false/*B99 packet.ViaChainInteraction*/);
    }

    private void OnCommonHoseConnectedPacket(CommonHoseConnectedPacket packet)
    {
        bool foundTrainCar = NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar);
        bool foundOtherTrainCar = NetworkedTrainCar.TryGet(packet.OtherNetId, out TrainCar otherTrainCar);

        if (!foundTrainCar || trainCar == null ||
            !foundOtherTrainCar || otherTrainCar == null)
        {
            LogError($"OnCommonHoseConnectedPacket() netId: {packet.NetId}, trainCar found: {foundTrainCar}, trainCar is null: {trainCar == null}, otherNetId: {packet.OtherNetId}, otherTrainCar found: {foundOtherTrainCar}, other trainCar is null:  {otherTrainCar == null}");
            return;
        }

        string carId = $"[{trainCar?.ID}, {packet.NetId}]";
        string otherCarId = $"[{otherTrainCar?.ID}, {packet.OtherNetId}]";

        //LogDebug(() => $"OnCommonHoseConnectedPacket() trainCar: {carId}, isFront: {packet.IsFront}, otherTrainCar: {otherCarId}, isFront: {packet.OtherIsFront}, playAudio: {packet.PlayAudio}");

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;
        Coupler otherCoupler = packet.OtherIsFront ? otherTrainCar.frontCoupler : otherTrainCar.rearCoupler;

        if (coupler == null || coupler.hoseAndCock == null ||
            otherCoupler == null || otherCoupler.hoseAndCock == null)
        {
            LogError($"OnCommonHoseConnectedPacket() trainCar: {carId}, coupler found: {coupler != null}, otherCoupler found: {otherCoupler != null}, hoseAndCock found: {coupler.hoseAndCock != null}, otherHoseAndCock found: {otherCoupler.hoseAndCock != null}");
            return;
        }

        if (coupler.hoseAndCock.IsHoseConnected || otherCoupler.hoseAndCock.IsHoseConnected)
        {
            Coupler connectedTo = null;
            Coupler otherConnectedTo = null;

            if (coupler?.hoseAndCock?.connectedTo != null)
                NetworkedTrainCar.TryGetCoupler(coupler.hoseAndCock.connectedTo, out connectedTo);
            if (otherCoupler?.hoseAndCock?.connectedTo != null)
                NetworkedTrainCar.TryGetCoupler(otherCoupler.hoseAndCock.connectedTo, out otherConnectedTo);

            LogWarning($"OnCommonHoseConnectedPacket() trainCar: {carId}, isFront: {packet.IsFront}, IsHoseConnected: {coupler?.hoseAndCock?.IsHoseConnected}, connectedTo: {connectedTo?.train?.ID}," +
                       $" otherTrainCar: {otherCarId}, other isFront: {otherCoupler?.isFrontCoupler}, other IsHoseConnected: {otherCoupler?.hoseAndCock?.IsHoseConnected}, other connectedTo: {otherConnectedTo?.train?.ID}");
        }
        else
        {
            coupler.ConnectAirHose(otherCoupler, packet.PlayAudio);
        }
    }

    private void OnCommonHoseDisconnectedPacket(CommonHoseDisconnectedPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar netTrainCar) || netTrainCar.IsDestroying)
            return;

        TrainCar trainCar = netTrainCar.TrainCar;

        //LogDebug(() => $"OnCommonHoseDisconnectedPacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, isFront: {packet.IsFront}, playAudio: {packet.PlayAudio}");

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;

        coupler.DisconnectAirHose(packet.PlayAudio);
    }

    private void OnCommonMuConnectedPacket(CommonMuConnectedPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar) || !NetworkedTrainCar.TryGet(packet.OtherNetId, out TrainCar otherTrainCar))
            return;

        MultipleUnitCable cable = packet.IsFront ? trainCar.muModule.frontCable : trainCar.muModule.rearCable;
        MultipleUnitCable otherCable = packet.OtherIsFront ? otherTrainCar.muModule.frontCable : otherTrainCar.muModule.rearCable;

        cable.Connect(otherCable, packet.PlayAudio);
    }

    private void OnCommonMuDisconnectedPacket(CommonMuDisconnectedPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;

        MultipleUnitCable cable = packet.IsFront ? trainCar.muModule.frontCable : trainCar.muModule.rearCable;

        cable.Disconnect(packet.PlayAudio);
    }

    private void OnCommonCockFiddlePacket(CommonCockFiddlePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;

        coupler.IsCockOpen = packet.IsOpen;
    }

    private void OnCommonBrakeCylinderReleasePacket(CommonBrakeCylinderReleasePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;

        trainCar.brakeSystem.ReleaseBrakeCylinderPressure();
    }

    private void OnCommonHandbrakePositionPacket(CommonHandbrakePositionPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;

        trainCar.brakeSystem.SetHandbrakePosition(packet.Position);
    }

    private void OnCommonSimFlowPacket(CommonTrainPortsPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.Common_UpdatePorts(packet);
    }

    private void OnCommonTrainFusesPacket(CommonTrainFusesPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.Common_UpdateFuses(packet);
    }

    private void OnClientboundTrainControlAuthorityUpdatePacket(ClientboundTrainControlAuthorityUpdatePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.Client_ReceiveAuthorityUpdate(packet.PortNetId, packet.State);
    }

    private void OnClientboundBrakeStateUpdatePacket(ClientboundBrakeStateUpdatePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;


        networkedTrainCar.Client_ReceiveBrakeStateUpdate(packet);

        //LogDebug(() => $"Received Brake Pressures netId {packet.NetId}: {packet.MainReservoirPressure}, {packet.IndependentPipePressure}, {packet.BrakePipePressure}, {packet.BrakeCylinderPressure}");
    }

    private void OnClientboundCargoStatePacket(ClientboundCargoStatePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        LogDebug(() => $"OnClientboundCargoStatePacket() {networkedTrainCar.CurrentID}, IsLoading: {packet.IsLoading}, CargoType: {packet.CargoTypeNetId}, CargoAmount: {packet.CargoAmount}, Health: {packet.CargoHealth}, CargoModelIndex: {packet.CargoModelIndex}, WarehouseMachineId: {packet.WarehouseMachineNetId}");

        networkedTrainCar.CargoModelIndex = packet.CargoModelIndex;
        Car logicCar = networkedTrainCar.TrainCar.logicCar;

        if (logicCar == null)
        {
            LogWarning($"OnClientboundCargoStatePacket() Failed to find logic car for [{networkedTrainCar.TrainCar.ID}, {packet.NetId}] is initialised: {networkedTrainCar.Client_Initialized}");
            return;
        }

        CargoTypeLookup.Instance.TryGet(packet.CargoTypeNetId, out CargoType cargoType);

        if (cargoType == CargoType.None && logicCar.CurrentCargoTypeInCar == CargoType.None)
            return;

        //packet.CargoAmount is the total amount, not the amount to load/unload
        float cargoAmount = Mathf.Clamp(packet.CargoAmount, 0, logicCar.capacity);

        WarehouseMachine warehouseMachine = null;
        if (packet.WarehouseMachineNetId != 0 && (!WarehouseMachineLookup.TryGet(packet.WarehouseMachineNetId, out warehouseMachine) || warehouseMachine == null))
        {
            LogWarning($"OnClientboundCargoStatePacket() Failed to find WarehouseMachine for netId {packet.WarehouseMachineNetId}");
            return;
        }

        if (packet.IsLoading)
        {
            LogDebug(() => $"OnClientboundCargoStatePacket() Loading cargo: {cargoType} into {networkedTrainCar.CurrentID}, current amount: {packet.CargoAmount}");
            //Check correct cargo is loaded and the amount is correct
            if (logicCar.LoadedCargoAmount == cargoAmount && logicCar.CurrentCargoTypeInCar == cargoType)
                return;

            //We need either no cargo or the same cargo - if it's different, we need to remove it first
            if (logicCar.CurrentCargoTypeInCar != CargoType.None && logicCar.CurrentCargoTypeInCar != cargoType)
                logicCar.DumpCargo();

            //We have the correct cargo, but not the right amount, calculate the delta
            if (logicCar.CurrentCargoTypeInCar == cargoType)
                cargoAmount -= logicCar.LoadedCargoAmount;

            if (cargoAmount > 0)
            {
                logicCar.LoadCargo(cargoAmount, cargoType, warehouseMachine);
            }

            networkedTrainCar.TrainCar.CargoDamage.LoadCargoDamageState(packet.CargoHealth);
        }
        else
        {
            LogDebug(() => $"OnClientboundCargoStatePacket() Unloading cargo: {cargoType} into {networkedTrainCar.CurrentID}, current amount: {packet.CargoAmount}");

            //Check correct cargo is loaded and the amount is correct
            if (logicCar.LoadedCargoAmount == cargoAmount && logicCar.CurrentCargoTypeInCar == cargoType)
                return;

            //If there is different cargo we need to remove it, then load the appropriate amount
            if (logicCar.CurrentCargoTypeInCar == CargoType.None || logicCar.CurrentCargoTypeInCar != cargoType)
            {
                //avoid triggering the load event by backdooring it
                logicCar.LastUnloadedCargoType = logicCar.CurrentCargoTypeInCar;
                logicCar.CurrentCargoTypeInCar = cargoType;
                logicCar.LoadedCargoAmount = cargoAmount;
            }

            //We have the correct cargo, calculate the delta
            if (logicCar.CurrentCargoTypeInCar == cargoType)
                cargoAmount = logicCar.LoadedCargoAmount - cargoAmount;

            if (cargoAmount > 0)
                logicCar.UnloadCargo(cargoAmount, cargoType, warehouseMachine);
        }
    }

    private void OnClientboundCargoHealthUpdatePacket(ClientboundCargoHealthUpdatePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        CargoDamageModel cargoDamageModel = networkedTrainCar.TrainCar.CargoDamage;

        if (networkedTrainCar.TrainCar == null || cargoDamageModel == null)
            return;

        float deltaHealth = cargoDamageModel.currentHealth - packet.CargoHealth;

        //LogDebug(() => $"OnClientboundCargoHealthUpdatePacket() {networkedTrainCar.CurrentID}, current health: {cargoDamageModel.currentHealth}, new health: {packet.CargoHealth}, delta: {cargoDamageModel}, applySensitivity: {packet.CargoHealth > 0}");

        if (deltaHealth > 0)
            cargoDamageModel.ApplyDamageToCargo(deltaHealth, packet.CargoHealth > 0);
    }

    private void OnClientboundCarHealthUpdatePacket(ClientboundCarHealthUpdatePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;

        packet.Health.LoadTo(trainCar);
    }

    private void OnClientboundWarehouseControllerUpdatePacket(ClientboundWarehouseControllerUpdatePacket packet)
    {
        LogDebug(() => $"OnClientboundWarehouseControllerUpdatePacket() NetId: {packet.NetId}, IsLoading: {packet.IsLoading}, JobNetId: {packet.JobNetId}, CarNetId: {packet.CarNetId}, CargoType: {packet.CargoTypeNetId}, Preset: [{(WarehouseMachineController.TextPreset)packet.Preset}, {packet.Preset}]");
        if (!NetworkedWarehouseMachineController.Get(packet.NetId, out NetworkedWarehouseMachineController networkedWarehouseMachineController))
        {
            LogWarning($"OnClientboundWarehouseControllerUpdatePacket() Failed to find networked warehouse machine controller for [{packet.NetId}]");
            return;
        }

        networkedWarehouseMachineController.ClientProcessUpdate(packet);
    }

    private void OnClientboundRerailTrainPacket(ClientboundRerailTrainPacket packet)
    {
        LogDebug(() => $"OnClientboundRerailTrainPacket() NetId: {packet.NetId}, TrackId: {packet.TrackId}, Position: {packet.Position}, Forward: {packet.Forward}, currentMove: {WorldMover.currentMove}");
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;
        if (!NetworkedRailTrack.TryGet(packet.TrackId, out NetworkedRailTrack networkedRailTrack))
            return;

        Log($"Rerailing [{trainCar?.ID}, {packet.NetId}] to track {networkedRailTrack?.RailTrack?.LogicTrack()?.ID}");
        LogDebug(() => $"Rerailing [{trainCar?.ID}, {packet.NetId}] track: [{networkedRailTrack?.RailTrack?.LogicTrack()?.ID}, {packet.TrackId}], raw position: {packet.Position}, adjusted position: {packet.Position + WorldMover.currentMove}, forward: {packet.Forward}");
        trainCar.Rerail(networkedRailTrack.RailTrack, packet.Position + WorldMover.currentMove, packet.Forward);
    }

    private void OnClientboundMoveTrainPacket(ClientboundMoveTrainPacket packet)
    {
        LogDebug(() => $"OnClientboundMoveTrainPacket() received for netId: {packet.NetId}, trackId: {packet.TrackId}, position: {packet.Position}, forward: {packet.Forward}, isTeleporting: {packet.IsTeleporting}");
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;
        if (!NetworkedRailTrack.TryGet(packet.TrackId, out NetworkedRailTrack networkedRailTrack))
            return;

        Log($"Moving [{trainCar?.ID}, {packet.NetId}] to track {networkedRailTrack?.RailTrack?.LogicTrack()?.ID}");
        LogDebug(() => $"Moving [{trainCar?.ID}, {packet.NetId}] track: [{networkedRailTrack?.RailTrack?.LogicTrack()?.ID}, {packet.TrackId}], raw position: {packet.Position}, adjusted position: {packet.Position + WorldMover.currentMove}, forward: {packet.Forward}, front coupled: {trainCar.frontCoupler.coupledTo != null}, rear coupled:{trainCar.rearCoupler.coupledTo != null}, derailed: {trainCar.derailed}");

        if (!packet.IsTeleporting)
            trainCar.MoveToTrackWithCarUncouple(networkedRailTrack.RailTrack, packet.Position + WorldMover.currentMove, packet.Forward);
        else
            LogDebug(() => $"OnClientboundMoveTrainPacket() netId: {packet.NetId} Attempting to move train, teleport not implemented");
    }

    private void OnClientboundWindowsBrokenPacket(ClientboundWindowsBrokenPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;
        DamageController damageController = trainCar.GetComponent<DamageController>();
        if (damageController == null)
            return;
        WindowsBreakingController windowsController = damageController.windows;
        if (windowsController == null)
            return;
        windowsController.BreakWindowsFromCollision(packet.ForceDirection);
    }

    private void OnClientboundWindowsRepairedPacket(ClientboundWindowsRepairedPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar))
            return;
        DamageController damageController = trainCar.GetComponent<DamageController>();
        if (damageController == null)
            return;
        WindowsBreakingController windowsController = damageController.windows;
        if (windowsController == null)
            return;
        windowsController.RepairWindows();
    }

    private void OnClientboundMoneyPacket(ClientboundMoneyPacket packet)
    {
        LogDebug(() => $"Received new money amount ${packet.Amount}");
        Inventory.Instance.SetMoney(packet.Amount);
    }

    private void OnClientboundLicenseAcquiredPacket(ClientboundLicenseAcquiredPacket packet)
    {
        LogDebug(() => $"Received new {(packet.IsJobLicense ? "job" : "general")} license {packet.Id}");

        if (packet.IsJobLicense)
            LicenseManager.Instance.AcquireJobLicense(Globals.G.Types.jobLicenses.Find(l => l.id == packet.Id));
        else
            LicenseManager.Instance.AcquireGeneralLicense(Globals.G.Types.generalLicenses.Find(l => l.id == packet.Id));

        foreach (CareerManagerLicensesScreen screen in Object.FindObjectsOfType<CareerManagerLicensesScreen>())
            screen.PopulateTextsFromIndex(screen.IndexOfFirstDisplayedEntry); //B99
    }

    private void OnClientboundGarageUnlockPacket(ClientboundGarageUnlockPacket packet)
    {
        LogDebug(() => $"Received new garage {packet.Id}");
        LicenseManager.Instance.UnlockGarage(Globals.G.types.garages.Find(g => g.id == packet.Id));
    }

    private void OnClientboundDebtStatusPacket(ClientboundDebtStatusPacket packet)
    {
        CareerManagerDebtControllerPatch.HasDebt = packet.HasDebt;
    }

    private void OnCommonChatPacket(CommonChatPacket packet)
    {
        chatGUI?.ReceiveMessage(packet.message);
    }

    private void OnClientboundJobsCreatePacket(ClientboundJobsCreatePacket packet)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return;

        if (!NetworkedStationController.Get(packet.StationNetId, out NetworkedStationController networkedStationController))
        {
            LogError($"OnClientboundJobsCreatePacket() {packet.StationNetId} does not exist!");
            return;
        }

        Log($"Received {packet.Jobs.Length} jobs for station {networkedStationController.StationController.logicStation.ID}");

        networkedStationController.AddJobs(packet.Jobs);
    }

    private void OnClientboundJobsUpdatePacket(ClientboundJobsUpdatePacket packet)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return;

        if (!NetworkedStationController.Get(packet.StationNetId, out NetworkedStationController networkedStationController))
        {
            LogError($"OnClientboundJobsUpdatePacket() {packet.StationNetId} does not exist!");
            return;
        }

        Log($"Received {packet.JobUpdates.Length} job updates for station {networkedStationController.StationController.logicStation.ID}");

        networkedStationController.UpdateJobs(packet.JobUpdates);
    }

    private void OnClientboundTaskUpdatePacket(ClientboundTaskUpdatePacket packet)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return;

        if (!NetworkedTask.TryGet(packet.TaskNetId, out Task task) || task == null)
        {
            LogError($"Received task update for taskNetId {packet.TaskNetId}, task was not found");
            return;
        }

        task.SetState(packet.NewState);
        task.taskStartTime = packet.TaskStartTime;
        task.taskFinishTime = packet.TaskFinishTime;
    }

    private void OnClientboundJobValidateResponsePacket(ClientboundJobValidateResponsePacket packet)
    {
        Log($"Job validation response received JobNetId: {packet.JobNetId}, Status: {packet.Invalid}");

        if (!NetworkedJob.Get(packet.JobNetId, out NetworkedJob networkedJob))
            return;

        Object.Destroy(networkedJob.gameObject);
    }

    private void OnCommonPitStopInteractionPacket(CommonPitStopInteractionPacket packet)
    {
        if (!NetworkedPitStopStation.Get(packet.NetId, out var netPitStop))
        {
            LogWarning($"Pit Stop Interaction received for netId: {packet.NetId}, but pit stop does not exist!");
        }

        Log($"Pit stop interaction received for {netPitStop.StationName}");

        LogDebug(() => $"OnCommonPitStopInteractionPacket() [{netPitStop.StationName}, {packet.NetId}], interaction: [{packet.InteractionType}], resource: {packet?.ResourceType}, State: {packet.Value}");
        netPitStop.ProcessInteractionPacketAsClient(packet);
    }

    private void OnCommonPitStopPlugInteractionPacket(CommonPitStopPlugInteractionPacket packet)
    {
        if (!NetworkedPluggableObject.Get(packet.NetId, out var netPlug))
        {
            LogWarning($"Pit Stop Plug Interaction received for plug netId: {packet.NetId}, but pit stop plug does not exist!");
            return;
        }

        Log($"Pit Stop Plug Interaction received for {netPlug.NetId}");

        LogDebug(() => $"OnCommonPitStopPlugInteractionPacket() [{netPlug?.transform?.name}, {packet.NetId}], interaction: [{(PlugInteractionType)packet.InteractionType}]");
        netPlug.ProcessPacket(packet);
    }

    private void OnClientboundPitStopBulkUpdatePacket(ClientboundPitStopBulkUpdatePacket packet)
    {
        LogDebug(() => $"OnClientboundPitStopBulkUpdatePacket() NetId: {packet.NetId}, CarCount: {packet.CarCount}, CarSelection: {packet.CarSelection}, FaucetNotch: {packet.FaucetNotch}, ResourceData Count: {packet.ResourceData.Length}, PlugData: {packet.PlugData.Length}");

        if (!NetworkedPitStopStation.Get(packet.NetId, out var netPitStop))
        {
            LogWarning($"Pit Stop Bulk Data received for station netId: {packet.NetId}, but pit stop does not exist!");
            return;
        }

        Log($"Pit Stop Bulk Data received for {netPitStop.StationName}");

        netPitStop.ProcessBulkUpdate(packet);
    }


    private void OnCommonItemChangePacket(CommonItemChangePacket packet)
    {
        //LogDebug(() => $"OnCommonItemChangePacket({packet?.Items?.Count})");


        //LogDebug(() =>
        //{
        //    string debug = "";

        //    foreach (var item in packet?.Items)
        //    {
        //        debug += "UpdateType: " + item?.UpdateType + "\r\n";
        //        debug += "itemNetId: " + item?.ItemNetId + "\r\n";
        //        debug += "PrefabName: " + item?.PrefabName + "\r\n";
        //        debug += "Equipped: " + item?.ItemState + "\r\n";
        //        debug += "Position: " + item?.ItemPosition + "\r\n";
        //        debug += "Rotation: " + item?.ItemRotation + "\r\n";
        //        debug += "ThrowDirection: " + item?.ThrowDirection + "\r\n";
        //        debug += "Player: " + item?.Player + "\r\n";
        //        debug += "CarNetId: " + item?.CarNetId + "\r\n";
        //        debug += "AttachedFront: " + item?.AttachedFront + "\r\n";

        //        debug += $"States: {item?.States?.Count}\r\n";

        //        if (item.States != null)
        //            foreach (var state in item?.States)
        //                debug += "\t" + state.Key + ": " + state.Value + "\r\n";
        //        else
        //            debug += "\r\n";
        //    }

        //    return debug;
        //});

        //NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, null);
    }

    private void OnCommonPaintThemePacket(CommonPaintThemePacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar netTrainCar))
            return;

        if (!PaintThemeLookup.Instance.TryGet(packet.PaintThemeId, out PaintTheme paint) || paint == null)
        {
            LogWarning($"Received paint theme change for {netTrainCar?.CurrentID}, but paint theme id '{packet.PaintThemeId}' does not exist.");
            return;
        }

        Log($"Received paint theme change for {netTrainCar?.CurrentID}, theme '{paint.AssetName}'");

        LogDebug(() => $"OnCommonPaintThemePacket() [{netTrainCar?.CurrentID}, {packet.NetId}], area: {packet.TargetArea}, paint: [{paint?.AssetName}, {packet.PaintThemeId}]");
        netTrainCar?.Common_ReceivePaintThemeUpdate(packet.TargetArea, paint);
    }

    private void OnCommonCashRegisterWithModulesActionPacket(CommonCashRegisterWithModulesActionPacket packet)
    {
        if (!NetworkedCashRegisterWithModules.Get(packet.NetId, out NetworkedCashRegisterWithModules netCashRegister))
        {
            LogWarning($"Cash Register With Modules Action received for netId: {packet.NetId}, but cash register does not exist!");
            return;
        }

        Log($"Cash Register With Modules Action received for {netCashRegister.GetObjectPath()}, Action: {packet.Action}, Amount: {packet.Amount}");

        netCashRegister.Client_ProcessCashRegisterAction(packet.Action, packet.Amount);
    }

    private void OnCommonGenericSwitchStatePacket(CommonGenericSwitchStatePacket packet)
    {
        if (!NetworkedGenericSwitch.TryGet(packet.NetId, out NetworkedGenericSwitch netSwitch))
        {
            LogWarning($"Received Generic Switch State for switch {packet.NetId}, but switch does not exist!");
            return;
        }

        netSwitch.Client_ReceiveSwitchState(packet.IsOn);
    }

    #endregion

    #region Senders

    private void SendPacketToServer<T>(T packet, DeliveryMethod deliveryMethod) where T : class, new()
    {
        SendPacket(serverPeer, packet, deliveryMethod);
    }

    private void SendNetSerializablePacketToServer<T>(T packet, DeliveryMethod deliveryMethod) where T : INetSerializable, new()
    {
        SendNetSerializablePacket(serverPeer, packet, deliveryMethod);
    }


    #region Mod Packets
    public void SendExternalPacketToServer<T>(T packet, bool reliable) where T : class, IPacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        SendPacketToServer(packet, deliveryMethod);
    }

    public void SendExternalSerializablePacketToServer<T>(T packet, bool reliable) where T : class, ISerializablePacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        var wrapper = new ExternalSerializablePacketWrapper<T> { Packet = packet };
        SendNetSerializablePacketToServer(wrapper, deliveryMethod);
    }
    #endregion

    public void SendSaveGameDataRequest()
    {
        Log("Requesting game data from server");
        SendPacketToServer(new ServerboundSaveGameDataRequestPacket(), DeliveryMethod.ReliableOrdered);
    }

    private void SendReadyPacket()
    {
        Log("World loaded, sending ready packet");
        SendPacketToServer(new ServerboundClientReadyPacket(), DeliveryMethod.ReliableOrdered);
    }

    public void SendPlayerPosition(Vector3 position, Vector3 moveDir, float rotationY, ushort carId, bool isJumping, bool isOnCar, bool reliable)
    {
        //LogDebug(() => $"SendPlayerPosition({position}, {moveDir}, {rotationY}, {carId}, {isJumping}, {IsOnCar})");

        SendPacketToServer(new ServerboundPlayerPositionPacket
        {
            Position = position,
            MoveDir = new Vector2(moveDir.x, moveDir.z),
            RotationY = rotationY,
            IsJumpingIsOnCar = (byte)((isJumping ? 1 : 0) | (isOnCar ? 2 : 0)),
            CarID = carId
        }, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
    }

    public void SendTimeAdvance(float amountOfTimeToSkipInSeconds)
    {
        SendPacketToServer(new ServerboundTimeAdvancePacket
        {
            amountOfTimeToSkipInSeconds = amountOfTimeToSkipInSeconds
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendJunctionSwitched(ushort netId, byte selectedBranch, Junction.SwitchMode mode)
    {
        SendPacketToServer(new CommonChangeJunctionPacket
        {
            NetId = netId,
            SelectedBranch = selectedBranch,
            Mode = (byte)mode
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendTurntableRotation(byte netId, float rotation)
    {
        SendPacketToServer(new CommonRotateTurntablePacket
        {
            NetId = netId,
            rotation = rotation
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendCouplerInteraction(CouplerInteractionType flags, Coupler coupler, Coupler otherCoupler = null)
    {
        ushort couplerNetId = coupler?.train?.GetNetId() ?? 0;
        ushort otherCouplerNetId = otherCoupler?.train?.GetNetId() ?? 0;
        bool couplerIsFront = coupler?.isFrontCoupler ?? false;
        bool otherCouplerIsFront = otherCoupler?.isFrontCoupler ?? false;

        if (couplerNetId == 0)
        {
            LogWarning($"SendCouplerInteraction failed. Coupler: {coupler.name} {couplerNetId}");
            return;
        }

        LogDebug(() => $"SendCouplerInteraction([{flags}], {coupler?.train?.ID}, {otherCoupler?.train?.ID}) coupler isFront: {couplerIsFront}, otherCoupler isFront: {otherCouplerIsFront}");

        if (coupler == null)
            return;

        Log($"Sending coupler interaction [{flags}] for {coupler?.train?.ID}, {(couplerIsFront ? "Front" : "Rear")}");

        SendPacketToServer(new CommonCouplerInteractionPacket
        {
            NetId = couplerNetId,
            IsFrontCoupler = couplerIsFront,
            OtherNetId = otherCouplerNetId,
            IsFrontOtherCoupler = otherCouplerIsFront,
            Flags = (ushort)flags,
        }, DeliveryMethod.ReliableOrdered);
    }


    //public void SendTrainCouple(Coupler coupler, Coupler otherCoupler, bool playAudio, bool viaChainInteraction)
    //{
    //    ushort couplerNetId = coupler.train.GetNetId();
    //    ushort otherCouplerNetId = otherCoupler.train.GetNetId();

    //    if (couplerNetId == 0 || otherCouplerNetId == 0)
    //    {
    //        LogWarning($"SendTrainCouple failed. Coupler: {coupler.name} {couplerNetId}, OtherCoupler: {otherCoupler.name} {otherCouplerNetId}");
    //        return;
    //    }

    //    SendPacketToServer(new CommonTrainCouplePacket
    //    {
    //        NetId = couplerNetId, //coupler.train.GetNetId(),
    //        IsFrontCoupler = coupler.isFrontCoupler,
    //        Value = (byte)coupler.state,
    //        OtherNetId = otherCouplerNetId, //otherCoupler.train.GetNetId(),
    //        OtherState = (byte)otherCoupler.state,
    //        OtherCarIsFrontCoupler = otherCoupler.isFrontCoupler,
    //        PlayAudio = playAudio,
    //        ViaChainInteraction = viaChainInteraction
    //    }, DeliveryMethod.ReliableUnordered);
    //}

    public void SendHoseConnected(Coupler coupler, Coupler otherCoupler, bool playAudio)
    {
        ushort couplerNetId = coupler.train.GetNetId();
        ushort otherCouplerNetId = otherCoupler.train.GetNetId();

        if (couplerNetId == 0 || otherCouplerNetId == 0)
        {
            LogWarning($"SendHoseConnected failed. Coupler: {coupler?.train?.ID} {couplerNetId}, OtherCoupler: {otherCoupler?.train?.ID} {otherCouplerNetId}");
            return;
        }

        SendPacketToServer(new CommonHoseConnectedPacket
        {
            NetId = couplerNetId,
            IsFront = coupler.isFrontCoupler,
            OtherNetId = otherCouplerNetId,
            OtherIsFront = otherCoupler.isFrontCoupler,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendHoseDisconnected(Coupler coupler, bool playAudio)
    {
        ushort couplerNetId = coupler.train.GetNetId();

        if (couplerNetId == 0)
        {
            LogWarning($"SendHoseDisconnected failed. Coupler: {coupler.name} {couplerNetId}");
            return;
        }

        LogDebug(() => $"SendHoseDisconnected({coupler.train.ID}, {coupler.isFrontCoupler}, {playAudio})");

        SendPacketToServer(new CommonHoseDisconnectedPacket
        {
            NetId = couplerNetId,
            IsFront = coupler.isFrontCoupler,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendMuConnected(MultipleUnitCable cable, MultipleUnitCable otherCable, bool playAudio)
    {
        ushort cableNetId = cable.muModule.train.GetNetId();
        ushort otherCableNetId = otherCable.muModule.train.GetNetId();

        if (cableNetId == 0 || otherCableNetId == 0)
        {
            LogWarning($"SendMuConnected failed. Cable: {cable.muModule.train.name} {cableNetId}, OtherCable: {otherCable.muModule.train.name} {otherCableNetId}");
            return;
        }

        SendPacketToServer(new CommonMuConnectedPacket
        {
            NetId = cableNetId,
            IsFront = cable.isFront,
            OtherNetId = otherCableNetId,
            OtherIsFront = otherCable.isFront,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendMuDisconnected(ushort netId, MultipleUnitCable cable, bool playAudio)
    {

        SendPacketToServer(new CommonMuDisconnectedPacket
        {
            NetId = netId,
            IsFront = cable.isFront,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendCockState(ushort netId, Coupler coupler, bool isOpen)
    {
        SendPacketToServer(new CommonCockFiddlePacket
        {
            NetId = netId,
            IsFront = coupler.isFrontCoupler,
            IsOpen = isOpen
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendBrakeCylinderReleased(ushort netId)
    {
        SendPacketToServer(new CommonBrakeCylinderReleasePacket
        {
            NetId = netId
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendHandbrakePositionChanged(ushort netId, float position)
    {
        SendPacketToServer(new CommonHandbrakePositionPacket
        {
            NetId = netId,
            Position = position
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendAddCoal(ushort netId, float coalMassDelta)
    {
        SendPacketToServer(new ServerboundAddCoalPacket
        {
            NetId = netId,
            CoalMassDelta = coalMassDelta
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendTenderCoalPileInteraction(ushort netId, float coalMassDelta)
    {
        SendPacketToServer(new ServerboundTenderCoalPacket
        {
            NetId = netId,
            CoalMassDelta = coalMassDelta
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendFireboxIgnition(ushort netId)
    {
        SendPacketToServer(new ServerboundFireboxIgnitePacket
        {
            NetId = netId,
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendPorts(ushort netId, uint[] portIds, float[] portValues)
    {
        SendPacketToServer(new CommonTrainPortsPacket
        {
            NetId = netId,
            PortIds = portIds,
            PortValues = portValues
        }, DeliveryMethod.ReliableOrdered);

        /*
        string log=$"Sending ports netId: {netId}";
        for (int i = 0; i < portIds.Length; i++) {
            log += $"\r\n\t{portIds[i]}: {portValues[i]}";
        }

        LogDebug(() => log);
        */
    }

    public void SendFuses(ushort netId, uint[] fuseIds, bool[] fuseValues)
    {
        SendPacketToServer(new CommonTrainFusesPacket
        {
            NetId = netId,
            FuseIds = fuseIds,
            FuseValues = fuseValues
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendTrainSyncRequest(ushort netId)
    {
        SendPacketToServer(new ServerboundTrainSyncRequestPacket
        {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainDeleteRequest(ushort netId)
    {
        SendPacketToServer(new ServerboundTrainDeleteRequestPacket
        {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainRerailRequest(ushort netId, ushort trackId, Vector3 position, Vector3 forward)
    {
        SendPacketToServer(new ServerboundTrainRerailRequestPacket
        {
            NetId = netId,
            TrackId = trackId,
            Position = position,
            Forward = forward
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainSpawnRequest(string liveryId, ushort trackNetId, int position, bool withTrackDirection)
    {
        SendPacketToServer(new ServerboundTrainSpawnRequestPacket
        {
            LiveryId = liveryId,
            TrackNetId = trackNetId,
            Index = position,
            WithTrackDirection = withTrackDirection
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendWorkTrainRequest(uint ticketId, string liveryId, ushort trackNetId, int position, bool withTrackDirection)
    {
        SendPacketToServer(new ServerboundWorkTrainRequestPacket
        {
            TicketId = ticketId,
            LiveryId = liveryId,
            TrackNetId = trackNetId,
            Index = position,
            WithTrackDirection = withTrackDirection
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendLicensePurchaseRequest(string id, bool isJobLicense)
    {
        SendPacketToServer(new ServerboundLicensePurchaseRequestPacket
        {
            Id = id,
            IsJobLicense = isJobLicense
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendJobValidateRequest(NetworkedJob job, NetworkedStationController station)
    {
        SendPacketToServer(new ServerboundJobValidateRequestPacket
        {
            JobNetId = job.NetId,
            StationNetId = station.NetId,
            validationType = job.ValidationType
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendWarehouseRequest(WarehouseAction action, ushort netId)
    {
        SendPacketToServer(new ServerboundWarehouseMachineControllerRequestPacket
        {
            NetId = netId,
            WarehouseAction = action,
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendChat(string message)
    {
        SendPacketToServer(new CommonChatPacket
        {
            message = message
        }, DeliveryMethod.ReliableUnordered);

    }

    public void SendPitStopInteractionPacket(ushort netId, PitStopStationInteractionType interaction, ResourceType? resource, float state)
    {
        LogDebug(() => $"SendPitStopInteractionPacket({netId}, [{interaction}], {resource}, {state})");

        int res = resource == null ? 0 : (int)resource;
        SendPacketToServer(new CommonPitStopInteractionPacket
        {
            Tick = NetworkLifecycle.Instance.Tick,
            NetId = netId,
            InteractionType = interaction,
            ResourceType = res,
            Value = state
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendPitStopPlugInteractionPacket
    (
        ushort netId,
        PlugInteractionType interaction,
        Vector3? position = null,
        Quaternion? rotation = null,
        ushort trainCarNetId = 0,
        sbyte socketIndex = -1
    )
    {
        LogDebug(() => $"SendPitStopPlugInteractionPacket({netId}, {interaction}, pos: {position}, rot: {rotation}, trainNetId: {trainCarNetId}, socketIndex: {socketIndex})");

        SendNetSerializablePacketToServer(new CommonPitStopPlugInteractionPacket
        {
            NetId = netId,
            InteractionType = interaction,
            TrainCarNetId = trainCarNetId,
            SocketIndex = socketIndex,
            Position = position,
            Rotation = rotation,

        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendItemsChangePacket(List<ItemUpdateData> items)
    {
        Log($"Sending CommonItemChangePacket with {items.Count()} items");
        //SendPacketToServer(new CommonItemChangePacket { Items = items },
        //    DeliveryMethod.ReliableUnordered);

        SendNetSerializablePacketToServer(new CommonItemChangePacket { Items = items },
                DeliveryMethod.ReliableOrdered);
    }

    public void SendPaintThemeChange(NetworkedTrainCar netTraincar, TrainCarPaint.Target targetArea, uint themeId)
    {
        Log($"Sending paint theme change for {netTraincar.CurrentID}");

        SendPacketToServer(new CommonPaintThemePacket { NetId = netTraincar.NetId, TargetArea = targetArea, PaintThemeId = themeId }, DeliveryMethod.ReliableUnordered);
    }

    public void SendCashRegisterAction(ushort netId, CashRegisterAction action, double amount = 0.0f)
    {
        SendPacketToServer(
            new CommonCashRegisterWithModulesActionPacket
            {
                NetId = netId,
                Action = action,
                Amount = amount
            },
            DeliveryMethod.ReliableOrdered
        );
    }

    public void SendTrainControlAuthorityRequest(ushort netId, uint portNetId, bool requestAuthority)
    {
        SendPacketToServer
        (
            new ServerboundTrainControlAuthorityPacket
            {
                NetId = netId,
                PortNetId = portNetId,
                RequestAuthority = requestAuthority
            },
            DeliveryMethod.ReliableOrdered
        );
    }

    public void SendGenericSwitchState(uint netId, bool isOn)
    {
        SendPacketToServer
        (
            new CommonGenericSwitchStatePacket
            {
                NetId = netId,
                IsOn = isOn
            },
            deliveryMethod: DeliveryMethod.ReliableOrdered
        );
    }
    #endregion
}
