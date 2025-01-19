using LiteNetLib;
using System.Net.Sockets;
using System.Net;
using System;
using LiteNetLib.Utils;

namespace Multiplayer.Networking.TransportLayers;
public interface ITransport
{
    NetStatistics Statistics { get; }
    bool IsRunning { get; }


    bool Start();
    bool Start(int port);
    bool Start(IPAddress ipv4, IPAddress ipv6, int port);
    void Stop(bool sendDisconnectPackets);
    void PollEvents();
    void UpdateSettings(Settings settings);

    // Connection management
    ITransportPeer Connect(string address, int port, NetDataWriter data);
    void Send(ITransportPeer peer, NetDataWriter writer, DeliveryMethod deliveryMethod);

    // Events
    event Action<NetDataReader, IConnectionRequest> OnConnectionRequest;
    event Action<ITransportPeer> OnPeerConnected;
    event Action<ITransportPeer, DisconnectInfo> OnPeerDisconnected;
    event Action<ITransportPeer, NetDataReader, byte, DeliveryMethod> OnNetworkReceive;
    event Action<IPEndPoint, SocketError> OnNetworkError;
    event Action<ITransportPeer, int> OnNetworkLatencyUpdate;
}

public interface IConnectionRequest
{
    ITransportPeer Accept();
    void Reject(NetDataWriter data = null);
    IPEndPoint RemoteEndPoint { get; }
}

public interface ITransportPeer
{
    int Id { get; }
    TransportConnectionState ConnectionState { get; }
    void Send(NetDataWriter writer, DeliveryMethod deliveryMethod);
    void Disconnect(NetDataWriter data = null);
}

public enum TransportConnectionState
{
    Connected,
    Connecting,
    Disconnected,
    Disconnecting
}
