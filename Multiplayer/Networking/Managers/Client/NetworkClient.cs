using System;
using System.Net;
using System.Text;
using System.Collections.Generic;
using DV;
using DV.Damage;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.MultipleUnit;
using DV.ServicePenalty.UI;
using DV.ThingTypes;
using DV.UI;
using DV.WeatherSystem;
using LiteNetLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.UI;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Clientbound.Jobs;
using Multiplayer.Networking.Packets.Clientbound.SaveGame;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Networking.Packets.Clientbound.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Patches.SaveGame;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;
using Object = UnityEngine.Object;
using Multiplayer.Networking.Packets.Serverbound.Train;
using System.Linq;
using LiteNetLib.Utils;
using DV.UserManagement;
using DV.Common;
using DV.Customization.Paint;
using Multiplayer.Networking.TransportLayers;
using System.Collections;

namespace Multiplayer.Networking.Managers.Client;

public class NetworkClient : NetworkManager
{
    protected override string LogPrefix => "[Client]";

    private Action<DisconnectReason, string> onDisconnect;
    private string disconnectMessage;

    public ITransportPeer SelfPeer { get; private set; }
    public readonly ClientPlayerManager ClientPlayerManager;

    // One way ping in milliseconds
    public int Ping { get; private set; }
    private ITransportPeer serverPeer;

    private ChatGUI chatGUI;
    public bool isSinglePlayer;

    private bool isAlsoHost;
    IGameSession originalSession;

    public NetworkClient(Settings settings) : base(settings)
    {
        ClientPlayerManager = new ClientPlayerManager();
    }

    public void Start(string address, int port, string password, bool isSinglePlayer, Action<DisconnectReason, string> onDisconnect)
    {
        LogDebug(() => $"NetworkClient Constructor");

        this.onDisconnect = onDisconnect;
        //netManager.Start();
        base.Start();

        ServerboundClientLoginPacket serverboundClientLoginPacket = new()
        {
            Username = Multiplayer.Settings.GetUserName(),
            Guid = Multiplayer.Settings.GetGuid().ToByteArray(),
            Password = password,
            BuildMajorVersion = (ushort)BuildInfo.BUILD_VERSION_MAJOR,
            Mods = ModInfo.FromModEntries(UnityModManager.modEntries)
        };
        netPacketProcessor.Write(cachedWriter, serverboundClientLoginPacket);
        SelfPeer = Connect(address, port, cachedWriter);

        isAlsoHost = NetworkLifecycle.Instance.IsServerRunning;
        originalSession = UserManager.Instance.CurrentUser.CurrentSession;

        LogDebug(() => $"NetworkClient.Start() isAlsoHost: {isAlsoHost}, Original session is Null: {originalSession == null}");
    }

    public override void Stop()
    {
        if (!isAlsoHost && originalSession != null)
        {
            LogDebug(() => $"NetworkClient.Stop() destroying session... Original session is Null: {originalSession == null}");
            //IGameSession session = UserManager.Instance.CurrentUser.CurrentSession;
            Client_GameSession.SetCurrent(originalSession);
            //session?.Dispose();
        }

        base.Stop();
    }

