using System;
using System.Text;
using DV;
using DV.Damage;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.MultipleUnit;
using DV.ServicePenalty.UI;
using DV.ThingTypes;
using DV.UI;
using DV.UIFramework;
using DV.WeatherSystem;
using LiteNetLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Clientbound.SaveGame;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Networking.Packets.Clientbound.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Patches.SaveGame;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;
using Object = UnityEngine.Object;

namespace Multiplayer.Networking.Listeners;

public class NetworkClient : NetworkManager
{
    protected override string LogPrefix => "[Client]";

    public NetPeer selfPeer { get; private set; }
    public readonly ClientPlayerManager PlayerManager;

    // One way ping in milliseconds
    public int Ping { get; private set; }
    private NetPeer serverPeer;

    public NetworkClient(Settings settings) : base(settings)
    {
        PlayerManager = new ClientPlayerManager();
    }

    public void Start(string address, int port, string password)
    {
        netManager.Start();
        ServerboundClientLoginPacket serverboundClientLoginPacket = new() {
            Username = Multiplayer.Settings.Username,
            Guid = Multiplayer.Settings.GetGuid().ToByteArray(),
            Password = password,
            BuildMajorVersion = (ushort)BuildInfo.BUILD_VERSION_MAJOR,
            Mods = ModInfo.FromModEntries(UnityModManager.modEntries)
        };
        netPacketProcessor.Write(cachedWriter, serverboundClientLoginPacket);
        selfPeer = netManager.Connect(address, port, cachedWriter);
    }

