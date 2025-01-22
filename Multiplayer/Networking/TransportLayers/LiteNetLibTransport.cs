using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Multiplayer.Networking.TransportLayers;

public class LiteNetLibTransport : ITransport, INetEventListener
{
    public NetStatistics Statistics => netManager.Statistics;
    public bool IsRunning => netManager?.IsRunning ?? false;

    public event Action<NetDataReader, IConnectionRequest> OnConnectionRequest;
    public event Action<ITransportPeer> OnPeerConnected;
    public event Action<ITransportPeer, DisconnectReason> OnPeerDisconnected;
    public event Action<ITransportPeer, NetDataReader, byte, DeliveryMethod> OnNetworkReceive;
    public event Action<IPEndPoint, SocketError> OnNetworkError;
    public event Action<ITransportPeer, int> OnNetworkLatencyUpdate;
 
    private readonly Dictionary<NetPeer, LiteNetLibPeer> netPeerToPeer = [];

    private readonly NetManager netManager;

    #region ITransport
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

    public ITransportPeer Connect(string address, int port, NetDataWriter data)
    {
        var netPeer = netManager.Connect(address, port, data);
        var peer = new LiteNetLibPeer(netPeer);

        //Multiplayer.LogDebug(() => $"LiteNetLibTransport.Connect length: {data.Length}. packet: {BitConverter.ToString(data.Data)}");

        netPeerToPeer[netPeer] = peer;
        return peer;
    }

    public void Send(ITransportPeer peer, NetDataWriter writer, DeliveryMethod deliveryMethod)
    {
        var litePeer = (LiteNetLibPeer)peer;
        litePeer.Send(writer, deliveryMethod);
    }
    #endregion

    #region INetEventListener
    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        //Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.OnConnectionRequest({request.RemoteEndPoint})");
        OnConnectionRequest?.Invoke(request.Data, new LiteNetLibConnectionRequest(request, this));
    }

    void INetEventListener.OnPeerConnected(NetPeer netPeer)
    {
        var peer = new LiteNetLibPeer(netPeer);

        netPeerToPeer[netPeer] = peer;

        OnPeerConnected?.Invoke(peer);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer netPeer, DisconnectInfo disconnectInfo)
    {
        if(!netPeerToPeer.TryGetValue(netPeer, out var peer))
            return;

        OnPeerDisconnected?.Invoke(peer, disconnectInfo.Reason);

        netPeerToPeer.Remove(netPeer);
        CleanupPeerDictionaries();
    }


    void INetEventListener.OnNetworkReceive(NetPeer netPeer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        //Multiplayer.LogDebug(() => $"LiteNetLibTransport.OnNetworkReceive({netPeer?.Id})");

        if (netPeerToPeer.TryGetValue(netPeer, out var peer))
        {
            //Multiplayer.LogDebug(() => $"LiteNetLibTransport.OnNetworkReceive({netPeer?.Id}) peer: {peer != null}");
            OnNetworkReceive?.Invoke(peer, reader, channelNumber, deliveryMethod);
        }
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

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer netPeer, int latency)
    {
        if (netPeerToPeer.TryGetValue(netPeer, out var peer))
            OnNetworkLatencyUpdate?.Invoke(peer, latency);
    }

    #endregion

    public void UpdateSettings(Settings settings)
    {
        Multiplayer.LogDebug(() => $"LiteNetLibTransport.INetEventListener.UpdateSettings()");
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

    private void CleanupPeerDictionaries()
    {
        var nullPeers = netPeerToPeer.Where(kvp => kvp.Key == null || kvp.Value == null).ToList();
        foreach (var pair in nullPeers)
        {
            netPeerToPeer.Remove(pair.Key);
        }
    }
    public void RegisterPeer(NetPeer netPeer, LiteNetLibPeer peer)
    {
        netPeerToPeer[netPeer] = peer;
    }

}

public class LiteNetLibConnectionRequest : IConnectionRequest
{
    private readonly ConnectionRequest request;
    private readonly LiteNetLibTransport transport;

    public LiteNetLibConnectionRequest(ConnectionRequest request, LiteNetLibTransport transport)
    {
        this.request = request;
        this.transport = transport;
    }

    public ITransportPeer Accept()
    {
        var netPeer = request.Accept();
        var peer = new LiteNetLibPeer(netPeer);
        transport.RegisterPeer(netPeer, peer);

        return peer;
    }

    public void Reject(NetDataWriter data = null)
    {
        request.Reject(data);
    }

    public IPEndPoint RemoteEndPoint => request.RemoteEndPoint;
}

public class LiteNetLibPeer : ITransportPeer
{
    private readonly NetPeer peer;
    public int Id => peer.Id;

    public LiteNetLibPeer(NetPeer peer)
    {
        this.peer = peer;
    }

    public void Send(NetDataWriter writer, DeliveryMethod deliveryMethod)
    {
        peer.Send(writer, deliveryMethod);
    }

    public void Disconnect(NetDataWriter data = null)
    {
        peer.Disconnect(data);
    }

    public TransportConnectionState ConnectionState => peer.ConnectionState switch
    {
        LiteNetLib.ConnectionState.Connected => TransportConnectionState.Connected,
        LiteNetLib.ConnectionState.Outgoing => TransportConnectionState.Connecting,
        LiteNetLib.ConnectionState.Disconnected => TransportConnectionState.Disconnected,
        LiteNetLib.ConnectionState.ShutdownRequested => TransportConnectionState.Disconnecting,
        _ => TransportConnectionState.Disconnected
    };

}
