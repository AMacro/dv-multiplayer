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
    public NetStatistics Statistics => new();
    public bool IsRunning { get; private set; }

    public event Action<NetDataReader, IConnectionRequest> OnConnectionRequest;
    public event Action<ITransportPeer> OnPeerConnected;
    public event Action<ITransportPeer, DisconnectReason> OnPeerDisconnected;
    public event Action<ITransportPeer, NetDataReader, byte, DeliveryMethod> OnNetworkReceive;
    public event Action<IPEndPoint, SocketError> OnNetworkError;
    public event Action<ITransportPeer, int> OnNetworkLatencyUpdate;

    private readonly List<SteamServerManager> servers = [];
    private SteamClientManager client;


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

        var server = SteamNetworkingSockets.CreateNormalSocket<SteamServerManager>(NetAddress.AnyIp((ushort)port));
        if (server != null)
        {
            Multiplayer.LogDebug(() => $"SteamWorksTransport.Start({port}) Normal not null");
            server.transport = this;
            servers.Add(server);
            IsRunning = true;
        }

        server = SteamNetworkingSockets.CreateRelaySocket<SteamServerManager>();
        
        if (server != null)
        {
            Multiplayer.LogDebug(() => $"SteamWorksTransport.Start({port}) Relay not null");
            server.transport = this;
            servers.Add(server);
            IsRunning = true;

            Multiplayer.Log($"SteamId: {Steamworks.Data.NetIdentity.LocalHost}");
        }
        

        return IsRunning;
    }

    public bool Start(IPAddress ipv4, IPAddress ipv6, int port)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Start({ipv4}, {ipv6}, {port})");
        return Start(port);
    }

    public void Stop(bool sendDisconnectPackets)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.Stop()");

        client?.Close(true);

        foreach (var server in servers)
        {
            if (server != null)
            {
                // Close all connections first
                foreach (var connection in server.Connected)
                {
                    connection.Close(true, (int)NetConnectionEnd.App_Generic);
                }

                //close the server
                server.Close();
            }
        }

        servers.Clear();
    }

    public void PollEvents()
    {
        SteamClient.RunCallbacks();

        client?.Receive();
        

        foreach (var server in servers)
        {
            server?.Receive();
        }

        //update pings
        foreach (var kvp in connectionToPeer)
        {
            var peer = kvp.Value;
            var connection = kvp.Key;

            if(peer != null && connection != null)
                OnNetworkLatencyUpdate?.Invoke(peer, connection.QuickStatus().Ping / 2); //nromalise to match LiteNetLib's implementation
        }
    }

    public ITransportPeer Connect(string address, int port, NetDataWriter data)
    {
        //Multiplayer.LogDebug(() => $"SteamWorksTransport.Connect({address}, {port}, {data.Length})");

        if (port < 0)
            return ConnectRelay(address, data);
        else
            return ConnectNative(address, port, data);
    }

    public ITransportPeer ConnectNative(string address, int port, NetDataWriter data)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.ConnectNative({address}, {port}, {data.Length})");

        var add = NetAddress.From(address, (ushort)port);


        //Multiplayer.LogDebug(() => $"SteamWorksTransport.Connect packet: {BitConverter.ToString(data.Data)}");

        // Create connection manager for client
        client = SteamNetworkingSockets.ConnectNormal<SteamClientManager>(add);
        client.transport = this;
        client.loginPacket = data;
        client.peer = CreatePeer(client.Connection);

        return client.peer;
    }

    public ITransportPeer ConnectRelay(string steamID, NetDataWriter data)
    {
        Multiplayer.LogDebug(() => $"SteamWorksTransport.ConnectRelay({steamID})");

        SteamId id = new();
        if (!ulong.TryParse(steamID, out id.Value))
        {
            Multiplayer.LogDebug(() => $"SteamWorksTransport.ConnectRelay({steamID}) failed to parse");
            return null;
        }


        //Multiplayer.LogDebug(() => $"SteamWorksTransport.ConnectRelay packet: {BitConverter.ToString(data.Data)}");

        // Create connection manager for client
        client = SteamNetworkingSockets.ConnectRelay<SteamClientManager>(id);
        client.transport = this;
        client.loginPacket = data;
        client.peer = CreatePeer(client.Connection);

        return client.peer;
    }


    public void Send(ITransportPeer peer, NetDataWriter writer, DeliveryMethod deliveryMethod)
    {
        //Multiplayer.LogDebug(() => $"SteamWorksTransport.Send({peer.Id}, {deliveryMethod})");
        peer.Send(writer, deliveryMethod);
    }

    public void UpdateSettings(Settings settings)
    {
        //todo: implement any settings
    }

    #endregion

    #region SteamManagers
    public class SteamServerManager : SocketManager
    {
        public SteamWorksTransport transport;

        public override void OnConnecting(Connection connection, ConnectionInfo info)
        {

            //Multiplayer.LogDebug(() => $"SteamServerManager.OnConnecting({connection}, {info})");
            connection.Accept();
        }

        public override void OnConnected(Connection connection, ConnectionInfo info)
        {
            //Multiplayer.LogDebug(() => $"SteamServerManager.OnConnected({connection}, {info})");
            base.OnConnected(connection, info);

            var peer = transport.CreatePeer(connection);
            peer.connectionRequest = new SteamConnectionRequest(connection, info, peer);
            transport?.OnPeerConnected?.Invoke(peer);
        }

        public override void OnDisconnected(Steamworks.Data.Connection connection, Steamworks.Data.ConnectionInfo info)
        {
            //Multiplayer.LogDebug(() => $"SteamServerManager.OnDisconnected({connection}, {info})");
            base.OnDisconnected(connection, info);
            var peer = transport.GetPeer(connection);

            transport?.OnPeerDisconnected?.Invoke(peer, NetConnectionEndToDisconnectReason(info.EndReason));
        }

        public override void OnMessage(Steamworks.Data.Connection connection, Steamworks.Data.NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            //Multiplayer.LogDebug(() => $"SteamServerManager.OnMessage({connection}, {identity}, , {size}, {messageNum}, {recvTime}, {channel})");

            var peer = transport.GetPeer(connection);

            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);


            //Multiplayer.LogDebug(() => $"SteamServerManager.Received packet: {BitConverter.ToString(buffer)}");

            var reader = new NetDataReader(buffer, 0, size);
            if (peer.connectionRequest != null)
            {
                transport?.OnConnectionRequest?.Invoke(reader, peer.connectionRequest);
                peer.connectionRequest = null;
                return;
            }

            transport?.OnNetworkReceive?.Invoke(peer, reader, (byte)channel, DeliveryMethod.ReliableOrdered);

            //base.OnMessage(connection,identity,data,size,messageNum,recvTime,channel);
        }

        public override void OnConnectionChanged(Steamworks.Data.Connection connection, Steamworks.Data.ConnectionInfo info)
        {
            //Multiplayer.LogDebug(() => $"SteamServerManager.OnConnectionChanged({connection}, {info})");
            base.OnConnectionChanged(connection, info);
            if (transport.GetPeer(connection) is SteamPeer peer)
            {
                peer.OnConnectionStatusChanged(info.State);
            }
        }
    }

    public class SteamClientManager : ConnectionManager
    {
        public SteamWorksTransport transport;
        public NetDataWriter loginPacket;
        public SteamPeer peer;

        public override void OnConnected(ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamClientManager.OnConnected({info})");
            base.OnConnected(info);
            transport.IsRunning = true;
            peer.Send(loginPacket, DeliveryMethod.ReliableUnordered);
            transport?.OnPeerConnected?.Invoke(peer);
        }

        public override void OnConnecting(ConnectionInfo info)
        {
            //Multiplayer.LogDebug(() => $"SteamClientManager.OnConnecting({info})");
            base.OnConnecting(info);
        }

        public override void OnDisconnected(ConnectionInfo info)
        {
            Multiplayer.LogDebug(() => $"SteamClientManager.OnDisconnected({info.EndReason})");
            base.OnDisconnected(info);
            transport?.OnPeerDisconnected?.Invoke(peer, NetConnectionEndToDisconnectReason(info.EndReason));
        }

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            //Multiplayer.LogDebug(() => $"SteamClientManager.Connection(,{size}, {messageNum}, {recvTime}, {channel})");

            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);

            var reader = new NetDataReader(buffer, 0, size);
            transport?.OnNetworkReceive?.Invoke(peer, reader, (byte)channel, DeliveryMethod.ReliableOrdered);
            //base.OnMessage(data, size, messageNum, recvTime, channel);
        }

        public override void OnConnectionChanged(ConnectionInfo info)
        {
            base.OnConnectionChanged(info);
            peer?.OnConnectionStatusChanged(info.State);
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

    public static DisconnectReason NetConnectionEndToDisconnectReason(NetConnectionEnd reason)
    {
        return reason switch
        {
            NetConnectionEnd.Remote_Timeout => DisconnectReason.Timeout,
            NetConnectionEnd.Misc_Timeout => DisconnectReason.Timeout,
            NetConnectionEnd.Remote_BadProtocolVersion => DisconnectReason.InvalidProtocol,
            NetConnectionEnd.Remote_BadCrypt => DisconnectReason.ConnectionFailed,
            NetConnectionEnd.Remote_BadCert => DisconnectReason.ConnectionRejected,
            NetConnectionEnd.Local_OfflineMode => DisconnectReason.NetworkUnreachable,
            NetConnectionEnd.Local_NetworkConfig => DisconnectReason.NetworkUnreachable,
            NetConnectionEnd.Misc_P2P_NAT_Firewall => DisconnectReason.PeerToPeerConnection,
            NetConnectionEnd.Local_P2P_ICE_NoPublicAddresses => DisconnectReason.PeerNotFound,
            NetConnectionEnd.Remote_P2P_ICE_NoPublicAddresses => DisconnectReason.PeerNotFound,
            NetConnectionEnd.Misc_PeerSentNoConnection => DisconnectReason.PeerNotFound,
            NetConnectionEnd.App_Generic => DisconnectReason.DisconnectPeerCalled,
            _ => DisconnectReason.ConnectionFailed
        };
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
            peer?.Send(data, DeliveryMethod.ReliableUnordered);

        connection.Close(true);
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
        //Multiplayer.LogDebug(() => $"SteamPeer.Send({writer.Data.Length})\r\n{Environment.StackTrace}");
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
        if (data != null)
            Send(data, DeliveryMethod.ReliableUnordered);

        connection.Close(true);
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