    protected override void Subscribe()
    {
        netPacketProcessor.SubscribeReusable<ClientboundServerDenyPacket>(OnClientboundServerDenyPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerJoinedPacket>(OnClientboundPlayerJoinedPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerDisconnectPacket>(OnClientboundPlayerDisconnectPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerPositionPacket>(OnClientboundPlayerPositionPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPlayerCarPacket>(OnClientboundPlayerCarPacket);
        netPacketProcessor.SubscribeReusable<ClientboundPingUpdatePacket>(OnClientboundPingUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundTickSyncPacket>(OnClientboundTickSyncPacket);
        netPacketProcessor.SubscribeReusable<ClientboundServerLoadingPacket>(OnClientboundServerLoadingPacket);
        netPacketProcessor.SubscribeReusable<ClientboundBeginWorldSyncPacket>(OnClientboundBeginWorldSyncPacket);
        netPacketProcessor.SubscribeReusable<ClientboundGameParamsPacket>(OnClientboundGameParamsPacket);
        netPacketProcessor.SubscribeReusable<ClientboundSaveGameDataPacket>(OnClientboundSaveGameDataPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWeatherPacket>(OnClientboundWeatherPacket);
        netPacketProcessor.SubscribeReusable<ClientboundRemoveLoadingScreenPacket>(OnClientboundRemoveLoadingScreen);
        netPacketProcessor.SubscribeReusable<ClientboundTimeAdvancePacket>(OnClientboundTimeAdvancePacket);
        netPacketProcessor.SubscribeReusable<ClientboundRailwayStatePacket>(OnClientboundRailwayStatePacket);
        netPacketProcessor.SubscribeReusable<CommonChangeJunctionPacket>(OnCommonChangeJunctionPacket);
        netPacketProcessor.SubscribeReusable<CommonRotateTurntablePacket>(OnCommonRotateTurntablePacket);
        netPacketProcessor.SubscribeReusable<ClientboundSpawnTrainCarPacket>(OnClientboundSpawnTrainCarPacket);
        netPacketProcessor.SubscribeReusable<ClientboundSpawnTrainSetPacket>(OnClientboundSpawnTrainSetPacket);
        netPacketProcessor.SubscribeReusable<ClientboundDestroyTrainCarPacket>(OnClientboundDestroyTrainCarPacket);
        netPacketProcessor.SubscribeReusable<ClientboundTrainsetPhysicsPacket>(OnClientboundTrainPhysicsPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainCouplePacket>(OnCommonTrainCouplePacket);
        netPacketProcessor.SubscribeReusable<CommonTrainUncouplePacket>(OnCommonTrainUncouplePacket);
        netPacketProcessor.SubscribeReusable<CommonHoseConnectedPacket>(OnCommonHoseConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonHoseDisconnectedPacket>(OnCommonHoseDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuConnectedPacket>(OnCommonMuConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuDisconnectedPacket>(OnCommonMuDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonCockFiddlePacket>(OnCommonCockFiddlePacket);
        netPacketProcessor.SubscribeReusable<CommonBrakeCylinderReleasePacket>(OnCommonBrakeCylinderReleasePacket);
        netPacketProcessor.SubscribeReusable<CommonHandbrakePositionPacket>(OnCommonHandbrakePositionPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainPortsPacket>(OnCommonSimFlowPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainFusesPacket>(OnCommonTrainFusesPacket);
        netPacketProcessor.SubscribeReusable<ClientboundCargoStatePacket>(OnClientboundCargoStatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundCarHealthUpdatePacket>(OnClientboundCarHealthUpdatePacket);
        netPacketProcessor.SubscribeReusable<ClientboundRerailTrainPacket>(OnClientboundRerailTrainPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWindowsBrokenPacket>(OnClientboundWindowsBrokenPacket);
        netPacketProcessor.SubscribeReusable<ClientboundWindowsRepairedPacket>(OnClientboundWindowsRepairedPacket);
        netPacketProcessor.SubscribeReusable<ClientboundMoneyPacket>(OnClientboundMoneyPacket);
        netPacketProcessor.SubscribeReusable<ClientboundLicenseAcquiredPacket>(OnClientboundLicenseAcquiredPacket);
        netPacketProcessor.SubscribeReusable<ClientboundGarageUnlockPacket>(OnClientboundGarageUnlockPacket);
        netPacketProcessor.SubscribeReusable<ClientboundDebtStatusPacket>(OnClientboundDebtStatusPacket);
    }

    #region Net Events

    public override void OnPeerConnected(NetPeer peer)
    {
        serverPeer = peer;
        if (NetworkLifecycle.Instance.IsHost(peer))
            SendReadyPacket();
        else
            SendSaveGameDataRequest();
    }

    public override void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        NetworkLifecycle.Instance.Stop();

        TrainStress.globalIgnoreStressCalculation = false;

        if (MainMenuThingsAndStuff.Instance != null)
        {
            MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
            NetworkLifecycle.Instance.TriggerMainMenuEventLater();
        }
        else
        {
            MainMenu.GoBackToMainMenu();
        }

        string text = $"{disconnectInfo.Reason}";

        switch (disconnectInfo.Reason)
        {
            case DisconnectReason.DisconnectPeerCalled:
            case DisconnectReason.ConnectionRejected:
                netPacketProcessor.ReadAllPackets(disconnectInfo.AdditionalData);
                return;
            case DisconnectReason.RemoteConnectionClose:
                text = "The server shut down";
                break;
        }

        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            Popup popup = MainMenuThingsAndStuff.Instance.ShowOkPopup();
            if (popup == null)
                return;
            popup.labelTMPro.text = text;
        });
    }

    public override void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        Ping = latency;
    }

    public override void OnConnectionRequest(ConnectionRequest request)
    {
        // todo
    }

    #endregion

    #region Listeners

    private void OnClientboundServerDenyPacket(ClientboundServerDenyPacket packet)
    {
        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            Popup popup = MainMenuThingsAndStuff.Instance.ShowOkPopup();
            if (popup == null)
                return;
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

            popup.labelTMPro.text = text;
        });
    }

    private void OnClientboundPlayerJoinedPacket(ClientboundPlayerJoinedPacket packet)
    {
        Guid guid = new(packet.Guid);
        PlayerManager.AddPlayer(packet.Id, packet.Username, guid);
        PlayerManager.UpdateCar(packet.Id, packet.TrainCar);
        PlayerManager.UpdatePosition(packet.Id, packet.Position, Vector3.zero, packet.Rotation, false, packet.TrainCar != 0);
    }

    private void OnClientboundPlayerDisconnectPacket(ClientboundPlayerDisconnectPacket packet)
    {
        Log($"Received player disconnect packet (Id: {packet.Id})");
        PlayerManager.RemovePlayer(packet.Id);
    }

    private void OnClientboundPlayerPositionPacket(ClientboundPlayerPositionPacket packet)
    {
        PlayerManager.UpdatePosition(packet.Id, packet.Position, packet.MoveDir, packet.RotationY, packet.IsJumping, packet.IsOnCar);
    }

    private void OnClientboundPlayerCarPacket(ClientboundPlayerCarPacket packet)
    {
        PlayerManager.UpdateCar(packet.Id, packet.CarId);
    }

    private void OnClientboundPingUpdatePacket(ClientboundPingUpdatePacket packet)
    {
        PlayerManager.UpdatePing(packet.Id, packet.Ping);
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
        go.AddComponent<StartGameData_ServerSave>().SetFromPacket(packet);
        Object.DontDestroyOnLoad(go);

        SceneSwitcher.SwitchToScene(DVScenes.Game);
        WorldStreamingInit.LoadingFinished += SendReadyPacket;

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
    }

    private void OnClientboundTimeAdvancePacket(ClientboundTimeAdvancePacket packet)
    {
        TimeAdvance.AdvanceTime(packet.amountOfTimeToSkipInSeconds);
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
        LogDebug(() =>
        {
            StringBuilder sb = new("Spawning trainset consisting of ");
            foreach (TrainsetSpawnPart spawnPart in packet.SpawnParts)
                sb.Append($"{spawnPart.CarId} ({spawnPart.LiveryId}) with net ID {spawnPart.NetId}, ");
            return sb.ToString();
        });

        NetworkedCarSpawner.SpawnCars(packet.SpawnParts);

        foreach (TrainsetSpawnPart spawnPart in packet.SpawnParts)
            SendTrainSyncRequest(spawnPart.NetId);
    }

    private void OnClientboundDestroyTrainCarPacket(ClientboundDestroyTrainCarPacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        CarSpawner.Instance.DeleteCar(networkedTrainCar.TrainCar);
    }

    public void OnClientboundTrainPhysicsPacket(ClientboundTrainsetPhysicsPacket packet)
    {
        NetworkTrainsetWatcher.Instance.Client_HandleTrainsetPhysicsUpdate(packet);
    }

    private void OnCommonTrainCouplePacket(CommonTrainCouplePacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar) || !NetworkedTrainCar.GetTrainCar(packet.OtherNetId, out TrainCar otherTrainCar))
            return;

        Coupler coupler = packet.IsFrontCoupler ? trainCar.frontCoupler : trainCar.rearCoupler;
        Coupler otherCoupler = packet.OtherCarIsFrontCoupler ? otherTrainCar.frontCoupler : otherTrainCar.rearCoupler;

        coupler.CoupleTo(otherCoupler, packet.PlayAudio, packet.ViaChainInteraction);
    }

    private void OnCommonTrainUncouplePacket(CommonTrainUncouplePacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        Coupler coupler = packet.IsFrontCoupler ? trainCar.frontCoupler : trainCar.rearCoupler;

        coupler.Uncouple(packet.PlayAudio, false, packet.DueToBrokenCouple, packet.ViaChainInteraction);
    }

    private void OnCommonHoseConnectedPacket(CommonHoseConnectedPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar) || !NetworkedTrainCar.GetTrainCar(packet.OtherNetId, out TrainCar otherTrainCar))
            return;

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;
        Coupler otherCoupler = packet.OtherIsFront ? otherTrainCar.frontCoupler : otherTrainCar.rearCoupler;

        coupler.ConnectAirHose(otherCoupler, packet.PlayAudio);
    }

    private void OnCommonHoseDisconnectedPacket(CommonHoseDisconnectedPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        Coupler coupler = packet.IsFront ? trainCar.frontCoupler : trainCar.rearCoupler;

        coupler.DisconnectAirHose(packet.PlayAudio);
    }

    private void OnCommonMuConnectedPacket(CommonMuConnectedPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar) || !NetworkedTrainCar.GetTrainCar(packet.OtherNetId, out TrainCar otherTrainCar))
            return;

        MultipleUnitCable cable = packet.IsFront ? trainCar.muModule.frontCable : trainCar.muModule.rearCable;
        MultipleUnitCable otherCable = packet.IsFront ? otherTrainCar.muModule.frontCable : otherTrainCar.muModule.rearCable;

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

    private void OnClientboundCargoStatePacket(ClientboundCargoStatePacket packet)
    {
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.CargoModelIndex = packet.CargoModelIndex;
        Car logicCar = networkedTrainCar.TrainCar.logicCar;

        if (packet.CargoType == (ushort)CargoType.None && logicCar.CurrentCargoTypeInCar == CargoType.None)
            return;

        float cargoAmount = Mathf.Clamp(packet.CargoAmount, 0, logicCar.capacity);

        // todo: cache warehouse machine
        WarehouseMachine warehouse = string.IsNullOrEmpty(packet.WarehouseMachineId) ? null : JobSaveManager.Instance.GetWarehouseMachineWithId(packet.WarehouseMachineId);
        if (packet.IsLoading)
            logicCar.LoadCargo(cargoAmount, (CargoType)packet.CargoType, warehouse);
        else
            logicCar.UnloadCargo(cargoAmount, (CargoType)packet.CargoType, warehouse);
    }

    private void OnClientboundCarHealthUpdatePacket(ClientboundCarHealthUpdatePacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;

        CarDamageModel carDamage = trainCar.CarDamage;
        float difference = Mathf.Abs(packet.Health - carDamage.currentHealth);
        if (difference < 0.0001)
            return;

        if (packet.Health < carDamage.currentHealth)
            carDamage.DamageCar(difference);
        else
            carDamage.RepairCar(difference);
    }

    private void OnClientboundRerailTrainPacket(ClientboundRerailTrainPacket packet)
    {
        if (!NetworkedTrainCar.GetTrainCar(packet.NetId, out TrainCar trainCar))
            return;
        if (!NetworkedRailTrack.Get(packet.TrackId, out NetworkedRailTrack networkedRailTrack))
            return;
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
            screen.PopulateLicensesTextsFromIndex(screen.indexOfFirstDisplayedLicense);
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

    #endregion

    #region Senders

    private void SendPacketToServer<T>(T packet, DeliveryMethod deliveryMethod) where T : class, new()
    {
        SendPacket(serverPeer, packet, deliveryMethod);
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

    public void SendPlayerPosition(Vector3 position, Vector3 moveDir, float rotationY, bool isJumping, bool isOnCar, bool reliable)
    {
        SendPacketToServer(new ServerboundPlayerPositionPacket {
            Position = position,
            MoveDir = new Vector2(moveDir.x, moveDir.z),
            RotationY = rotationY,
            IsJumpingIsOnCar = (byte)((isJumping ? 1 : 0) | (isOnCar ? 2 : 0))
        }, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
    }

    public void SendPlayerCar(ushort carId)
    {
        SendPacketToServer(new ServerboundPlayerCarPacket {
            CarId = carId
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendTimeAdvance(float amountOfTimeToSkipInSeconds)
    {
        SendPacketToServer(new ServerboundTimeAdvancePacket {
            amountOfTimeToSkipInSeconds = amountOfTimeToSkipInSeconds
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendJunctionSwitched(ushort netId, byte selectedBranch, Junction.SwitchMode mode)
    {
        SendPacketToServer(new CommonChangeJunctionPacket {
            NetId = netId,
            SelectedBranch = selectedBranch,
            Mode = (byte)mode
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTurntableRotation(byte netId, float rotation)
    {
        SendPacketToServer(new CommonRotateTurntablePacket {
            NetId = netId,
            rotation = rotation
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendTrainCouple(Coupler coupler, Coupler otherCoupler, bool playAudio, bool viaChainInteraction)
    {
        SendPacketToServer(new CommonTrainCouplePacket {
            NetId = coupler.train.GetNetId(),
            IsFrontCoupler = coupler.isFrontCoupler,
            OtherNetId = otherCoupler.train.GetNetId(),
            OtherCarIsFrontCoupler = otherCoupler.isFrontCoupler,
            PlayAudio = playAudio,
            ViaChainInteraction = viaChainInteraction
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainUncouple(Coupler coupler, bool playAudio, bool dueToBrokenCouple, bool viaChainInteraction)
    {
        SendPacketToServer(new CommonTrainUncouplePacket {
            NetId = coupler.train.GetNetId(),
            IsFrontCoupler = coupler.isFrontCoupler,
            PlayAudio = playAudio,
            ViaChainInteraction = viaChainInteraction,
            DueToBrokenCouple = dueToBrokenCouple
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendHoseConnected(Coupler coupler, Coupler otherCoupler, bool playAudio)
    {
        SendPacketToServer(new CommonHoseConnectedPacket {
            NetId = coupler.train.GetNetId(),
            IsFront = coupler.isFrontCoupler,
            OtherNetId = otherCoupler.train.GetNetId(),
            OtherIsFront = otherCoupler.isFrontCoupler,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendHoseDisconnected(Coupler coupler, bool playAudio)
    {
        SendPacketToServer(new CommonHoseDisconnectedPacket {
            NetId = coupler.train.GetNetId(),
            IsFront = coupler.isFrontCoupler,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendMuConnected(MultipleUnitCable cable, MultipleUnitCable otherCable, bool playAudio)
    {
        SendPacketToServer(new CommonMuConnectedPacket {
            NetId = cable.muModule.train.GetNetId(),
            IsFront = cable.isFront,
            OtherNetId = otherCable.muModule.train.GetNetId(),
            OtherIsFront = otherCable.isFront,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendMuDisconnected(ushort netId, MultipleUnitCable cable, bool playAudio)
    {
        SendPacketToServer(new CommonMuDisconnectedPacket {
            NetId = netId,
            IsFront = cable.isFront,
            PlayAudio = playAudio
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendCockState(ushort netId, Coupler coupler, bool isOpen)
    {
        SendPacketToServer(new CommonCockFiddlePacket {
            NetId = netId,
            IsFront = coupler.isFrontCoupler,
            IsOpen = isOpen
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendBrakeCylinderReleased(ushort netId)
    {
        SendPacketToServer(new CommonBrakeCylinderReleasePacket {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendHandbrakePositionChanged(ushort netId, float position)
    {
        SendPacketToServer(new CommonHandbrakePositionPacket {
            NetId = netId,
            Position = position
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendPorts(ushort netId, string[] portIds, float[] portValues)
    {
        SendPacketToServer(new CommonTrainPortsPacket {
            NetId = netId,
            PortIds = portIds,
            PortValues = portValues
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendFuses(ushort netId, string[] fuseIds, bool[] fuseValues)
    {
        SendPacketToServer(new CommonTrainFusesPacket {
            NetId = netId,
            FuseIds = fuseIds,
            FuseValues = fuseValues
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainSyncRequest(ushort netId)
    {
        SendPacketToServer(new ServerboundTrainSyncRequestPacket {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainDeleteRequest(ushort netId)
    {
        SendPacketToServer(new ServerboundTrainDeleteRequestPacket {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendTrainRerailRequest(ushort netId, ushort trackId, Vector3 position, Vector3 forward)
    {
        SendPacketToServer(new ServerboundTrainRerailRequestPacket {
            NetId = netId,
            TrackId = trackId,
            Position = position,
            Forward = forward
        }, DeliveryMethod.ReliableUnordered);
    }

    public void SendLicensePurchaseRequest(string id, bool isJobLicense)
    {
        SendPacketToServer(new ServerboundLicensePurchaseRequestPacket {
            Id = id,
            IsJobLicense = isJobLicense
        }, DeliveryMethod.ReliableUnordered);
    }

    #endregion
}
