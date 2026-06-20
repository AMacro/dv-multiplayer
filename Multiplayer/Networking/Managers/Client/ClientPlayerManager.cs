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
    public Action<NetworkedPlayer> OnPlayerPrefsUpdated;
    public IReadOnlyCollection<NetworkedPlayer> Players => playerMap.Values;

    private readonly GameObject playerTagPrefab;
    private readonly GameObject[] playerPrefabs;

    public ClientPlayerManager()
    {
        playerTagPrefab = Multiplayer.AssetIndex.PlayerTag;
        playerPrefabs = Multiplayer.AssetIndex.playerPrefabs;
    }

    public bool TryGetPlayer(byte playerid, out NetworkedPlayer player)
    {
        return playerMap.TryGetValue(playerid, out player);
    }

    public void AddPlayer(byte playerId, string username, string crewName, string prefabId)
    {
        if (playerMap.ContainsKey(playerId))
        {
            Multiplayer.LogWarning($"Player with id {playerId} already exists. Removing existing player {playerMap[playerId].Username}");
            RemovePlayer(playerId);
        }

        // Player model holder
        GameObject go = new($"Player[{username}]");
        go.transform.SetParent(WorldMover.OriginShiftParent);
        go.layer = LayerMask.NameToLayer(Layers.Player);

        // Setup player tag
        Object.Instantiate(playerTagPrefab, go.transform);
        NetworkedPlayer networkedPlayer = go.AddComponent<NetworkedPlayer>();

        networkedPlayer.PlayerId = playerId;
        networkedPlayer.Username = username;
        networkedPlayer.CrewName = crewName;

        networkedPlayer.ChangeModel(playerPrefabs[0]);

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

    // Currently only updates crew name, but can be expanded to include other preferences in the future, e.g. player model, marker color, etc.
    public void UpdatePreferences(byte playerId, string crewName)
    {
        Multiplayer.LogDebug(()=>$"Updating preferences for playerId: {playerId}, CrewName:{crewName}");

        if (!TryGetPlayer(playerId, out NetworkedPlayer player))
            return;

        Multiplayer.LogDebug(() => $"Updating preferences for playerId: {playerId}, CrewName: {crewName}, Found: {player.Username}");

        player.CrewName = crewName;
        OnPlayerPrefsUpdated?.Invoke(player);
    }

    //public void UpdateCar(byte playerId, ushort carId)
    //{
    //    if (!playerMap.TryGetValue(playerId, out NetworkedPlayer player))
    //        return;
    //    player.UpdateCar(carId);
    //}
}
