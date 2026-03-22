using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Networking.Managers.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using static UnityModManagerNet.UnityModManager;

namespace Multiplayer.API
{
    public class ClientAPIProvider : IClient
    {
        private readonly NetworkClient client;

        public event Action<IPlayer> OnPlayerConnected;
        public event Action<IPlayer> OnPlayerDisconnected;

        public void RegisterReadyBlock(ModInfo modInfo)
        {
            client.RegisterReadyBlock(modInfo.DisplayName);
        }

        public void CancelReadyBlock(ModInfo modInfo)
        {
            client.CancelReadyBlock(modInfo.DisplayName);
        }

        #region Client Properties
        public byte PlayerId => client.PlayerId;
        public IReadOnlyCollection<IPlayer> Players => client.ClientPlayerWrappers;
        public int PlayerCount => client.ClientPlayerManager.Players.Count + 1; // add 1 for local player

        public IPlayer GetPlayer(byte playerId)
        {
            client.PlayerWrapperCache.TryGetValue(playerId, out var player);
            return player;
        }

        public bool IsConnected => client.IsRunning;

        public int Ping => client.Ping;

        #endregion

        #region Packet API
        public void RegisterPacket<T>(ClientPacketHandler<T> handler) where T : class, IPacket, new()
        {
            client.RegisterExternalPacket<T>(handler);
        }
        public void RegisterSerializablePacket<T>(ClientPacketHandler<T> handler) where T : class, ISerializablePacket, new()
        {
            client.RegisterExternalSerializablePacket<T>(handler);
        }

        public void SendPacketToServer<T>(T packet, bool reliable = true) where T : class, IPacket, new()
        {
            client.SendExternalPacketToServer(packet, reliable);
        }

        public void SendSerializablePacketToServer<T>(T packet, bool reliable = true) where T : class, ISerializablePacket, new()
        {
            client.SendExternalSerializablePacketToServer(packet, reliable);
        }
        #endregion

        #region Class Helpers
        internal ClientAPIProvider(NetworkClient clientInstance)
        {
            this.client = clientInstance;

            client.ClientPlayerManager.OnPlayerConnected += OnPlayerConnectedInternal;
            client.ClientPlayerManager.OnPlayerDisconnected += OnPlayerDisconnectedInternal;
        }

        internal void Dispose()
        {
            client.ClientPlayerManager.OnPlayerConnected -= OnPlayerConnectedInternal;
            client.ClientPlayerManager.OnPlayerDisconnected -= OnPlayerDisconnectedInternal;
        }

        private void OnPlayerConnectedInternal(Components.Networking.Player.NetworkedPlayer networkedPlayer)
        {
            OnPlayerConnected?.Invoke(client.GetWrapper(networkedPlayer));
        }

        private void OnPlayerDisconnectedInternal(Components.Networking.Player.NetworkedPlayer networkedPlayer)
        {
            OnPlayerDisconnected?.Invoke(client.GetWrapper(networkedPlayer));
            client.PlayerWrapperCache.Remove(networkedPlayer.PlayerId);
        }
        #endregion
    }
}
