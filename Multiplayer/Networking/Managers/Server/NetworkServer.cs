using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.Scenarios.Common;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.WeatherSystem;
using Humanizer;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Clientbound.Jobs;
using Multiplayer.Networking.Packets.Clientbound.SaveGame;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Networking.Packets.Clientbound.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Utils;
using UnityEngine;
using UnityModManagerNet;
using System.Net;
using Multiplayer.Networking.Packets.Serverbound.Train;
using Multiplayer.Networking.Packets.Unconnected;
using System.Text;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.TransportLayers;


namespace Multiplayer.Networking.Managers.Server;

public class NetworkServer : NetworkManager
{
    public Action<uint> PlayerDisconnect;
    protected override string LogPrefix => "[Server]";

    private readonly Queue<ITransportPeer> joinQueue = new();
    private readonly Dictionary<byte, ServerPlayer> serverPlayers = [];
    private readonly Dictionary<byte, ITransportPeer> Peers = [];

    private LobbyServerManager lobbyServerManager;
    public bool isSinglePlayer;
    public LobbyServerData serverData;
    public RerailController rerailController;

    public IReadOnlyCollection<ServerPlayer> ServerPlayers => serverPlayers.Values;
    public int PlayerCount => ServerPlayers.Count;

    private static ITransportPeer SelfPeer => NetworkLifecycle.Instance.Client?.SelfPeer;
    public static byte SelfId => (byte)SelfPeer.Id;
    private readonly ModInfo[] serverMods;

    public readonly IDifficulty Difficulty;
    private bool IsLoaded;

    //we don't care if the client doesn't have these mods
    public static string[] modWhiteList = ["RuntimeUnityEditor", "BookletOrganizer"];

    public NetworkServer(IDifficulty difficulty, Settings settings, bool isSinglePlayer, LobbyServerData serverData) : base(settings)
    {
        LogDebug(()=>$"NetworkServer Constructor");
        this.isSinglePlayer = isSinglePlayer;
        this.serverData = serverData;

        Difficulty = difficulty;

        serverMods = ModInfo.FromModEntries(UnityModManager.modEntries)
                            .Where(mod => !modWhiteList.Contains(mod.Id)).ToArray();


    }

    public override bool Start(int port)
    {
        //setup paint theme lookup cache
        PaintThemeLookup.Instance.CheckInstance();

        WorldStreamingInit.LoadingFinished += OnLoaded;

        Multiplayer.Log($"Starting server...");
        //Try to get our static IPv6 Address we will need this for IPv6 NAT punching to be reliable
        if (IPAddress.TryParse(LobbyServerManager.GetStaticIPv6Address(), out IPAddress ipv6Address))
        {
            //start the connection, IPv4 messages can come from anywhere, IPv6 messages need to specifically come from the static IPv6
            return base.Start(IPAddress.Any, ipv6Address,port);

        }

        //we're not running IPv6, start as normal
        return base.Start(port);
    }

    public override void Stop()
    {
        if (lobbyServerManager != null)
        {
            lobbyServerManager.RemoveFromLobbyServer();
            UnityEngine.Object.Destroy(lobbyServerManager);
        }

        //Alert all clients (except h
        var packet =  WritePacket(new ClientboundDisconnectPacket());
        foreach (var peer in Peers.Values)
        {
            if (peer != SelfPeer)
                peer?.Disconnect(packet);
        }

        base.Stop();
    }

