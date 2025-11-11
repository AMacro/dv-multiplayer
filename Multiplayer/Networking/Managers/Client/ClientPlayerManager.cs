using DV;
using Multiplayer.Components.Networking.Player;
using System.Collections.Generic;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Multiplayer.Networking.Managers.Client;

public class ClientPlayerManager
{
    private readonly Dictionary<byte, NetworkedPlayer> playerMap = new();

    public Action<NetworkedPlayer> OnPlayerConnected;
    public Action<NetworkedPlayer> OnPlayerDisconnected;
    public IReadOnlyCollection<NetworkedPlayer> Players => playerMap.Values;

    private readonly GameObject playerPrefab;

    public ClientPlayerManager()
    {
        playerPrefab = Multiplayer.AssetIndex.playerPrefab;
    }

    public bool TryGetPlayer(byte playerid, out NetworkedPlayer player)
    {
        return playerMap.TryGetValue(playerid, out player);
    }

    public void AddPlayer(byte playerId, string username)
    {
        GameObject go = Object.Instantiate(playerPrefab, WorldMover.OriginShiftParent);
        go.layer = LayerMask.NameToLayer(Layers.Player);
        NetworkedPlayer networkedPlayer = go.AddComponent<NetworkedPlayer>();
        networkedPlayer.PlayerId = playerId;
        networkedPlayer.Username = username;
        //networkedPlayer.Guid = guid;
        playerMap.Add(playerId, networkedPlayer);
        OnPlayerConnected?.Invoke(networkedPlayer);
    }

    public void RemovePlayer(byte playerid)
    {
        if (!TryGetPlayer(playerid, out NetworkedPlayer networkedPlayer))
            return;

        OnPlayerDisconnected?.Invoke(networkedPlayer);
        Object.Destroy(networkedPlayer.gameObject);
        playerMap.Remove(playerid);
    }

    public void UpdatePing(byte playerId, int ping)
    {
        if (!TryGetPlayer(playerId, out NetworkedPlayer player))
            return;
        player.SetPing(ping);
    }

    public void UpdatePosition(byte playerid, Vector3 position, Vector3 moveDir, float rotation, bool isJumping, bool isOnCar, ushort carId)
    {
        if (!TryGetPlayer(playerid, out NetworkedPlayer player))
            return;
        player.UpdateCar(carId);
        player.UpdatePosition(position, moveDir, rotation, isJumping, isOnCar);
    }

    //public void UpdateCar(byte playerId, ushort carId)
    //{
    //    if (!playerMap.TryGetValue(playerId, out NetworkedPlayer player))
    //        return;
    //    player.UpdateCar(carId);
    //}
}
