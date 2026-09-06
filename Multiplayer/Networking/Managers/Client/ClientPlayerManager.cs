using DV;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Networking.Data.Player;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Multiplayer.Networking.Managers.Client;

public class ClientPlayerManager : IDisposable
{
    private const float CULLING_CHECK_INTERVAL = 2f;
    private const float CULL_SQR_DISTANCE = 150f * 150f;
    private const float ACTIVATE_SQR_DISTANCE = 145f * 145f;

    private readonly Dictionary<byte, NetworkedPlayer> playerMap = new();

    public Action<NetworkedPlayer> OnPlayerConnected;
    public Action<NetworkedPlayer> OnPlayerDisconnected;
    public Action<NetworkedPlayer> OnPlayerPrefsUpdated;
    public IReadOnlyCollection<NetworkedPlayer> Players => playerMap.Values;

    private GameObject playerTagPrefab => Multiplayer.AssetIndex.PlayerTag;

    private Coroutine cullingRoutine;

    public ClientPlayerManager()
    {
        cullingRoutine = CoroutineManager.Instance.StartCoroutine(CullingCoro());
    }

    public void StartCulling()
    {
        if (cullingRoutine != null)
        {
            CoroutineManager.Instance.Stop(cullingRoutine);
            cullingRoutine = null;
        }

        cullingRoutine = CoroutineManager.Instance.StartCoroutine(CullingCoro());
    }

    void IDisposable.Dispose()
    {
        if (UnloadWatcher.isQuitting)
            return;

        if (cullingRoutine != null)
            CoroutineManager.Instance?.Stop(cullingRoutine);
    }

    public bool TryGetPlayer(byte playerid, out NetworkedPlayer player)
    {
        return playerMap.TryGetValue(playerid, out player);
    }

    public void AddPlayer(byte playerId, string username, string crewName, string characterId, bool isVr)
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
        networkedPlayer.IsVR = isVr;

        // Get player model from registry and apply it to the player
        var model = Multiplayer.PlayerModelRegistry.GetModelById(characterId);

        networkedPlayer.ChangeModel(model.Prefab);

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

    public void UpdatePosition(byte playerId, PlayerTrackingData trackingData, PlayerPostureFlags posture, bool isOnCar, ushort carId)
    {
        if (!TryGetPlayer(playerId, out NetworkedPlayer player))
            return;
        player.UpdateCar(carId);
        player.UpdatePosition(trackingData, posture, isOnCar);
    }

    public void UpdatePreferences(byte playerId, Dictionary<PlayerPreference, string> preferences)
    {
        Multiplayer.LogDebug(() => $"Updating preferences for playerId: {playerId}, Preference count : {preferences?.Count}");

        if (!TryGetPlayer(playerId, out NetworkedPlayer player))
            return;

        if (preferences.TryGetValue(PlayerPreference.CrewName, out string crewName))
            player.CrewName = crewName;

        if (preferences.TryGetValue(PlayerPreference.CharacterModel, out string characterId))
        {
            var model = Multiplayer.PlayerModelRegistry.GetModelById(characterId);
            player.ChangeModel(model.Prefab);
        }

        OnPlayerPrefsUpdated?.Invoke(player);
    }

    private IEnumerator CullingCoro()
    {
        while (true)
        {
            yield return new WaitForSeconds(CULLING_CHECK_INTERVAL);

            Vector3 localPos = PlayerManager.PlayerTransform?.position ?? Vector3.zero;

            if (Players == null || Players.Count == 0)
                continue;

            foreach (var player in Players)
            {
                if (player == null)
                    continue;

                float sqrDist = (player.transform.position - localPos).sqrMagnitude;

                if (player.IsCulled && sqrDist < ACTIVATE_SQR_DISTANCE)
                    player.IsCulled = false;
                else if (!player.IsCulled && sqrDist > CULL_SQR_DISTANCE)
                    player.IsCulled = true;
            }
        }
    }
}
