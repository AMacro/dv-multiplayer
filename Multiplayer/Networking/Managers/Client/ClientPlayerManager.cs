using System;
using System.Collections.Generic;
using DV;
using Multiplayer.Components.Networking.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Multiplayer.Networking.Listeners;

public class ClientPlayerManager
{
    private readonly Dictionary<byte, NetworkedPlayer> playerMap = new();

    public Action<byte, NetworkedPlayer> OnPlayerConnected;
    public Action<byte, NetworkedPlayer> OnPlayerDisconnected;
    public IReadOnlyCollection<NetworkedPlayer> Players => playerMap.Values;

    private readonly GameObject playerPrefab;

    public ClientPlayerManager()
    {
        playerPrefab = Multiplayer.AssetIndex.playerPrefab;
    }

    public bool TryGetPlayer(byte id, out NetworkedPlayer player)
    {
        return playerMap.TryGetValue(id, out player);
    }

    public void AddPlayer(byte id, string username, Guid guid)
    {
        GameObject go = Object.Instantiate(playerPrefab, WorldMover.Instance.originShiftParent);
        go.layer = LayerMask.NameToLayer(Layers.Player);
        NetworkedPlayer networkedPlayer = go.AddComponent<NetworkedPlayer>();
        networkedPlayer.Id = id;
        networkedPlayer.Username = username;
        networkedPlayer.Guid = guid;
        playerMap.Add(id, networkedPlayer);
        OnPlayerConnected?.Invoke(id, networkedPlayer);
    }

    public void RemovePlayer(byte id)
    {
        if (!playerMap.TryGetValue(id, out NetworkedPlayer networkedPlayer))
            return;
        OnPlayerDisconnected?.Invoke(id, networkedPlayer);
        Object.Destroy(networkedPlayer.gameObject);
        playerMap.Remove(id);
    }

    public void UpdatePing(byte id, int ping)
    {
        if (!playerMap.TryGetValue(id, out NetworkedPlayer player))
            return;
        player.SetPing(ping);
    }

    public void UpdatePosition(byte id, Vector3 position, Vector3 moveDir, float rotation, bool isJumping, bool isOnCar)
    {
        if (!playerMap.TryGetValue(id, out NetworkedPlayer player))
            return;
        player.UpdatePosition(position, moveDir, rotation, isJumping, isOnCar);
    }

    public void UpdateCar(byte playerId, ushort carId)
    {
        if (!playerMap.TryGetValue(playerId, out NetworkedPlayer player))
            return;
        player.UpdateCar(carId);
    }
}