    protected override void Subscribe()
    {
        netPacketProcessor.SubscribeReusable<ClientboundServerLoadingPacket>(OnClientboundServerLoadingPacket);
        netPacketProcessor.SubscribeReusable<ClientboundLoginResponsePacket>(OnClientboundLoginResponsePacket);
        netPacketProcessor.SubscribeReusable<ClientboundDisconnectPacket>(OnClientboundDisconnectPacket);
        netPacketProcessor.SubscribeReusable<ClientboundRemoveLoadingScreenPacket>(OnClientboundRemoveLoadingScreen);

        netPacketProcessor.SubscribeReusable<ClientboundTickSyncPacket>(OnClientboundTickSyncPacket);
        netPacketProcessor.SubscribeReusable<ClientboundBeginWorldSyncPacket>(OnClientboundBeginWorldSyncPacket);
        netPacketProcessor.SubscribeReusable<ClientboundGameParamsPacket>(OnClientboundGameParamsPacket);
        netPacketProcessor.SubscribeReusable<ClientboundSaveGameDataPacket>(OnClientboundSaveGameDataPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWeatherPacket>(OnClientboundWeatherPacket);
        netPacketProcessor.SubscribeReusable<ClientboundRailwayStatePacket>(OnClientboundRailwayStatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundStationControllerLookupPacket>(OnClientboundStationControllerLookupPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPitStopStationLookupPacket>(OnClientboundPitStopStationLookupPacket);


        netPacketProcessor.SubscribeReusable<ClientboundPlayerJoinedPacket>(OnClientboundPlayerJoinedPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerDisconnectPacket>(OnClientboundPlayerDisconnectPacket);

        netPacketProcessor.SubscribeReusable<ClientboundPlayerPositionPacket>(OnClientboundPlayerPositionPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPingUpdatePacket>(OnClientboundPingUpdatePacket);

        netPacketProcessor.SubscribeReusable<ClientboundTimeAdvancePacket>(OnClientboundTimeAdvancePacket);
        netPacketProcessor.SubscribeReusable<CommonChangeJunctionPacket>(OnCommonChangeJunctionPacket);
        netPacketProcessor.SubscribeReusable<CommonRotateTurntablePacket>(OnCommonRotateTurntablePacket);
        netPacketProcessor.SubscribeReusable<ClientboundSpawnTrainCarPacket>(OnClientboundSpawnTrainCarPacket);
        netPacketProcessor.SubscribeReusable<ClientboundSpawnTrainSetPacket>(OnClientboundSpawnTrainSetPacket);
        netPacketProcessor.SubscribeReusable<ClientboundDestroyTrainCarPacket>(OnClientboundDestroyTrainCarPacket);
        netPacketProcessor.SubscribeReusable<ClientboundTrainsetPhysicsPacket>(OnClientboundTrainPhysicsPacket);
        netPacketProcessor.SubscribeReusable<CommonCouplerInteractionPacket>(OnCommonCouplerInteractionPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainCouplePacket>(OnCommonTrainCouplePacket);
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
        netPacketProcessor.SubscribeReusable<ClientboundBrakeStateUpdatePacket>(OnClientboundBrakeStateUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundFireboxStatePacket>(OnClientboundFireboxStatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundCargoStatePacket>(OnClientboundCargoStatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundCarHealthUpdatePacket>(OnClientboundCarHealthUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundRerailTrainPacket>(OnClientboundRerailTrainPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWindowsBrokenPacket>(OnClientboundWindowsBrokenPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWindowsRepairedPacket>(OnClientboundWindowsRepairedPacket);
        netPacketProcessor.SubscribeReusable<ClientboundMoneyPacket>(OnClientboundMoneyPacket);
        netPacketProcessor.SubscribeReusable<ClientboundLicenseAcquiredPacket>(OnClientboundLicenseAcquiredPacket);
        netPacketProcessor.SubscribeReusable<ClientboundGarageUnlockPacket>(OnClientboundGarageUnlockPacket);
        netPacketProcessor.SubscribeReusable<ClientboundDebtStatusPacket>(OnClientboundDebtStatusPacket);
        netPacketProcessor.SubscribeReusable<ClientboundJobsUpdatePacket>(OnClientboundJobsUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundJobsCreatePacket>(OnClientboundJobsCreatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundJobValidateResponsePacket>(OnClientboundJobValidateResponsePacket);
        netPacketProcessor.SubscribeReusable<CommonChatPacket>(OnCommonChatPacket);
        netPacketProcessor.SubscribeNetSerializable<CommonItemChangePacket>(OnCommonItemChangePacket);

        netPacketProcessor.SubscribeReusable<CommonPitStopInteractionPacket>(OnCommonPitStopInteractionPacket);
    }

    #region Net Events

    public override void OnPeerConnected(ITransportPeer peer)
    {
        serverPeer = peer;
    }

    public override void OnPeerDisconnected(ITransportPeer peer, DisconnectReason disconnectReason)
    {

        LogDebug(()=>$"OnPeerDisconnected({peer.Id}, {disconnectReason}) disconnect message: {disconnectMessage}");

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
            Log($"Received player accepted packet");
            
            if (NetworkLifecycle.Instance.IsHost(SelfPeer))
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
                if (packet.Extra.Length != 0)
                    text += "\n";
            }

            if (packet.Extra.Length != 0)
                text += Locale.Get(Locale.DISCONN_REASON__MODS_EXTRA_KEY, placeholders: string.Join("\n - ", packet.Extra));
        }

        Log($"Received player deny packet: {text}");
        onDisconnect(DisconnectReason.ConnectionRejected, text);
    }

    private void OnClientboundPlayerJoinedPacket(ClientboundPlayerJoinedPacket packet)
    {
        //Guid guid = new(packet.Guid);
        ClientPlayerManager.AddPlayer(packet.Id, packet.Username);

        ClientPlayerManager.UpdatePosition(packet.Id, packet.Position, Vector3.zero, packet.Rotation, false, packet.CarID != 0, packet.CarID);
    }

    //For other player left the game
    private void OnClientboundPlayerDisconnectPacket(ClientboundPlayerDisconnectPacket packet)
    {
        Log($"Received player disconnect packet for player id: {packet.Id}");
        ClientPlayerManager.RemovePlayer(packet.Id);
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
            disconnectMessage = "Server Shutting Down";
        }
    }
    private void OnClientboundPlayerPositionPacket(ClientboundPlayerPositionPacket packet)
    {
        ClientPlayerManager.UpdatePosition(packet.Id, packet.Position, packet.MoveDir, packet.RotationY, packet.IsJumping, packet.IsOnCar, packet.CarID);
    }

    private void OnClientboundPingUpdatePacket(ClientboundPingUpdatePacket packet)
    {
        ClientPlayerManager.UpdatePing(packet.Id, packet.Ping);
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
        if (Globals.G.gameParams != null)
            packet.Apply(Globals.G.gameParams);
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
        WorldStreamingInit.LoadingFinished += () =>
        {
            Log($"WorldStreamingInit.LoadingFinished()");
            NetworkedItemManager.Instance.CheckInstance();
            Log($"WorldStreamingInit.LoadingFinished() CacheWorldItems()");
            NetworkedItemManager.Instance.CacheWorldItems();
            Log($"WorldStreamingInit.LoadingFinished() InitialisePitStops()"); 
            NetworkedPitStopStation.InitialisePitStops();
            Log($"WorldStreamingInit.LoadingFinished() SendReadyPacket()");
            SendReadyPacket();
        };

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
        WeatherDriver.Instance.LoadSaveData(JObject.FromObject(packet));
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

    //Force pitstops to be mapped to same netId across all clients and server - probably should implement for junctions, etc.
    private void OnClientboundPitStopStationLookupPacket(ClientboundPitStopStationLookupPacket packet)
    {
        LogDebug(() => $"OnClientboundPitStopStationLookupPacket({packet.NetIds?.Length})");

        if (packet == null)
        {
            LogError("OnClientboundPitStopStationLookupPacket received null packet");
            return;
        }

        if (packet.NetIds == null || packet.Locations == null)
        {
            LogError($"OnClientboundPitStopStationLookupPacket received packet with null arrays: NetIDs is null: {packet.NetIds == null}, Locations is null: {packet.Locations == null}");
            return;
        }

        //Log($"WorldStreamingInit.LoadingFinished() CachePitStopStations()");
        //NetworkedPitStopStation.InitialisePitStops();

        for (int i = 0; i < packet.NetIds.Length; i++)
        {
            LogDebug(() => $"OnClientboundPitStopStationLookupPacket[{i}] vector: {packet.Locations[i]}, netId: {packet.NetIds[i]}");
            if (NetworkedPitStopStation.GetFromVector(packet.Locations[i], out  NetworkedPitStopStation netStation))
            {
                netStation.NetId = packet.NetIds[i];
                if (netStation.Station.pitstop != null)
                {
                    netStation.Station.pitstop.currentCarIndex = packet.SelectedCars[i];
                    //netStation.Station.pitstop.OnCarSelectionChanged();
                }
            }
            else
                LogError($"Syncing PitStopStations station with coords: {packet.Locations[i]} not found");
        }
    }

     private void OnClientboundRailwayStatePacket(ClientboundRailwayStatePacket packet)
    {
        for (int i = 0; i < packet.SelectedJunctionBranches.Length; i++)
        {
            if (!NetworkedJunction.Get((ushort)(i + 1), out NetworkedJunction junction))
                return;
            junction.Switch((byte)Junction.SwitchMode.NO_SOUND, packet.SelectedJunctionBranches[i]);
        }

        for (int i = 0; i < packet.TurntableRotations.Length; i++)
        {
            if (!NetworkedTurntable.Get((byte)(i + 1), out NetworkedTurntable turntable))
                return;
            turntable.SetRotation(packet.TurntableRotations[i], true);
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

    private void OnClientboundSpawnTrainCarPacket(ClientboundSpawnTrainCarPacket packet)
    {
        TrainsetSpawnPart spawnPart = packet.SpawnPart;

        LogDebug(() => $"Spawning {spawnPart.CarId} ({spawnPart.LiveryId}) with net ID {spawnPart.NetId}");

        NetworkedCarSpawner.SpawnCar(spawnPart);

        SendTrainSyncRequest(spawnPart.NetId);
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
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        //Protect myself from getting deleted in race conditions
        if (PlayerManager.Car == networkedTrainCar.TrainCar)
        {
            LogWarning($"Server attempted to delete car I'm on: {PlayerManager.Car?.ID}, net ID: {packet?.NetId}");
            PlayerManager.SetCar(null);
        }

        //Protect other players from getting deleted in race conditions - this should be a temporary fix, if another playe's game object is deleted we should just recreate it
        if(networkedTrainCar == null || networkedTrainCar.gameObject == null || networkedTrainCar.TrainCar == null)
        {
            LogDebug(() => $"OnClientboundDestroyTrainCarPacket({packet?.NetId}) networkedTrainCar: {networkedTrainCar != null}, go: {(networkedTrainCar?.gameObject) != null}, trainCar: {networkedTrainCar?.TrainCar != null}");
        }
        else
        {
            NetworkedPlayer[] componentsInChildren = (networkedTrainCar?.gameObject != null) ? networkedTrainCar.GetComponentsInChildren<NetworkedPlayer>() : [];

            foreach (NetworkedPlayer networkedPlayer in componentsInChildren)
            {
                networkedPlayer.UpdateCar(0);
            }

            networkedTrainCar.TrainCar.UpdateJobIdOnCarPlates(string.Empty);
            CarSpawner.Instance.DeleteCar(networkedTrainCar.TrainCar);
        }
    }

    public void OnClientboundTrainPhysicsPacket(ClientboundTrainsetPhysicsPacket packet)
    {
        NetworkTrainsetWatcher.Instance.Client_HandleTrainsetPhysicsUpdate(packet);
    }

    private void OnCommonCouplerInteractionPacket(CommonCouplerInteractionPacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out var netTrainCar))
        {
            LogError($"OnCommonCouplerInteractionPacket netId: {packet.NetId}, TrainCar not found!");
            return;
        }

        netTrainCar.Common_ReceiveCouplerInteraction(packet);
    }
    private void OnCommonTrainCouplePacket(CommonTrainCouplePacket packet)
    {
        //    TrainCar trainCar = null;
        //    TrainCar otherTrainCar = null;

        //    if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out trainCar) || !NetworkedTrainCar.GetTrainCar(packet.OtherNetId, out otherTrainCar))
        //    {
        //        LogDebug(() => $"OnCommonTrainCouplePacket() netId: {packet.NetId}, trainCar found?: {trainCar != null}, otherNetId: {packet.OtherNetId}, otherTrainCar found?: {otherTrainCar != null}");
        //        return;
        //    }

        //    LogDebug(() => $"OnCommonTrainCouplePacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, otherNetId: {packet.OtherNetId}, otherTrainCar: {otherTrainCar.ID}");

        //    Coupler coupler = packet.IsFrontCoupler ? trainCar.frontCoupler : trainCar.rearCoupler;
        //    Coupler otherCoupler = packet.OtherCarIsFrontCoupler ? otherTrainCar.frontCoupler : otherTrainCar.rearCoupler;

        //    if (coupler.CoupleTo(otherCoupler, packet.PlayAudio, false/*B99 packet.ViaChainInteraction*/) == null)
        //        LogDebug(() => $"OnCommonTrainCouplePacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, otherNetId: {packet.OtherNetId}, otherTrainCar: {otherTrainCar.ID} Failed to couple!");
    }

    private void OnCommonTrainUncouplePacket(CommonTrainUncouplePacket packet)
    {
        //if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
        //{
        //    LogDebug(() => $"OnCommonTrainUncouplePacket() netId: {packet.NetId}, trainCar found?: {trainCar != null}");
        //    return;
        //}

        //LogDebug(() => $"OnCommonTrainUncouplePacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, isFront: {packet.IsFrontCoupler}, playAudio: {packet.PlayAudio}, DueToBrokenCouple: {packet.DueToBrokenCouple}, viaChainInteraction: {packet.ViaChainInteraction}");

        //Coupler coupler = packet.IsFrontCoupler ? trainCar.frontCoupler : trainCar.rearCoupler;
        //coupler.Uncouple(packet.PlayAudio, false, packet.DueToBrokenCouple, false/*B99 packet.ViaChainInteraction*/);
    }

    private void OnCommonHoseConnectedPacket(CommonHoseConnectedPacket packet)
    {
        bool foundTrainCar = NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar);
        bool foundOtherTrainCar = NetworkedTrainCar.GetTrainCar(packet.OtherNetId, out TrainCar otherTrainCar);

        if (!foundTrainCar || trainCar == null ||
            !foundOtherTrainCar || otherTrainCar == null)
        {
            LogError($"OnCommonHoseConnectedPacket() netId: {packet.NetId}, trainCar found: {foundTrainCar}, trainCar is null: {trainCar == null}, otherNetId: {packet.OtherNetId}, otherTrainCar found: {foundOtherTrainCar}, other trainCar is null:  {otherTrainCar == null}");
            return;
        }

        string carId = $"[{ trainCar?.ID}, { packet.NetId}]";
        string otherCarId = $"[{ otherTrainCar?.ID}, { packet.OtherNetId}]";

        LogDebug(() => $"OnCommonHoseConnectedPacket() trainCar: {carId}, isFront: {packet.IsFront}, otherTrainCar: {otherCarId}, isFront: {packet.OtherIsFront}, playAudio: {packet.PlayAudio}");

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

            if(coupler?.hoseAndCock?.connectedTo != null)
                NetworkedTrainCar.TryGetCoupler(coupler.hoseAndCock.connectedTo, out connectedTo);
            if(otherCoupler?.hoseAndCock?.connectedTo != null)
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
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar netTrainCar) || netTrainCar.IsDestroying)
            return;

        TrainCar trainCar = netTrainCar.TrainCar;

        LogDebug(() => $"OnCommonHoseDisconnectedPacket() netId: {packet.NetId}, trainCar: {trainCar.ID}, isFront: {packet.IsFront}, playAudio: {packet.PlayAudio}");

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;

        coupler.DisconnectAirHose(packet.PlayAudio);
    }

    private void OnCommonMuConnectedPacket(CommonMuConnectedPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar) || !NetworkedTrainCar.GetTrainCar(packet.OtherNetId, out TrainCar otherTrainCar))
            return;

        MultipleUnitCable cable = packet.IsFront ? trainCar.muModule.frontCable : trainCar.muModule.rearCable;
        MultipleUnitCable otherCable = packet.OtherIsFront ? otherTrainCar.muModule.frontCable : otherTrainCar.muModule.rearCable;

        cable.Connect(otherCable, packet.PlayAudio);
    }

    private void OnCommonMuDisconnectedPacket(CommonMuDisconnectedPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        MultipleUnitCable cable = packet.IsFront ? trainCar.muModule.frontCable : trainCar.muModule.rearCable;

        cable.Disconnect(packet.PlayAudio);
    }

    private void OnCommonCockFiddlePacket(CommonCockFiddlePacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;

        coupler.IsCockOpen = packet.IsOpen;
    }

    private void OnCommonBrakeCylinderReleasePacket(CommonBrakeCylinderReleasePacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        trainCar.brakeSystem.ReleaseBrakeCylinderPressure();
    }

    private void OnCommonHandbrakePositionPacket(CommonHandbrakePositionPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        trainCar.brakeSystem.SetHandbrakePosition(packet.Position);
    }

    private void OnCommonSimFlowPacket(CommonTrainPortsPacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.Common_UpdatePorts(packet);
    }

    private void OnCommonTrainFusesPacket(CommonTrainFusesPacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.Common_UpdateFuses(packet);
    }

    private void OnClientboundBrakeStateUpdatePacket(ClientboundBrakeStateUpdatePacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;


        networkedTrainCar.Client_ReceiveBrakeStateUpdate(packet);

        //LogDebug(() => $"Received Brake Pressures netId {packet.NetId}: {packet.MainReservoirPressure}, {packet.IndependentPipePressure}, {packet.BrakePipePressure}, {packet.BrakeCylinderPressure}");
    }

    private void OnClientboundFireboxStatePacket(ClientboundFireboxStatePacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;


        networkedTrainCar.Client_ReceiveFireboxStateUpdate(packet.Contents, packet.IsOn);
    }

    private void OnClientboundCargoStatePacket(ClientboundCargoStatePacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.CargoModelIndex = packet.CargoModelIndex;
        Car logicCar = networkedTrainCar.TrainCar.logicCar;

        if (logicCar == null)
        {
            Multiplayer.LogWarning($"OnClientboundCargoStatePacket() Failed to find logic car for [{networkedTrainCar.TrainCar.ID}, {packet.NetId}] is initialised: {networkedTrainCar.Client_Initialized}");
            return;
        }

        if (packet.CargoType == (ushort)CargoType.None && logicCar.CurrentCargoTypeInCar == CargoType.None)
            return;

        //packet.CargoAmount is the total amount, not the amount to load/unload
        float cargoAmount = Mathf.Clamp(packet.CargoAmount, 0, logicCar.capacity);

        // todo: cache warehouse machine
        WarehouseMachine warehouse = string.IsNullOrEmpty(packet.WarehouseMachineId) ? null : JobSaveManager.Instance.GetWarehouseMachineWithId(packet.WarehouseMachineId);
        if (packet.IsLoading)
        {
            //Check correct cargo is loaded and the amount is correct
            if (logicCar.LoadedCargoAmount == cargoAmount && logicCar.CurrentCargoTypeInCar == (CargoType)packet.CargoType)
                return;

            //We need either no cargo or the same cargo - if it's different, we need to remove it first
            if (logicCar.CurrentCargoTypeInCar != CargoType.None && logicCar.CurrentCargoTypeInCar != (CargoType)packet.CargoType)
                logicCar.DumpCargo();

            //We have the correct cargo, but not the right amount, calculate the delta
            if (logicCar.CurrentCargoTypeInCar == (CargoType)packet.CargoType)
                cargoAmount -= logicCar.LoadedCargoAmount;

            if (cargoAmount > 0)
                logicCar.LoadCargo(cargoAmount, (CargoType)packet.CargoType, warehouse);
        }
        else
        {
            //Check correct cargo is loaded and the amount is correct
            if (logicCar.LoadedCargoAmount == cargoAmount && logicCar.CurrentCargoTypeInCar == (CargoType)packet.CargoType)
                return;

            //If there is different cargo we need to remove it, then load the appropriate amount
            if (logicCar.CurrentCargoTypeInCar == CargoType.None || logicCar.CurrentCargoTypeInCar != (CargoType)packet.CargoType)
            {
                //avoid triggering the load event by backdooring it
                logicCar.LastUnloadedCargoType = logicCar.CurrentCargoTypeInCar;
                logicCar.CurrentCargoTypeInCar = (CargoType)packet.CargoType;
                logicCar.LoadedCargoAmount = cargoAmount;
            }

            //We have the correct cargo, calculate the delta
            if (logicCar.CurrentCargoTypeInCar == (CargoType)packet.CargoType)
                cargoAmount = logicCar.LoadedCargoAmount - cargoAmount;

            if (cargoAmount > 0)
                logicCar.UnloadCargo(cargoAmount, (CargoType)packet.CargoType, warehouse);
        }
    }

    private void OnClientboundCarHealthUpdatePacket(ClientboundCarHealthUpdatePacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        packet.Health.LoadTo(trainCar);
        //todo test new code thoroughly
        //check that career and comms radio repairs and damage work as expected

        //CarDamageModel carDamage = trainCar.CarDamage;
        //float difference = Mathf.Abs(packet.Health - carDamage.currentHealth);
        //if (difference < 0.0001)
        //    return;

        //if (packet.Health < carDamage.currentHealth)
        //    carDamage.DamageCar(difference);
        //else
        //    carDamage.RepairCar(difference);
    }

    private void OnClientboundRerailTrainPacket(ClientboundRerailTrainPacket packet)
    {
        
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;
        if (!NetworkedRailTrack.Get(packet.TrackId, out NetworkedRailTrack networkedRailTrack))
            return;

        Log($"Rerailing [{trainCar?.ID}, {packet.NetId}] to track {networkedRailTrack?.RailTrack?.logicTrack?.ID}");
        LogDebug(() => $"Rerailing [{trainCar?.ID}, {packet.NetId}] track: [{networkedRailTrack?.RailTrack?.logicTrack?.ID}, {packet.TrackId}], raw position: {packet.Position}, adjusted position: {packet.Position + WorldMover.currentMove}, forward: {packet.Forward}");
        trainCar.Rerail(networkedRailTrack.RailTrack, packet.Position + WorldMover.currentMove, packet.Forward);
    }

    private void OnClientboundWindowsBrokenPacket(ClientboundWindowsBrokenPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
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
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
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
            LogWarning($"Pit stop Interaction received for netId: {packet.NetId}, but pit stop does not exist!");
        }

        Log($"Pit stop interaction received for {netPitStop.StationName}");

        LogDebug(() => $"OnCommonPitStopInteractionPacket() [{netPitStop.StationName}, {packet.NetId}], interaction: [{packet.InteractionType}], resource: {packet?.ResourceType}, State: {packet.State}");
        netPitStop.ProcessPacket(packet);
    }


    private void OnCommonItemChangePacket(CommonItemChangePacket packet)
    {
        //LogDebug(() => $"OnCommonItemChangePacket({packet?.Items?.Count})");


        //Multiplayer.LogDebug(() =>
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
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar netTrainCar))
            return;

        Log($"Received paint theme change for {netTrainCar?.CurrentID}");

        PaintTheme paint = PaintThemeLookup.Instance.GetPaintTheme(packet.PaintThemeId);

        if (paint == null)
        {
            LogWarning($"Paint theme index {packet.PaintThemeId} does not exist!");
            return;
        }

        if (!Enum.IsDefined(typeof(TrainCarPaint.Target), packet.TargetArea))
        {
            LogWarning($"TrainCarPaint Target {packet.TargetArea} is not defined!");
            return;
        }

        LogDebug(() => $"OnCommonPaintThemePacket() [{netTrainCar?.CurrentID}, {packet.NetId}], area: {(TrainCarPaint.Target)packet.TargetArea}, paint: [{paint?.assetName}, {packet.PaintThemeId}]");
        netTrainCar?.Common_ReceivePaintThemeUpdate((TrainCarPaint.Target)packet.TargetArea, paint);
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

    public void SendSaveGameDataRequest()
    {
        SendPacketToServer(new ServerboundSaveGameDataRequestPacket(), DeliveryMethod.ReliableOrdered);
    }

    private void SendReadyPacket()
    {
        Log("World loaded, sending ready packet");
        SendPacketToServer(new ServerboundClientReadyPacket(), DeliveryMethod.ReliableOrdered);
    }

    public void SendPlayerPosition(Vector3 position, Vector3 moveDir, float rotationY, ushort carId, bool isJumping, bool isOnCar, bool reliable)
    {
        //LogDebug(() => $"SendPlayerPosition({position}, {moveDir}, {rotationY}, {carId}, {isJumping}, {isOnCar})");

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
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendJunctionSwitched(ushort netId, byte selectedBranch, Junction.SwitchMode mode)
    {
        SendPacketToServer(new CommonChangeJunctionPacket
        {
            NetId = netId,
            SelectedBranch = selectedBranch,
            Mode = (byte)mode
        }, DeliveryMethod.ReliableUnordered);
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


    public void SendTrainCouple(Coupler coupler, Coupler otherCoupler, bool playAudio, bool viaChainInteraction)
    {
        ushort couplerNetId = coupler.train.GetNetId();
        ushort otherCouplerNetId = otherCoupler.train.GetNetId();

        if (couplerNetId == 0 || otherCouplerNetId == 0)
        {
            LogWarning($"SendTrainCouple failed. Coupler: {coupler.name} {couplerNetId}, OtherCoupler: {otherCoupler.name} {otherCouplerNetId}");
            return;
        }

        SendPacketToServer(new CommonTrainCouplePacket
        {
            NetId = couplerNetId, //coupler.train.GetNetId(),
            IsFrontCoupler = coupler.isFrontCoupler,
            State = (byte)coupler.state,
            OtherNetId = otherCouplerNetId, //otherCoupler.train.GetNetId(),
            OtherState = (byte)otherCoupler.state,
            OtherCarIsFrontCoupler = otherCoupler.isFrontCoupler,
            PlayAudio = playAudio,
            ViaChainInteraction = viaChainInteraction
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainUncouple(Coupler coupler, bool playAudio, bool dueToBrokenCouple, bool viaChainInteraction)
    {
        ushort couplerNetId = coupler.train.GetNetId();

        if (couplerNetId == 0)
        {
            LogWarning($"SendTrainUncouple failed. Coupler: {coupler.name} {couplerNetId}");
            return;
        }

        SendPacketToServer(new CommonTrainUncouplePacket
        {
            NetId = couplerNetId,
            IsFrontCoupler = coupler.isFrontCoupler,
            PlayAudio = playAudio,
            ViaChainInteraction = viaChainInteraction,
            DueToBrokenCouple = dueToBrokenCouple
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendHoseConnected(Coupler coupler, Coupler otherCoupler, bool playAudio)
    {
        ushort couplerNetId = coupler.train.GetNetId();
        ushort otherCouplerNetId = otherCoupler.train.GetNetId();

        if (couplerNetId == 0 || otherCouplerNetId == 0)
        {
            LogWarning($"SendHoseConnected failed. Coupler: {coupler.name} {couplerNetId}, OtherCoupler: {otherCoupler.name} {otherCouplerNetId}");
            return;
        }

        SendPacketToServer(new CommonHoseConnectedPacket
        {
            NetId = couplerNetId,
            IsFront = coupler.isFrontCoupler,
            OtherNetId = otherCouplerNetId,
            OtherIsFront = otherCoupler.isFrontCoupler,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendHoseDisconnected(Coupler coupler, bool playAudio)
    {
        ushort couplerNetId = coupler.train.GetNetId();

        if (couplerNetId == 0)
        {
            LogWarning($"SendHoseDisconnected failed. Coupler: {coupler.name} {couplerNetId}");
            return;
        }

        SendPacketToServer(new CommonHoseDisconnectedPacket
        {
            NetId = couplerNetId,
            IsFront = coupler.isFrontCoupler,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
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
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendMuDisconnected(ushort netId, MultipleUnitCable cable, bool playAudio)
    {

        SendPacketToServer(new CommonMuDisconnectedPacket
        {
            NetId = netId,
            IsFront = cable.isFront,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendCockState(ushort netId, Coupler coupler, bool isOpen)
    {
        SendPacketToServer(new CommonCockFiddlePacket
        {
            NetId = netId,
            IsFront = coupler.isFrontCoupler,
            IsOpen = isOpen
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendBrakeCylinderReleased(ushort netId)
    {
        SendPacketToServer(new CommonBrakeCylinderReleasePacket
        {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered);
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

    public void SendFireboxIgnition(ushort netId)
    {
        SendPacketToServer(new ServerboundFireboxIgnitePacket
        {
            NetId = netId,
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendPorts(ushort netId, string[] portIds, float[] portValues)
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

    public void SendFuses(ushort netId, string[] fuseIds, bool[] fuseValues)
    {
        SendPacketToServer(new CommonTrainFusesPacket
        {
            NetId = netId,
            FuseIds = fuseIds,
            FuseValues = fuseValues
        }, DeliveryMethod.ReliableUnordered);
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

    public void SendChat(string message)
    {
        SendPacketToServer(new CommonChatPacket
        {
            message = message
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendPitStopInteractionPacket(ushort netId, PitStopStationInteractionType interaction, ResourceType? resource, float state)
    {
        Multiplayer.LogDebug(()=>$"SendPitStopInteractionPacket({netId}, [{interaction}], {resource}, {state}");

        int res = resource == null ? 0 : (int)resource;
        SendPacketToServer(new CommonPitStopInteractionPacket
        {
            NetId = netId,
            InteractionType = (byte)interaction,
            ResourceType = res,
            State = state
        }, DeliveryMethod.ReliableOrdered);

    }

    public void SendItemsChangePacket(List<ItemUpdateData> items)
    {
        Multiplayer.Log($"Sending SendItemsChangePacket with {items.Count()} items");
        //SendPacketToServer(new CommonItemChangePacket { Items = items },
        //    DeliveryMethod.ReliableUnordered);

        SendNetSerializablePacketToServer(new CommonItemChangePacket { Items = items },
                DeliveryMethod.ReliableOrdered);
    }

    public void SendPaintThemeChangePacket(ushort netId, byte targetArea, sbyte themeIndex)
    {
        SendPacketToServer(new CommonPaintThemePacket { NetId = netId, TargetArea = targetArea, PaintThemeId = themeIndex }, DeliveryMethod.ReliableUnordered);
    }

    #endregion
}
