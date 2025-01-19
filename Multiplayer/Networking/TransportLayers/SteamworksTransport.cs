using LiteNetLib.Utils;
using LiteNetLib;
using System;
using System.Net.Sockets;
using System.Net;
using Steamworks;
using System.Collections.Generic;
using Steamworks.Data;
using System.Runtime.InteropServices;


namespace Multiplayer.Networking.TransportLayers;

public class SteamWorksTransport : ITransport
{
    public NetStatistics Statistics => new NetStatistics();
    public bool IsRunning => server != null;

    public event Action<NetDataReader, IConnectionRequest> OnConnectionRequest;
    public event Action<ITransportPeer> OnPeerConnected;
    public event Action<ITransportPeer, DisconnectInfo> OnPeerDisconnected;
    public event Action<ITransportPeer, NetDataReader, byte, DeliveryMethod> OnNetworkReceive;
    public event Action<IPEndPoint, SocketError> OnNetworkError;
    public event Action<ITransportPeer, int> OnNetworkLatencyUpdate;

    private SteamSocketManager server;
    private SteamConnectionManager client;


    private readonly Dictionary<int, SteamPeer> peerIdToPeer = [];
    private readonly Dictionary<Connection, SteamPeer> connectionToPeer = [];

    private int nextPeerId = 1;

    #region ITransport
    public SteamWorksTransport()
    {
        //static fields for SteamNetworking
    }

    public bool Start()
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Start()");
        return true;//return Start(0);
    }

    public bool Start(int port)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Start({port})");
        server = SteamNetworkingSockets.CreateNormalSocket<SteamSocketManager>(NetAddress.AnyIp((ushort)port));
        server.transport = this;

        return server != null;
    }

    public bool Start(IPAddress ipv4, IPAddress ipv6, int port)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Start({ipv4}, {ipv6}, {port})");
        return Start(port);
    }

    public void Stop(bool sendDisconnectPackets)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Stop()");
        if (server != null)
        {
            foreach (var connection in server.Connected)
            {
                connection.Close();
            }
            server = null;
        }
    }

    public void PollEvents()
    {
        SteamClient.RunCallbacks();
        client?.Receive();
        server?.Receive();
    }

    public ITransportPeer Connect(string address, int port, NetDataWriter data)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Connect({address}, {port}, {data.Length})");

        var add = NetAddress.From(address, (ushort)port);

   
        Multiplayer.LogDebug(() => $"SteamSocketManager.Connect packet: {BitConverter.ToString(data.Data)}");

        // Create connection manager for client
        client = SteamNetworkingSockets.ConnectNormal<SteamConnectionManager>(add); 
        client.transport = this;
        client.loginPacket = data;
        client.peer = CreatePeer(client.Connection);

        return client.peer;
    }


    public void Send(ITransportPeer peer, NetDataWriter writer, DeliveryMethod deliveryMethod)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Send({peer.Id}, {deliveryMethod})");
        peer.Send(writer, deliveryMethod);
    }

    public void UpdateSettings(Settings settings)
    {
        //todo: implement any settings
    }

    #endregion

    #region SteamManagers
    public class SteamSocketManager : SocketManager
    {
        public SteamWorksTransport transport;

        public override void OnConnecting(Connection connection, ConnectionInfo info)
        {

            Multiplayer.LogDebug(() => $"SteamSocketManager.OnConnecting({connection}, {info})");
            connection.Accept();
        }

        public override void OnConnected(Connection connection, ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamSocketManager.OnConnected({connection}, {info})");
            base.OnConnected(connection, info);

            var peer = transport.CreatePeer(connection);
            peer.connectionRequest = new SteamConnectionRequest(connection, info, peer);
        }

        public override void OnDisconnected(Steamworks.Data.Connection connection, Steamworks.Data.ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamSocketManager.OnDisconnected({connection}, {info})");
            base.OnDisconnected(connection, info);
            throw new NotImplementedException();
        }

        public override void OnMessage(Steamworks.Data.Connection connection, Steamworks.Data.NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            Multiplayer.LogDebug(() => $"SteamSocketManager.OnMessage({connection}, {identity}, , {size}, {messageNum}, {recvTime}, {channel})");

            var peer = transport.GetPeer(connection);

            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);


            Multiplayer.LogDebug(() => $"SteamSocketManager.Received packet: {BitConverter.ToString(buffer)}");

            var reader = new NetDataReader(buffer,0,size);
            if(peer.connectionRequest != null)
            {
                transport?.OnConnectionRequest?.Invoke(reader, peer.connectionRequest);
                peer.connectionRequest = null;
                return;
            }

            transport?.OnNetworkReceive?.Invoke(peer, reader, (byte)channel, DeliveryMethod.ReliableOrdered);
        }

        public override void OnConnectionChanged(Steamworks.Data.Connection connection, Steamworks.Data.ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamSocketManager.OnConnectionChanged({connection}, {info})");
            base.OnConnectionChanged(connection, info);
        }
    }

    public class SteamConnectionManager : ConnectionManager
    {
        public SteamWorksTransport transport;
        public NetDataWriter loginPacket;
        public SteamPeer peer;

        public override void OnConnected(ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamConnectionManager.OnConnected({info})");
            peer.Send(loginPacket, DeliveryMethod.ReliableUnordered);
        }

        public override void OnConnecting(ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamConnectionManager.OnConnecting({info})");
        }

        public override void OnDisconnected(ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamConnectionManager.ConnectionOnDisconnected({info})");
        }

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            Multiplayer.LogDebug(() => $"SteamConnectionManager.Connection(,{size}, {messageNum}, {recvTime}, {channel})");

            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);

            var reader = new NetDataReader(buffer, 0, size);
            transport?.OnNetworkReceive(peer, reader, (byte)channel, DeliveryMethod.ReliableOrdered);
        }
    }
    #endregion


    private SteamPeer CreatePeer(Connection connection)
    {
        var peer = new SteamPeer(nextPeerId++, connection);
        connectionToPeer[connection] = peer;
        peerIdToPeer[peer.Id] = peer;
        return peer;
    }

    private SteamPeer GetPeer(Connection connection)
    {
        return connectionToPeer.TryGetValue(connection, out var peer) ? peer : null;
    }
}