    protected override void Subscribe()
    {
        //Client management
        netPacketProcessor.SubscribeReusable<ServerboundClientLoginPacket, IConnectionRequest>(OnServerboundClientLoginPacket);

        //World sync
        netPacketProcessor.SubscribeReusable<ServerboundClientReadyPacket, ITransportPeer>(OnServerboundClientReadyPacket);
        netPacketProcessor.SubscribeReusable<ServerboundSaveGameDataRequestPacket, ITransportPeer>(OnServerboundSaveGameDataRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTimeAdvancePacket, ITransportPeer>(OnServerboundTimeAdvancePacket);


        netPacketProcessor.SubscribeReusable<ServerboundPlayerPositionPacket, ITransportPeer>(OnServerboundPlayerPositionPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTrainSyncRequestPacket>(OnServerboundTrainSyncRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTrainDeleteRequestPacket, ITransportPeer>(OnServerboundTrainDeleteRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTrainRerailRequestPacket, ITransportPeer>(OnServerboundTrainRerailRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundLicensePurchaseRequestPacket, ITransportPeer>(OnServerboundLicensePurchaseRequestPacket);
        netPacketProcessor.SubscribeReusable<CommonChangeJunctionPacket, ITransportPeer>(OnCommonChangeJunctionPacket);
        netPacketProcessor.SubscribeReusable<CommonRotateTurntablePacket, ITransportPeer>(OnCommonRotateTurntablePacket);
        netPacketProcessor.SubscribeReusable<CommonCouplerInteractionPacket, ITransportPeer>(OnCommonCouplerInteractionPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainCouplePacket, ITransportPeer>(OnCommonTrainCouplePacket);
        netPacketProcessor.SubscribeReusable<CommonTrainUncouplePacket, ITransportPeer>(OnCommonTrainUncouplePacket);
        netPacketProcessor.SubscribeReusable<CommonHoseConnectedPacket, ITransportPeer>(OnCommonHoseConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonHoseDisconnectedPacket, ITransportPeer>(OnCommonHoseDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuConnectedPacket, ITransportPeer>(OnCommonMuConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuDisconnectedPacket, ITransportPeer>(OnCommonMuDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonCockFiddlePacket, ITransportPeer>(OnCommonCockFiddlePacket);
        netPacketProcessor.SubscribeReusable<CommonBrakeCylinderReleasePacket, ITransportPeer>(OnCommonBrakeCylinderReleasePacket);
        netPacketProcessor.SubscribeReusable<CommonHandbrakePositionPacket, ITransportPeer>(OnCommonHandbrakePositionPacket);
        netPacketProcessor.SubscribeReusable<CommonPaintThemePacket, ITransportPeer>(OnCommonPaintThemePacket);
        netPacketProcessor.SubscribeReusable<ServerboundAddCoalPacket, ITransportPeer>(OnServerboundAddCoalPacket);
        netPacketProcessor.SubscribeReusable<ServerboundFireboxIgnitePacket, ITransportPeer>(OnServerboundFireboxIgnitePacket);
        netPacketProcessor.SubscribeReusable<CommonTrainPortsPacket, ITransportPeer>(OnCommonTrainPortsPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainFusesPacket, ITransportPeer>(OnCommonTrainFusesPacket);
        netPacketProcessor.SubscribeReusable<ServerboundJobValidateRequestPacket, ITransportPeer>(OnServerboundJobValidateRequestPacket);
        netPacketProcessor.SubscribeReusable<CommonChatPacket, ITransportPeer>(OnCommonChatPacket);
        netPacketProcessor.SubscribeReusable<UnconnectedPingPacket, IPEndPoint>(OnUnconnectedPingPacket);
        netPacketProcessor.SubscribeNetSerializable<CommonItemChangePacket, ITransportPeer>(OnCommonItemChangePacket);
    }

    private void OnLoaded()
    {
        //Debug.Log($"Server loaded, isSinglePlayer: {isSinglePlayer} isPublic: {isPublic}");
        if (!isSinglePlayer)
        {
            lobbyServerManager = NetworkLifecycle.Instance.GetOrAddComponent<LobbyServerManager>();
        }

        Log($"Server loaded, processing {joinQueue.Count} queued players");
        IsLoaded = true;

        while (joinQueue.Count > 0)
        {
            ITransportPeer peer = joinQueue.Dequeue();

            // Assuming the `peer.ConnectionState` property exists and is being checked
            if (peer.ConnectionState.Equals(TransportConnectionState.Connected))
            {
                System.Console.WriteLine("Connection is established.");
                OnServerboundClientReadyPacket(null, peer);
            }
            else
            {
                System.Console.WriteLine("Connection is not established.");
            }
        }
    }

    public bool TryGetServerPlayer(ITransportPeer peer, out ServerPlayer player)
    {
        return serverPlayers.TryGetValue((byte)peer.Id, out player);
    }
    public bool TryGetServerPlayer(byte id, out ServerPlayer player)
    {
        return serverPlayers.TryGetValue(id, out player);
    }

    public bool TryGetPeer(byte id, out ITransportPeer peer)
    {
        return Peers.TryGetValue(id, out peer);
    }

    #region Net Events

    public override void OnPeerConnected(ITransportPeer peer)
    {
    }

    public override void OnPeerDisconnected(ITransportPeer peer, DisconnectReason disconnectReason)
    {
        byte id = (byte)peer.Id;
        Log($"Player {(serverPlayers.TryGetValue(id, out ServerPlayer player) ? player : id)} disconnected: {disconnectReason}");

        if (WorldStreamingInit.isLoaded)
            SaveGameManager.Instance.UpdateInternalData();

        serverPlayers.Remove(id);
        Peers.Remove(id);

        SendPacketToAll(new ClientboundPlayerDisconnectPacket
        {
            Id = id
        }, DeliveryMethod.ReliableUnordered);

        PlayerDisconnect?.Invoke(id);
    }

    public override void OnNetworkLatencyUpdate(ITransportPeer peer, int latency)
    {
        ClientboundPingUpdatePacket clientboundPingUpdatePacket = new()
        {
            Id = (byte)peer.Id,
            Ping = latency
        };

        SendPacketToAll(clientboundPingUpdatePacket, DeliveryMethod.ReliableUnordered, peer);

        SendPacket(peer, new ClientboundTickSyncPacket
        {
            ServerTick = NetworkLifecycle.Instance.Tick
        }, DeliveryMethod.ReliableUnordered);

        if (latency > 150)
        {
            serverPlayers.TryGetValue((byte)peer.Id, out var player);
            LogWarning($"High Ping Detected! Player: \"{player?.Username}\", ping: {latency}ms");
        }
    }

    public override void OnConnectionRequest(NetDataReader requestData, IConnectionRequest request)
    {
        LogDebug(() => $"NetworkServer OnConnectionRequest");
        netPacketProcessor.ReadAllPackets(requestData, request);
    }

    #endregion

    #region Packet Senders

    private void SendPacketToAll<T>(T packet, DeliveryMethod deliveryMethod) where T : class, new()
    {
        NetDataWriter writer = WritePacket(packet);
        foreach (KeyValuePair<byte, ITransportPeer> kvp in Peers)
            kvp.Value?.Send(writer, deliveryMethod);
    }

    private void SendPacketToAll<T>(T packet, DeliveryMethod deliveryMethod, ITransportPeer excludePeer) where T : class, new()
    {
        NetDataWriter writer = WritePacket(packet);
        foreach (KeyValuePair<byte, ITransportPeer> kvp in Peers)
        {
            if (kvp.Key == excludePeer.Id)
                continue;
            kvp.Value.Send(writer, deliveryMethod);
        }
    }
    private void SendNetSerializablePacketToAll<T>(T packet, DeliveryMethod deliveryMethod) where T : INetSerializable, new()
    {
        NetDataWriter writer = WriteNetSerializablePacket(packet);
        foreach (KeyValuePair<byte, ITransportPeer> kvp in Peers)
            kvp.Value.Send(writer, deliveryMethod);
    }

    private void SendNetSerializablePacketToAll<T>(T packet, DeliveryMethod deliveryMethod, ITransportPeer excludePeer) where T : INetSerializable, new()
    {
        NetDataWriter writer = WriteNetSerializablePacket(packet);
        foreach (KeyValuePair<byte, ITransportPeer> kvp in Peers)
        {
            if (kvp.Key == excludePeer.Id)
                continue;
            kvp.Value.Send(writer, deliveryMethod);
        }
    }

    public void KickPlayer(ITransportPeer peer)
    {
        //peer.Send(WritePacket(new ClientboundDisconnectPacket()),DeliveryMethod.ReliableUnordered);
        peer.Disconnect(WritePacket(new ClientboundDisconnectPacket { Kicked = true }));
    }
    public void SendGameParams(GameParams gameParams)
    {
        SendPacketToAll(ClientboundGameParamsPacket.FromGameParams(gameParams), DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendSpawnTrainset(List<TrainCar> set, bool autoCouple, bool sendToAll, ITransportPeer sendTo = null)
    {

        LogDebug(() =>
        {
            StringBuilder sb = new();

            sb.Append($"SendSpawnTrainSet() Sending trainset {set?.FirstOrDefault()?.GetNetId()} with {set?.Count} cars");

            TrainCar[] noNetId = set?.Where(car => car.GetNetId() == 0).ToArray();

            if (noNetId.Length > 0)
                sb.AppendLine($"Erroneous cars!: {string.Join(", ", noNetId.Select(car => $"{{{car?.ID}, {car?.CarGUID}, {car.logicCar != null}}}"))}");

            return sb.ToString();

        });

        var packet = ClientboundSpawnTrainSetPacket.FromTrainSet(set, autoCouple);

        if (!sendToAll)
        {
            if (sendTo == null)
                LogError($"SendSpawnTrainSet() Trying to send to null peer!");
            else
                SendPacket(sendTo, packet, DeliveryMethod.ReliableOrdered);
        }
        else
            SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendSpawnTrainCar(NetworkedTrainCar networkedTrainCar)
    {
        SendPacketToAll(ClientboundSpawnTrainCarPacket.FromTrainCar(networkedTrainCar), DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendDestroyTrainCar(ushort netId, ITransportPeer peer = null)
    {
        //ushort netID = trainCar.GetNetId();
        LogDebug(() => $"SendDestroyTrainCar({netId})");

        if (netId == 0)
        {
            Multiplayer.LogWarning($"SendDestroyTrainCar failed. netId {netId}");
            return;
        }

        var packet = new ClientboundDestroyTrainCarPacket{ NetId = netId };

        if (peer == null)
            SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, SelfPeer);
        else
            SendPacket(peer, packet, DeliveryMethod.ReliableOrdered);
    }

    public void SendTrainsetPhysicsUpdate(ClientboundTrainsetPhysicsPacket packet, bool reliable)
    {
        SendPacketToAll(packet, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable, SelfPeer);
    }

    public void SendBrakeState(ushort netId, float mainReservoirPressure, float brakePipePressure, float brakeCylinderPressure, float overheatPercent, float overheatReductionFactor, float temperature)
    {
        SendPacketToAll(new ClientboundBrakeStateUpdatePacket
        {
            NetId = netId,
            MainReservoirPressure = mainReservoirPressure,
            BrakePipePressure = brakePipePressure,
            BrakeCylinderPressure = brakeCylinderPressure,
            OverheatPercent = overheatPercent,
            OverheatReductionFactor = overheatReductionFactor,
            Temperature = temperature
        }, DeliveryMethod.ReliableOrdered, SelfPeer);

        //Multiplayer.LogDebug(()=> $"Sending Brake Pressures netId {netId}: {mainReservoirPressure}, {independentPipePressure}, {brakePipePressure}, {brakeCylinderPressure}");
    }

    public void SendFireboxState(ushort netId, float fireboxContents, bool fireboxOn)
    {
        SendPacketToAll(new ClientboundFireboxStatePacket
        {
            NetId = netId,
            Contents = fireboxContents,
            IsOn = fireboxOn
        }, DeliveryMethod.ReliableOrdered, SelfPeer);

        Multiplayer.LogDebug(() => $"Sending Firebox States netId {netId}: {fireboxContents}, {fireboxOn}");
    }

    public void SendCargoState(TrainCar trainCar, ushort netId, bool isLoading, byte cargoModelIndex)
    {
        Car logicCar = trainCar.logicCar;
        CargoType cargoType = isLoading ? logicCar.CurrentCargoTypeInCar : logicCar.LastUnloadedCargoType;
        SendPacketToAll(new ClientboundCargoStatePacket
        {
            NetId = netId,
            IsLoading = isLoading,
            CargoType = (ushort)cargoType,
            CargoAmount = logicCar.LoadedCargoAmount,
            CargoModelIndex = cargoModelIndex,
            WarehouseMachineId = logicCar.CargoOriginWarehouse?.ID
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendCarHealthUpdate(ushort netId, float health)
    {
        SendPacketToAll(new ClientboundCarHealthUpdatePacket
        {
            NetId = netId,
            Health = health
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendRerailTrainCar(ushort netId, ushort rerailTrack, Vector3 worldPos, Vector3 forward)
    {
        SendPacketToAll(new ClientboundRerailTrainPacket
        {
            NetId = netId,
            TrackId = rerailTrack,
            Position = worldPos,
            Forward = forward
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendWindowsBroken(ushort netId, Vector3 forceDirection)
    {
        SendPacketToAll(new ClientboundWindowsBrokenPacket
        {
            NetId = netId,
            ForceDirection = forceDirection
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendWindowsRepaired(ushort netId)
    {
        SendPacketToAll(new ClientboundWindowsRepairedPacket
        {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendMoney(float amount)
    {
        SendPacketToAll(new ClientboundMoneyPacket
        {
            Amount = amount
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendLicense(string id, bool isJobLicense)
    {
        SendPacketToAll(new ClientboundLicenseAcquiredPacket
        {
            Id = id,
            IsJobLicense = isJobLicense
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendGarage(string id)
    {
        SendPacketToAll(new ClientboundGarageUnlockPacket
        {
            Id = id
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendDebtStatus(bool hasDebt)
    {
        SendPacketToAll(new ClientboundDebtStatusPacket
        {
            HasDebt = hasDebt
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendTrainUncouple(Coupler coupler, bool playAudio, bool dueToBrokenCouple, bool viaChainInteraction)
    {
        ushort couplerNetId = coupler.train.GetNetId();

        if (couplerNetId == 0)
        {
            LogWarning($"SendTrainUncouple failed. Coupler: {coupler.name} {couplerNetId}");
            return;
        }

        LogDebug(() => $"SendTrainUncouple({coupler.train.ID}, {coupler.isFrontCoupler}, {dueToBrokenCouple}, {viaChainInteraction})");

        SendPacketToAll(
            new CommonTrainUncouplePacket
            {
                NetId = couplerNetId,
                IsFrontCoupler = coupler.isFrontCoupler,
                PlayAudio = playAudio,
                ViaChainInteraction = viaChainInteraction,
                DueToBrokenCouple = dueToBrokenCouple,
            },
            DeliveryMethod.ReliableOrdered
        );
    }

    public void SendJobsCreatePacket(NetworkedStationController networkedStation, NetworkedJob[] jobs, ITransportPeer peer = null)
    {
        Multiplayer.Log($"Sending JobsCreatePacket for stationNetId {networkedStation.NetId} with {jobs.Count()} jobs");

        var packet = ClientboundJobsCreatePacket.FromNetworkedJobs(networkedStation, jobs);

        if (peer ==null)
            SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, SelfPeer);
        else
            SendPacket(peer, packet, DeliveryMethod.ReliableOrdered);
    }

    public void SendJobsUpdatePacket(ushort stationNetId, NetworkedJob[] jobs)
    {
        Multiplayer.Log($"Sending JobsUpdatePacket for stationNetId {stationNetId} with {jobs.Count()} jobs");
        SendPacketToAll(ClientboundJobsUpdatePacket.FromNetworkedJobs(stationNetId, jobs), DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendItemsChangePacket(List<ItemUpdateData> items, ServerPlayer player)
    {
        Multiplayer.Log($"Sending SendItemsChangePacket with {items.Count()} items to {player.Username}");

        if (Peers.TryGetValue(player.Id, out ITransportPeer peer) && peer != SelfPeer)
        {
            SendNetSerializablePacket(peer, new CommonItemChangePacket { Items = items },
                DeliveryMethod.ReliableOrdered);
        }
    }

    public void SendChat(string message, ITransportPeer exclude = null)
    {

        if (exclude != null)
        {
            NetworkLifecycle.Instance.Server.SendPacketToAll(new CommonChatPacket
            {
                message = message
            }, DeliveryMethod.ReliableUnordered, exclude);
        }
        else
        {
            NetworkLifecycle.Instance.Server.SendPacketToAll(new CommonChatPacket
            {
                message = message
            }, DeliveryMethod.ReliableUnordered);
        }
    }

    public void SendWhisper(string message, ITransportPeer recipient)
    {
        if (message != null || recipient != null)
        {
            NetworkLifecycle.Instance.Server.SendPacket(recipient, new CommonChatPacket
            {
                message = message
            }, DeliveryMethod.ReliableUnordered);
        }

    }

    #endregion

    #region Listeners

    private void OnServerboundClientLoginPacket(ServerboundClientLoginPacket packet, IConnectionRequest request)
    {
        // clean up username - remove leading/trailing white space, swap spaces for underscores and truncate
        packet.Username = packet.Username.Trim().Replace(' ', '_').Truncate(Settings.MAX_USERNAME_LENGTH);
        string overrideUsername = packet.Username;

        //ensure the username is unique
        int uniqueName = ServerPlayers.Where(player => player.OriginalUsername.ToLower() == packet.Username.ToLower()).Count();

        if (uniqueName > 0)
        {
            overrideUsername += uniqueName;
        }

        Guid guid;
        try
        {
            guid = new Guid(packet.Guid);
        }
        catch (ArgumentException)
        {
            // This can only happen if the sent GUID is tampered with, in which case, we aren't worried about showing a message.
            Log($"Invalid GUID from {packet.Username}{(Multiplayer.Settings.LogIps ? $" at {request.RemoteEndPoint.Address}" : "")}");
            request.Reject();
            return;
        }

        Log($"Processing login packet for {packet.Username} ({guid}){(Multiplayer.Settings.LogIps ? $" at {request.RemoteEndPoint.Address}" : "")}");

        if (Multiplayer.Settings.Password != packet.Password)
        {
            LogWarning("Denied login due to invalid password!");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__INVALID_PASSWORD_KEY
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        if (packet.BuildMajorVersion != BuildInfo.BUILD_VERSION_MAJOR)
        {
            LogWarning($"Denied login to incorrect game version! Got: {packet.BuildMajorVersion}, expected: {BuildInfo.BUILD_VERSION_MAJOR}");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__GAME_VERSION_KEY,
                ReasonArgs = [BuildInfo.BUILD_VERSION_MAJOR.ToString(), packet.BuildMajorVersion.ToString()]
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        if (PlayerCount >= Multiplayer.Settings.MaxPlayers || isSinglePlayer && PlayerCount >= 1)
        {
            LogWarning("Denied login due to server being full!");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__FULL_SERVER_KEY
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        ModInfo[] clientMods = packet.Mods.Where(mod => !modWhiteList.Contains(mod.Id)).ToArray();
        if (!serverMods.SequenceEqual(clientMods))
        {
            ModInfo[] missing = serverMods.Except(clientMods).ToArray();
            ModInfo[] extra = clientMods.Except(serverMods).ToArray();

            LogWarning($"Denied login due to mod mismatch! {missing.Length} missing, {extra.Length} extra");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__MODS_KEY,
                Missing = missing,
                Extra = extra
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        ITransportPeer peer = request.Accept();

        ServerPlayer serverPlayer = new()
        {
            Id = (byte)peer.Id,
            Username = overrideUsername,
            OriginalUsername = packet.Username,
            Guid = guid
        };

        serverPlayers.Add(serverPlayer.Id, serverPlayer);

        ClientboundLoginResponsePacket acceptPacket = new()
        {
            Accepted = true,
        };

        SendPacket(peer, acceptPacket, DeliveryMethod.ReliableUnordered);
    }

    private void OnServerboundSaveGameDataRequestPacket(ServerboundSaveGameDataRequestPacket packet, ITransportPeer peer)
    {
        if (Peers.ContainsKey((byte)peer.Id))
        {
            LogWarning("Denied save game data request from already connected peer!");
            return;
        }

        TryGetServerPlayer(peer, out ServerPlayer player);

        SendPacket(peer, ClientboundGameParamsPacket.FromGameParams(Globals.G.GameParams), DeliveryMethod.ReliableOrdered);
        SendPacket(peer, ClientboundSaveGameDataPacket.CreatePacket(player), DeliveryMethod.ReliableOrdered);
    }

    private void OnServerboundClientReadyPacket(ServerboundClientReadyPacket packet, ITransportPeer peer)
    {

        byte peerId = (byte)peer.Id;

        // Allow clients to connect before the server is fully loaded
        if (!IsLoaded)
        {
            joinQueue.Enqueue(peer);
            SendPacket(peer, new ClientboundServerLoadingPacket(), DeliveryMethod.ReliableOrdered);
            return;
        }

        // Unpause physics
        if (AppUtil.Instance.IsTimePaused)
            AppUtil.Instance.RequestSystemOnValueChanged(0.0f);

        // Allow the player to receive packets
        Peers.Add(peerId, peer);

        // Send the new player to all other players
        ServerPlayer serverPlayer = serverPlayers[peerId];
        ClientboundPlayerJoinedPacket clientboundPlayerJoinedPacket = new()
        {
            Id = peerId,
            Username = serverPlayer.Username,
            //Guid = serverPlayer.Guid.ToByteArray()
        };
        SendPacketToAll(clientboundPlayerJoinedPacket, DeliveryMethod.ReliableOrdered, peer);

        ChatManager.ServerMessage(serverPlayer.Username + " joined the game", null, peer);

        Log($"Client {peer.Id} is ready. Sending world state");

        // No need to sync the world state if the player is the host
        if (NetworkLifecycle.Instance.IsHost(peer))
        {
            SendPacket(peer, new ClientboundRemoveLoadingScreenPacket(), DeliveryMethod.ReliableOrdered);
            return;
        }

        SendPacket(peer, new ClientboundBeginWorldSyncPacket(), DeliveryMethod.ReliableOrdered);

        // Send weather state
        SendPacket(peer, WeatherDriver.Instance.GetSaveData().ToObject<ClientboundWeatherPacket>(), DeliveryMethod.ReliableOrdered);

        // Send junctions and turntables
        SendPacket(peer, new ClientboundRailwayStatePacket
        {
            SelectedJunctionBranches = NetworkedJunction.IndexedJunctions.Select(j => j.Junction.selectedBranch).ToArray(),
            TurntableRotations = NetworkedTurntable.IndexedTurntables.Select(j => j.TurntableRailTrack.currentYRotation).ToArray()
        }, DeliveryMethod.ReliableOrdered);

        // Send trains
        foreach (Trainset set in Trainset.allSets)
        {
            try
            {
                SendSpawnTrainset(set.cars, false, false, peer);
            }
            catch (Exception e)
            {
                LogWarning($"Exception when trying to send train set spawn data for [{set?.firstCar?.ID}, {set?.firstCar?.GetNetId()}]\r\n{e.Message}\r\n{e.StackTrace}");
            }
        }

        // Sync Stations (match NetIDs with StationIDs) - we could do this the same as junctions but juntions may need to be upgraded to work this way - future planning for mod integration
        SendPacket(peer, new ClientBoundStationControllerLookupPacket(NetworkedStationController.GetAll().ToArray()), DeliveryMethod.ReliableOrdered);

        //send jobs
        foreach (StationController station in StationController.allStations)
        {
            if (NetworkedStationController.GetFromStationController(station, out NetworkedStationController netStation))
            {
                //only send active jobs (available or in progress) - new clients don't need to know about old jobs
                NetworkedJob[] jobs = netStation.NetworkedJobs
                    .Where(j => j.Job.State == JobState.Available || j.Job.State == JobState.InProgress)
                    .ToArray();

                for (int i = 0; i < jobs.Length; i++)
                {
                    SendJobsCreatePacket(netStation, [jobs[i]]);
                }
            }
            else
            {
                LogError($"Sending job packets... Failed to get NetworkedStation from station");
            }
        }

        // Send existing players
        foreach (ServerPlayer player in ServerPlayers)
        {
            if (player.Id == peer.Id)
                continue;
            SendPacket(peer, new ClientboundPlayerJoinedPacket
            {
                Id = player.Id,
                Username = player.Username,
                //Guid = player.Guid.ToByteArray(),
                CarID = player.CarId,
                Position = player.RawPosition,
                Rotation = player.RawRotationY
            }, DeliveryMethod.ReliableOrdered);
        }

        // All data has been sent, allow the client to load into the world.
        Log($"Sending Remove Loading Screen to {serverPlayer.Username}");
        SendPacket(peer, new ClientboundRemoveLoadingScreenPacket(), DeliveryMethod.ReliableOrdered);

        serverPlayer.IsLoaded = true;
    }

    private void OnServerboundPlayerPositionPacket(ServerboundPlayerPositionPacket packet, ITransportPeer peer)
    {
        if (TryGetServerPlayer(peer, out ServerPlayer player))
        {
            player.CarId = packet.CarID;
            player.RawPosition = packet.Position;
            player.RawRotationY = packet.RotationY;

        }

        ClientboundPlayerPositionPacket clientboundPacket = new()
        {
            Id = (byte)peer.Id,
            Position = packet.Position,
            MoveDir = packet.MoveDir,
            RotationY = packet.RotationY,
            IsJumpingIsOnCar = packet.IsJumpingIsOnCar,
            CarID = packet.CarID
        };

        SendPacketToAll(clientboundPacket, DeliveryMethod.Sequenced, peer);
    }

    private void OnServerboundTimeAdvancePacket(ServerboundTimeAdvancePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(new ClientboundTimeAdvancePacket
        {
            amountOfTimeToSkipInSeconds = packet.amountOfTimeToSkipInSeconds
        }, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonChangeJunctionPacket(CommonChangeJunctionPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonRotateTurntablePacket(CommonRotateTurntablePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonCouplerInteractionPacket(CommonCouplerInteractionPacket packet, ITransportPeer peer)
    {
        //todo: add validation that to ensure the client is near the coupler - this packet may also be used for remote operations and may need to factor that in in the future
        if(NetworkedTrainCar.Get(packet.NetId, out var netTrainCar))
        {
            if(netTrainCar.Server_ValidateCouplerInteraction(packet, peer))
            {
                //passed validation, send to all but the originator
                SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
            }
            else
            {
                Multiplayer.LogDebug(() => $"OnCommonCouplerInteractionPacket([{packet.Flags}, {netTrainCar.CurrentID}, {packet.NetId}], {peer.Id}) Sending validation failure");
                //failed validation notify client
                SendPacket(
                            peer,
                            new CommonCouplerInteractionPacket
                                {
                                    NetId = packet.NetId,
                                    Flags = (ushort)CouplerInteractionType.NoAction,
                                    IsFrontCoupler = packet.IsFrontCoupler,
                                }
                            ,DeliveryMethod.ReliableOrdered
                          );
            }
        }
        else
        {
            Multiplayer.LogDebug(() => $"OnCommonCouplerInteractionPacket([{packet.Flags}, {netTrainCar.CurrentID}, {packet.NetId}], {peer.Id}) Sending destroy");
            //Car doesn't exist, tell client to delete it
            SendDestroyTrainCar(packet.NetId, peer);
        }
        
    }
    private void OnCommonTrainCouplePacket(CommonTrainCouplePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonTrainUncouplePacket(CommonTrainUncouplePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonHoseConnectedPacket(CommonHoseConnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonHoseDisconnectedPacket(CommonHoseDisconnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonMuConnectedPacket(CommonMuConnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonMuDisconnectedPacket(CommonMuDisconnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonCockFiddlePacket(CommonCockFiddlePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonBrakeCylinderReleasePacket(CommonBrakeCylinderReleasePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonHandbrakePositionPacket(CommonHandbrakePositionPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonPaintThemePacket(CommonPaintThemePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnServerboundAddCoalPacket(ServerboundAddCoalPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;

        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        //is value valid?
        if (float.IsNaN(packet.CoalMassDelta))
            return;

        if (!NetworkLifecycle.Instance.IsHost(peer))
        {
            float carLength = CarSpawner.Instance.carLiveryToCarLength[networkedTrainCar.TrainCar.carLivery];

            //is player close enough to add coal?
            if ((player.WorldPosition - networkedTrainCar.transform.position).sqrMagnitude <= carLength * carLength)
                networkedTrainCar.firebox?.fireboxCoalControlPort.ExternalValueUpdate(packet.CoalMassDelta);
        }

    }

    private void OnServerboundFireboxIgnitePacket(ServerboundFireboxIgnitePacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;

        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        if (!NetworkLifecycle.Instance.IsHost(peer))
        {
            //is player close enough to ignite firebox?
            float carLength = CarSpawner.Instance.carLiveryToCarLength[networkedTrainCar.TrainCar.carLivery];
            if ((player.WorldPosition - networkedTrainCar.transform.position).sqrMagnitude <= carLength * carLength)
                networkedTrainCar.firebox?.Ignite();
        }
    }

    private void OnCommonTrainPortsPacket(CommonTrainPortsPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        //if not the host && validation fails then ignore packet
        if (!NetworkLifecycle.Instance.IsHost(peer))
        {
            bool flag = networkedTrainCar.Server_ValidateClientSimFlowPacket(player, packet);

            //LogDebug(() => $"OnCommonTrainPortsPacket from {player.Username}, Not host, valid: {flag}");
            if (!flag)
            {
                return;
            }
        }

        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonTrainFusesPacket(CommonTrainFusesPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnServerboundTrainSyncRequestPacket(ServerboundTrainSyncRequestPacket packet)
    {
        if (NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            networkedTrainCar.Server_DirtyAllState();
    }

    private void OnServerboundTrainDeleteRequestPacket(ServerboundTrainDeleteRequestPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        if (networkedTrainCar.HasPlayers)
        {
            LogWarning($"{player.Username} tried to delete a train with players in it!");
            return;
        }

        TrainCar trainCar = networkedTrainCar.TrainCar;
        float cost = trainCar.playerSpawnedCar ? 0.0f : Mathf.RoundToInt(Globals.G.GameParams.DeleteCarMaxPrice);
        if (!Inventory.Instance.RemoveMoney(cost))
        {
            LogWarning($"{player.Username} tried to delete a train without enough money to do so!");
            return;
        }

        Job job = JobsManager.Instance.GetJobOfCar(trainCar);
        switch (job?.State)
        {
            case JobState.Available:
                job.ExpireJob();
                break;
            case JobState.InProgress:
                JobsManager.Instance.AbandonJob(job);
                break;
        }

        CarSpawner.Instance.DeleteCar(trainCar);
    }

    private void OnServerboundTrainRerailRequestPacket(ServerboundTrainRerailRequestPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;
        if (!NetworkedTrainCar.Get(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;
        if (!NetworkedRailTrack.Get(packet.TrackId, out NetworkedRailTrack networkedRailTrack))
            return;

        TrainCar trainCar = networkedTrainCar.TrainCar;
        Vector3 position = packet.Position + WorldMover.currentMove;

        //Check if player is a Newbie (currently shared with all players)
        float cost = TutorialHelper.InRestrictedMode || rerailController != null && rerailController.isPlayerNewbie ? 0f :
            RerailController.CalculatePrice((networkedTrainCar.transform.position - position).magnitude, trainCar.carType, Globals.G.GameParams.RerailMaxPrice);

        if (!Inventory.Instance.RemoveMoney(cost))
        {
            LogWarning($"{player.Username} tried to rerail a train without enough money to do so!");
            return;
        }

        trainCar.Rerail(networkedRailTrack.RailTrack, position, packet.Forward);
    }

    private void OnServerboundLicensePurchaseRequestPacket(ServerboundLicensePurchaseRequestPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;

        JobLicenseType_v2 jobLicense = null;
        GeneralLicenseType_v2 generalLicense = null;
        float? price = packet.IsJobLicense
            ? (jobLicense = Globals.G.Types.jobLicenses.Find(l => l.id == packet.Id))?.price
            : (generalLicense = Globals.G.Types.generalLicenses.Find(l => l.id == packet.Id))?.price;

        if (!price.HasValue)
        {
            LogWarning($"{player.Username} tried to purchase an invalid {(packet.IsJobLicense ? "job" : "general")} license with id {packet.Id}!");
            return;
        }

        CareerManagerDebtController.Instance.RefreshExistingDebtsState();
        if (CareerManagerDebtController.Instance.NumberOfNonZeroPricedDebts > 0)
        {
            LogWarning($"{player.Username} tried to purchase a {(packet.IsJobLicense ? "job" : "general")} license with id {packet.Id} while having existing debts!");
            return;
        }

        if (!Inventory.Instance.RemoveMoney(price.Value))
        {
            LogWarning($"{player.Username} tried to purchase a {(packet.IsJobLicense ? "job" : "general")} license with id {packet.Id} without enough money to do so!");
            return;
        }

        if (packet.IsJobLicense)
            LicenseManager.Instance.AcquireJobLicense(jobLicense);
        else
            LicenseManager.Instance.AcquireGeneralLicense(generalLicense);
    }


    private void OnServerboundJobValidateRequestPacket(ServerboundJobValidateRequestPacket packet, ITransportPeer peer)
    {
        Log($"OnServerboundJobValidateRequestPacket(): {packet.JobNetId}");

        if (!NetworkedJob.Get(packet.JobNetId, out NetworkedJob networkedJob))
        {
            LogWarning($"OnServerboundJobValidateRequestPacket() NetworkedJob not found: {packet.JobNetId}");

            SendPacket(peer, new ClientboundJobValidateResponsePacket { JobNetId = packet.JobNetId, Invalid = true }, DeliveryMethod.ReliableOrdered);
            return;
        }

        if (!TryGetServerPlayer(peer, out ServerPlayer player))
        {
            LogWarning($"OnServerboundJobValidateRequestPacket() ServerPlayer not found: {peer.Id}");
            return;
        }

        //Find the station and validator
        if (!NetworkedStationController.Get(packet.StationNetId, out NetworkedStationController networkedStationController) || networkedStationController.JobValidator == null)
        {
            LogWarning($"OnServerboundJobValidateRequestPacket() JobValidator not found. StationNetId: {packet.StationNetId}, StationController found: {networkedStationController != null}, JobValidator found: {networkedStationController?.JobValidator != null}");
            return;
        }

        LogDebug(() => $"OnServerboundJobValidateRequestPacket() Validating {packet.JobNetId}, Validation Type: {packet.validationType} overview: {networkedJob.JobOverview != null}, booklet: {networkedJob.JobBooklet != null}");
        switch (packet.validationType)
        {
            case ValidationType.JobOverview:
                networkedStationController.JobValidator.ProcessJobOverview(networkedJob.JobOverview.GetTrackedItem<JobOverview>());
                break;

            case ValidationType.JobBooklet:
                networkedStationController.JobValidator.ValidateJob(networkedJob.JobBooklet.GetTrackedItem<JobBooklet>());
                break;
        }

        //SendPacket(peer, new ClientboundJobValidateResponsePacket { JobNetId = packet.JobNetId, Invalid = false }, DeliveryMethod.ReliableUnordered);
    }

    private void OnCommonChatPacket(CommonChatPacket packet, ITransportPeer peer)
    {
        ChatManager.ProcessMessage(packet.message, peer);
    }
    #endregion

    #region Unconnected Packet Handling
    private void OnUnconnectedPingPacket(UnconnectedPingPacket packet, IPEndPoint endPoint)
    {
        //Multiplayer.Log($"OnUnconnectedPingPacket({endPoint.Address})");
        //SendUnconnectedPacket(packet, endPoint.Address.ToString(), endPoint.Port);
    }

    private void OnCommonItemChangePacket(CommonItemChangePacket packet, ITransportPeer peer)
    {
        //if(!TryGetServerPlayer(peer, out var player))
        //    return;

        //LogDebug(()=>$"OnCommonItemChangePacket({packet?.Items?.Count}, {peer.Id} (\"{player.Username}\"))");

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

        //        debug += "States:";

        //        if (item.States != null)
        //            foreach (var state in item?.States)
        //                debug += "\r\n\t" + state.Key + ": " + state.Value;
        //    }

        //    return debug;
        //}

        //);

        //NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, player);
    }
    #endregion
}
