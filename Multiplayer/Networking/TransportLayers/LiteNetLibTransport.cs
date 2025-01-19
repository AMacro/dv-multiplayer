using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Net;
using System.Net.Sockets;

namespace Multiplayer.Networking.TransportLayers;

public class LiteNetLibTransport : ITransport, INetEventListener
{
    public NetStatistics Statistics => netManager.Statistics;

    private readonly NetManager netManager;

    public bool IsRunning => netManager?.IsRunning ?? false;

    public event Action<NetDataReader, ConnectionRequest> OnConnectionRequest;
    public event Action<NetPeer> OnPeerConnected;
    public event Action<NetPeer, DisconnectInfo> OnPeerDisconnected;
    public event Action<NetPeer, NetPacketReader, byte, DeliveryMethod> OnNetworkReceive;
    public event Action<IPEndPoint, SocketError> OnNetworkError;

    public LiteNetLibTransport()
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.LiteNetLibTransport()");
        netManager = new NetManager(this)
        {
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            BroadcastReceiveEnabled = true,
        };
    }

    public bool Start()
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.Start()");
        return netManager.Start();
    }

    public bool Start(int port)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.Start({port})");
        return netManager.Start(port);
    }

    public bool Start(IPAddress ipv4, IPAddress ipv6, int port)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.Start({ipv4}, {ipv6}, {port})");
        return netManager.Start(ipv4, ipv6, port);
    }

    public void Stop(bool sendDisconnectPackets)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.Stop()");
        netManager.Stop(sendDisconnectPackets);
    }

    public void PollEvents()
    {
        netManager.PollEvents();
    }

    public NetPeer Connect(string address, int port, NetDataWriter data)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.Connect({address}, {port})");
        return netManager.Connect(address, port, data);
    }

    public void Send(NetPeer peer, NetDataWriter writer, DeliveryMethod deliveryMethod)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.Send({peer.Id}, {deliveryMethod})");
        peer.Send(writer, deliveryMethod);
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnConnectionRequest({request.RemoteEndPoint})");
        OnConnectionRequest?.Invoke(request.Data, request);
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnPeerConnected({peer.Id})");
        OnPeerConnected?.Invoke(peer);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnPeerDisconnected({peer.Id}, {disconnectInfo.Reason})");
        OnPeerDisconnected?.Invoke(peer, disconnectInfo);
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnNetworkReceive({peer.Id}, {channelNumber}, {deliveryMethod})");
        OnNetworkReceive?.Invoke(peer, reader, channelNumber, deliveryMethod);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnNetworkError({endPoint}, {socketError})");
        OnNetworkError?.Invoke(endPoint, socketError);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnNetworkReceiveUnconnected({remoteEndPoint}, {messageType})");
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnNetworkLatencyUpdate({peer.Id}, {latency})");
    }

    public void UpdateSettings(Settings settings)
    {
        //only look at LiteNetLib settings
        netManager.NatPunchEnabled = settings.EnableNatPunch;
        netManager.AutoRecycle = settings.ReuseNetPacketReaders;
        netManager.UseNativeSockets = settings.UseNativeSockets;
        netManager.EnableStatistics = settings.ShowStats;
        netManager.SimulatePacketLoss = settings.SimulatePacketLoss;
        netManager.SimulateLatency = settings.SimulateLatency;
        netManager.SimulationPacketLossChance = settings.SimulationPacketLossChance;
        netManager.SimulationMinLatency = settings.SimulationMinLatency;
        netManager.SimulationMaxLatency = settings.SimulationMaxLatency;
    }

}