public class SteamConnectionRequest : IConnectionRequest
{
    private readonly Connection connection;
    private readonly ConnectionInfo connectionInfo;
    private readonly SteamPeer peer;

    public SteamConnectionRequest(Connection connection, ConnectionInfo connectionInfo, SteamPeer peer)
    {
        this.connection = connection;
        this.connectionInfo = connectionInfo;
        this.peer = peer;
    }

    public ITransportPeer Accept()
    {
        return peer;
    }
    public void Reject(NetDataWriter data = null)
    {
        if (data != null)
            peer.Send(data, DeliveryMethod.ReliableUnordered);
        connection.Close();
    }

    public IPEndPoint RemoteEndPoint => new(IPAddress.Any, 0);
}


public class SteamPeer : ITransportPeer
{
    private readonly Connection connection;
    private TransportConnectionState _currentState;
    public SteamConnectionRequest connectionRequest;
    public int Id { get; }

    public SteamPeer(int id, Connection connection)
    {
        Id = (int)id;
        this.connection = connection;
    }

    public void Send(NetDataWriter writer, DeliveryMethod deliveryMethod)
    {

        // Map LiteNetLib delivery method to Steam's SendType
        SendType sendType = deliveryMethod switch
        {
            DeliveryMethod.ReliableOrdered => SendType.Reliable,
            DeliveryMethod.ReliableUnordered => SendType.Reliable,
            DeliveryMethod.Unreliable => SendType.Unreliable,
            DeliveryMethod.ReliableSequenced => SendType.Reliable,
            DeliveryMethod.Sequenced => SendType.Unreliable,
            _ => SendType.Reliable
        };

        connection.SendMessage(writer.Data, 0, writer.Length, sendType);
    }

    public void Disconnect(NetDataWriter data = null)
    {
        connection.Close();
    }

    public void OnConnectionStatusChanged(Steamworks.ConnectionState state)
    {

        _currentState = state switch
        {
            Steamworks.ConnectionState.Connected => TransportConnectionState.Connected,
            Steamworks.ConnectionState.Connecting => TransportConnectionState.Connecting,
            Steamworks.ConnectionState.FindingRoute => TransportConnectionState.Connecting,
            Steamworks.ConnectionState.ClosedByPeer => TransportConnectionState.Disconnected,
            Steamworks.ConnectionState.ProblemDetectedLocally => TransportConnectionState.Disconnected,
            Steamworks.ConnectionState.FinWait => TransportConnectionState.Disconnecting,
            Steamworks.ConnectionState.Linger => TransportConnectionState.Disconnecting,
            Steamworks.ConnectionState.Dead => TransportConnectionState.Disconnected,
            Steamworks.ConnectionState.None => TransportConnectionState.Disconnected,
            _ => TransportConnectionState.Disconnected
        };
    }
    public TransportConnectionState ConnectionState => _currentState;
}
