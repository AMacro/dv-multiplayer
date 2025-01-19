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
    NetPeer Connect(string address, int port, NetDataWriter data);
    void Send(NetPeer peer, NetDataWriter writer, DeliveryMethod deliveryMethod);

    // Events
    event Action<NetDataReader, ConnectionRequest> OnConnectionRequest;
    event Action<NetPeer> OnPeerConnected;
    event Action<NetPeer, DisconnectInfo> OnPeerDisconnected;
    event Action<NetPeer, NetPacketReader, byte, DeliveryMethod> OnNetworkReceive;
    event Action<IPEndPoint, SocketError> OnNetworkError;


}


