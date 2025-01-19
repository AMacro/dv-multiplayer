using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.Serialization;
using Multiplayer.Networking.TransportLayers;

namespace Multiplayer.Networking.Managers;

public abstract class NetworkManager
{
    protected readonly NetPacketProcessor netPacketProcessor;
    protected readonly NetDataWriter cachedWriter = new();

    private readonly ITransport transport;
    protected readonly NetManager netManager;

    protected abstract string LogPrefix { get; }

    public NetStatistics Statistics => transport.Statistics;
    public bool IsRunning => transport.IsRunning;
    public bool IsProcessingPacket { get; private set; }

    protected NetworkManager(Settings settings)
    {
        Multiplayer.LogDebug(() => $"NetworkManager Constructor");

        netPacketProcessor = new NetPacketProcessor();
        //transport = new LiteNetLibTransport();
        transport = new SteamWorksTransport();

        transport.OnConnectionRequest += OnConnectionRequest;
        transport.OnPeerConnected += OnPeerConnected;
        transport.OnPeerDisconnected += OnPeerDisconnected;
        transport.OnNetworkReceive += OnNetworkReceive;
        transport.OnNetworkError += OnNetworkError;
        transport.OnNetworkLatencyUpdate += OnNetworkLatencyUpdate;

        
        RegisterNestedTypes();

        OnSettingsUpdated(settings);
        Settings.OnSettingsUpdated += OnSettingsUpdated;

        Subscribe();

    }

    private void RegisterNestedTypes()
    {
        netPacketProcessor.RegisterNestedType(BogieData.Serialize, BogieData.Deserialize);
        netPacketProcessor.RegisterNestedType<JobUpdateStruct>();
        netPacketProcessor.RegisterNestedType(JobData.Serialize, JobData.Deserialize);
        netPacketProcessor.RegisterNestedType(ModInfo.Serialize, ModInfo.Deserialize);
        netPacketProcessor.RegisterNestedType(RigidbodySnapshot.Serialize, RigidbodySnapshot.Deserialize);
        netPacketProcessor.RegisterNestedType(StationsChainNetworkData.Serialize, StationsChainNetworkData.Deserialize);
        netPacketProcessor.RegisterNestedType(TrainsetMovementPart.Serialize, TrainsetMovementPart.Deserialize);
        netPacketProcessor.RegisterNestedType(TrainsetSpawnPart.Serialize, TrainsetSpawnPart.Deserialize);
        netPacketProcessor.RegisterNestedType(Vector2Serializer.Serialize, Vector2Serializer.Deserialize);
        netPacketProcessor.RegisterNestedType(Vector3Serializer.Serialize, Vector3Serializer.Deserialize);
    }

    private void OnSettingsUpdated(Settings settings)
    {
        transport?.UpdateSettings(settings);
    }

    public void PollEvents()
    {
        //netManager.PollEvents();
        transport?.PollEvents();
    }

    public virtual bool Start()
    {
        return transport.Start();
    }
    public virtual bool Start(IPAddress ipv4, IPAddress ipv6, int port)
    {
        return transport.Start(ipv4, ipv6, port);
    }
    public virtual bool Start(int port)
    {
        return transport.Start(port);
    }

    protected virtual ITransportPeer Connect(string address, int port, NetDataWriter netDataWriter)
    {
        return transport.Connect(address, port, netDataWriter);
    }


    public virtual void Stop()
    {
        transport.Stop(true);
        Settings.OnSettingsUpdated -= OnSettingsUpdated;
    }

    protected NetDataWriter WritePacket<T>(T packet) where T : class, new()
    {
        cachedWriter.Reset();
        netPacketProcessor.Write(cachedWriter, packet);
        return cachedWriter;
    }

    protected NetDataWriter WriteNetSerializablePacket<T>(T packet) where T : INetSerializable, new()
    {
        cachedWriter.Reset();
        netPacketProcessor.WriteNetSerializable(cachedWriter, ref packet);
        return cachedWriter;
    }

    protected void SendPacket<T>(ITransportPeer peer, T packet, DeliveryMethod deliveryMethod) where T : class, new()
    {
        peer?.Send(WritePacket(packet), deliveryMethod);
    }

    protected void SendNetSerializablePacket<T>(ITransportPeer peer, T packet, DeliveryMethod deliveryMethod) where T : INetSerializable, new()
    {
        peer?.Send(WriteNetSerializablePacket(packet), deliveryMethod);
    }

    //protected void SendUnconnectedPacket<T>(T packet, string ipAddress, int port) where T : class, new()
    //{
    //    transport.SendUnconnectedMessage(WritePacket(packet), ipAddress, port);
    //}

    protected abstract void Subscribe();

    #region Net Events
    public void OnNetworkReceive(ITransportPeer connection, NetDataReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        //LogDebug(() => $"NetworkManager.OnNetworkReceive()");
        try
        {
            IsProcessingPacket = true;
            netPacketProcessor.ReadAllPackets(reader, null);
        }
        catch (ParseException e)
        {
            Multiplayer.LogWarning($"Failed to parse packet: {e.Message}");
        }
        finally
        {
            IsProcessingPacket = false;
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Multiplayer.LogError($"Network error from {endPoint}: {socketError}");
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        //Multiplayer.Log($"OnNetworkReceiveUnconnected({remoteEndPoint}, {messageType})");
        try
        {
            IsProcessingPacket = true;
            netPacketProcessor.ReadAllPackets(reader, remoteEndPoint);
        }
        catch (ParseException e)
        {
            Multiplayer.LogWarning($"Failed to parse packet: {e.Message}");
        }
        finally
        {
            IsProcessingPacket = false;
        }
    }

    //Standard networking callbacks
    public abstract void OnPeerConnected(ITransportPeer peer);
    public abstract void OnPeerDisconnected(ITransportPeer peer, DisconnectInfo disconnectInfo);
    public abstract void OnConnectionRequest(NetDataReader requestData, IConnectionRequest request);
    public abstract void OnNetworkLatencyUpdate(ITransportPeer peer, int latency);

    #endregion

    #region Logging

    public void LogDebug(Func<object> resolver)
    {
        if (!Multiplayer.Settings.DebugLogging)
            return;
        Multiplayer.LogDebug(() => $"{LogPrefix} {resolver.Invoke()}");
    }

    public void Log(object msg)
    {
        Multiplayer.Log($"{LogPrefix} {msg}");
    }

    public void LogWarning(object msg)
    {
        Multiplayer.LogWarning($"{LogPrefix} {msg}");
    }

    public void LogError(object msg)
    {
        Multiplayer.LogError($"{LogPrefix} {msg}");
    }

    #endregion
}
