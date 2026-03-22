using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.API;

public class ServerAPIProvider : IServer
{
    private readonly NetworkServer server;

    public event Action<IPlayer> OnPlayerConnected;
    public event Action<IPlayer> OnPlayerDisconnected;
    public event Action<IPlayer> OnPlayerReady;

    #region Server Properties

    public int PlayerCount => server.PlayerCount;

    public IReadOnlyCollection<IPlayer> Players => server.ServerPlayerWrappers;

    public IPlayer GetPlayer(byte PlayerId)
    {
        server.PlayerWrapperCache.TryGetValue(PlayerId, out var player);

        return player;
    }
    #endregion

    #region Packet API
    public void RegisterPacket<T>(ServerPacketHandler<T> handler) where T : class, IPacket, new()
    {
        server.RegisterExternalPacket<T>(handler);
    }
    public void RegisterSerializablePacket<T>(ServerPacketHandler<T> handler) where T : class, ISerializablePacket, new()
    {
        server.RegisterExternalSerializablePacket<T>(handler);
    }


    public void SendPacketToAll<T>(T packet, bool reliable = true, bool excludeSelf = false, IPlayer excludePlayer = null) where T : class, IPacket, new()
    {
        ITransportPeer peer = null;

        if (excludePlayer != null)
            peer = GetPeerFromPlayer(excludePlayer, $"SendPacketToAll<{typeof(T).Name}>");

        server.SendExternalPacketToAll(packet, reliable, peer, excludeSelf);
    }

    public void SendSerializablePacketToAll<T>(T packet, bool reliable = true, bool excludeSelf = false, IPlayer excludePlayer = null) where T : class, ISerializablePacket, new()
    {
        ITransportPeer peer = null;

        if(excludePlayer != null)
            peer = GetPeerFromPlayer(excludePlayer, $"SendSerializablePacketToAll<{typeof(T).Name}>");

        server.SendExternalSerializablePacketToAll(packet, reliable, peer, excludeSelf);
    }

    public void SendPacketToPlayer<T>(T packet, IPlayer player, bool reliable = true) where T : class, IPacket, new()
    {
        var peer = GetPeerFromPlayer(player, $"SendPacketToPlayer<{typeof(T).Name}>");

        if (peer != null)
            server.SendExternalPacketToPlayer(packet, peer, reliable);
    }

    public void SendSerializablePacketToPlayer<T>(T packet, IPlayer player, bool reliable = true) where T : class, ISerializablePacket, new()
    {
        var peer = GetPeerFromPlayer(player, $"SendSerializablePacketToPlayer<{typeof(T).Name}>");

        if (peer != null)
            server.SendExternalSerializablePacketToPlayer(packet, peer, reliable);
    }
    #endregion

    #region Server Util
    public float AnyPlayerSqrMag(GameObject item) => DvExtensions.AnyPlayerSqrMag(item);

    public float AnyPlayerSqrMag(Vector3 anchor) => DvExtensions.AnyPlayerSqrMag(anchor);
    #endregion

    #region Player Management
    public void KickPlayer(IPlayer player)
    {
        server.KickPlayer(GetServerPlayerFromIPlayer(player));
    }

    public void SetPlayerCrewName(IPlayer player, string crewName)
    {
        var serverPlayer = GetServerPlayerFromIPlayer(player);

        if (serverPlayer != null)
            serverPlayer.CrewName = crewName;
    }
    #endregion

    #region Chat
    public void SendServerChatMessage(string message, IPlayer excludePlayer = null)
    {
        var excludedServerPlayer = GetServerPlayerFromIPlayer(excludePlayer);
        if (excludedServerPlayer != null)
            server.ChatManager.ServerMessage(message, null, excludedServerPlayer);
    }

    public void SendWhisperChatMessage(string message, IPlayer player)
    {
        var serverPlayer = GetServerPlayerFromIPlayer(player);
        if (serverPlayer != null)
            server.SendWhisper(message, serverPlayer);
    }

    public bool RegisterChatCommand(string commandLong, string commandShort, Func<string> helpMessage, ChatCommandCallback callback)
    {
        ChatCommandCallbackInternal internalCallback = (message, serverPlayer) =>
        {
            var playerWrapper = server.GetWrapper(serverPlayer);
            callback(message, playerWrapper);
        };

        return server.ChatManager.RegisterChatCommand(commandLong, commandShort, helpMessage, internalCallback);
    }

    public void RegisterChatFilter(ChatFilterDelegate callback)
    {
        ChatFilterDelegateInternal internalCallback = (ref string message, ServerPlayer serverPlayer) =>
        {
            var playerWrapper = server.GetWrapper(serverPlayer);
            return callback(ref message, playerWrapper);
        };

        server.ChatManager.RegisterChatFilter(internalCallback);
    }
    #endregion

    #region Class Helpers
    internal ServerAPIProvider(NetworkServer serverInstance)
    {
        this.server = serverInstance;

        server.PlayerConnected += OnPlayerConnectedInternal;
        server.PlayerDisconnected += OnPlayerDisconnectedInternal;
        server.PlayerReady += OnPlayerReadyInternal;
    }

    private ITransportPeer GetPeerFromPlayer(IPlayer player, string operationName)
    {
        if (player == null)
        {
            server.LogDebug(() => $"{operationName}: Player is null");
            return null;
        }

        if (player is ServerPlayerWrapper playerWrapper)
        {
            return playerWrapper.Peer;
        }

        server.LogWarning($"{operationName}: Player '{player.Username}' is not a ServerPlayerWrapper (got {player.GetType().Name})");
        return null;
    }

    private ServerPlayer GetServerPlayerFromIPlayer(IPlayer player)
    {
        if (player == null)
            return null;

        if (player is ServerPlayerWrapper wrapper)
            return wrapper._serverPlayer;

        server.LogWarning($"GetServerPlayerFromIPlayer: Player '{player.Username}' is not a ServerPlayerWrapper (got {player.GetType().Name})");
        return null;
    }

    internal void Dispose()
    {
        server.PlayerConnected -= OnPlayerConnectedInternal;
        server.PlayerDisconnected -= OnPlayerDisconnectedInternal;
    }

    private void OnPlayerConnectedInternal(ServerPlayer serverPlayer)
    {
        OnPlayerConnected?.Invoke(server.GetWrapper(serverPlayer));
    }

    private void OnPlayerDisconnectedInternal(ServerPlayer serverPlayer)
    {
        // Get wrapper before removing from cache
        var wrapper = server.GetWrapper(serverPlayer);
        OnPlayerDisconnected?.Invoke(wrapper);
        server.PlayerWrapperCache.Remove(serverPlayer.PlayerId);
    }

    private void OnPlayerReadyInternal(ServerPlayer serverPlayer)
    {
        OnPlayerReady?.Invoke(server.GetWrapper(serverPlayer));
    }
    #endregion
}
