using DV.InventorySystem;
using DV.JObjectExtstensions;
using DV.ThingTypes;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.SaveGame;

public class NetworkedSaveGameManager : SingletonBehaviour<NetworkedSaveGameManager>
{
    private const string ROOT_KEY = "Multiplayer";
    private const string PLAYERS_KEY = "Players";
    private const string INVENTORY_KEY = "Inventory";
    private const string SHARED_INVENTORY_KEY = "SharedInventory";

    protected override void Awake()
    {
        base.Awake();
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        Inventory.Instance.MoneyChanged += Server_OnMoneyChanged;
        LicenseManager.Instance.LicenseAcquired += Server_OnLicenseAcquired;
        LicenseManager.Instance.JobLicenseAcquired += Server_OnJobLicenseAcquired;
        LicenseManager.Instance.GarageUnlocked += Server_OnGarageUnlocked;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isUnloading)
            return;
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        Inventory.Instance.MoneyChanged -= Server_OnMoneyChanged;
        LicenseManager.Instance.LicenseAcquired -= Server_OnLicenseAcquired;
        LicenseManager.Instance.JobLicenseAcquired -= Server_OnJobLicenseAcquired;
        LicenseManager.Instance.GarageUnlocked -= Server_OnGarageUnlocked;
    }

    #region Server

    private static void Server_OnMoneyChanged(double oldAmount, double newAmount)
    {
        NetworkLifecycle.Instance.Server.SendMoney((float)newAmount);
    }

    private static void Server_OnLicenseAcquired(GeneralLicenseType_v2 license)
    {
        NetworkLifecycle.Instance.Server.SendLicense(license.id, false);
    }

    private static void Server_OnJobLicenseAcquired(JobLicenseType_v2 license)
    {
        NetworkLifecycle.Instance.Server.SendLicense(license.id, true);
    }

    private static void Server_OnGarageUnlocked(GarageType_v2 garage)
    {
        NetworkLifecycle.Instance.Server.SendGarage(garage.id);
    }

    public void Server_UpdateInternalData(SaveGameData data)
    {
        JObject root = data.GetJObject(ROOT_KEY) ?? [];
        JObject players = root.GetJObject(PLAYERS_KEY) ?? [];

        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.Peer == NetworkLifecycle.Instance.Server.SelfPeer || player.LoadingState != PlayerLoadingState.Complete)
                continue;

            JObject playerData = [];
            playerData.SetVector3(SaveGameKeys.Player_position, player.AbsoluteWorldPosition);
            playerData.SetFloat(SaveGameKeys.Player_rotation, player.WorldRotationY);

            // Per-player inventories are stored for a future non-co-op mode; the shared
            // inventory below is what is actually handed out while co-op is the only mode.
            if (player.SavedInventory != null)
            {
                playerData[INVENTORY_KEY] = SerializeInventory(player.SavedInventory);
            }
            else if (players.GetJObject(player.Guid.ToString())?[INVENTORY_KEY] is JArray previousInventory)
            {
                // The client has not reported its inventory yet this session; keep the saved one
                playerData[INVENTORY_KEY] = previousInventory;
            }

            players.SetJObject(player.Guid.ToString(), playerData);
        }

        root.SetJObject(PLAYERS_KEY, players);

        if (SharedInventory != null)
            root[SHARED_INVENTORY_KEY] = SerializeInventory(SharedInventory);

        data.SetJObject(ROOT_KEY, root);
    }

    public JObject Server_GetPlayerData(SaveGameData data, Guid guid)
    {
        return data?.GetJObject(ROOT_KEY)?.GetJObject(PLAYERS_KEY)?.GetJObject(guid.ToString());
    }

    // Where each player was when they left. The save file is only written when the game
    // actually saves, which it often declines to do mid-session, so a player rejoining
    // before then would otherwise be sent back to the default spawn.
    private readonly Dictionary<Guid, (Vector3 Position, float Rotation)> lastKnownPlacement = [];

    public void Server_RememberPlacement(ServerPlayer player)
    {
        if (player == null || player.LoadingState != PlayerLoadingState.Complete)
            return;

        lastKnownPlacement[player.Guid] = (player.AbsoluteWorldPosition, player.WorldRotationY);
        Multiplayer.Log($"Remembered where {player.Username} left off: {player.AbsoluteWorldPosition}");
    }

    public bool Server_TryGetPlacement(SaveGameData data, Guid guid, out Vector3 position, out float rotation)
    {
        if (lastKnownPlacement.TryGetValue(guid, out var placement))
        {
            position = placement.Position;
            rotation = placement.Rotation;
            return true;
        }

        JObject playerData = Server_GetPlayerData(data, guid);
        Vector3? saved = playerData?.GetVector3(SaveGameKeys.Player_position);
        if (saved.HasValue)
        {
            position = saved.Value;
            rotation = playerData.GetFloat(SaveGameKeys.Player_rotation) ?? 0f;
            return true;
        }

        position = default;
        rotation = 0f;
        return false;
    }

    /// <summary>
    ///     The inventory every player receives. Multiplayer is co-op only, so all players
    ///     carry the same items; the server keeps a single shared list.
    /// </summary>
    public PlayerItemSaveData[] SharedInventory { get; private set; }

    public void Server_SetSharedInventory(PlayerItemSaveData[] items)
    {
        SharedInventory = items;
    }

    public PlayerItemSaveData[] Server_GetPlayerInventory(ServerPlayer player, SaveGameData data)
    {
        // Co-op: everyone carries the same items. The shared entries are the single source of
        // truth; sending them here lets the joining client load them through the game's own
        // save path (correct slots, item state), and SharedInventoryManager then adopts them.
        SharedInventoryManager.Instance.Server_SeedFromHostInventory();

        var entries = SharedInventoryManager.Instance.Entries;
        if (entries.Count == 0)
            return [];

        // Slot layout is per-player: reuse the slots this player last had an item of that
        // kind in, so a reconnecting player finds their belongings where they left them
        // instead of in a heap in the backpack.
        List<PlayerItemSaveData> previous = [];
        if (Server_GetPlayerData(data, player.Guid)?[INVENTORY_KEY] is JArray savedInventory)
            previous = [.. DeserializeInventory(savedInventory)];

        HashSet<int> usedSlots = [];
        List<PlayerItemSaveData> items = [];

        foreach (var entry in entries)
        {
            int slot = -1;

            int match = previous.FindIndex(p => p.ItemPrefabName == entry.Value && p.InventorySlotIndex >= 0 && !usedSlots.Contains(p.InventorySlotIndex));
            if (match >= 0)
            {
                slot = previous[match].InventorySlotIndex;
                usedSlots.Add(slot);
                previous.RemoveAt(match);
            }

            items.Add(new PlayerItemSaveData
            {
                NetId = entry.Key,
                ItemPrefabName = entry.Value,
                BelongsToPlayer = true,
                InventorySlotIndex = slot,
                ContainerSlotIndex = -1,
            });
        }

        return items.ToArray();

        /* Per-player inventories (non-co-op mode). Restore this in place of the shared
           inventory above once players are meant to keep separate belongings:

        if (player.SavedInventory != null)
            return player.SavedInventory;

        if (Server_GetPlayerData(data, player.Guid)?[INVENTORY_KEY] is JArray inventory)
            return DeserializeInventory(inventory);

        return [];
        */
    }

    private static JArray SerializeInventory(PlayerItemSaveData[] items)
    {
        JArray array = [];
        foreach (PlayerItemSaveData item in items)
        {
            JObject entry = new()
            {
                ["prefab"] = item.ItemPrefabName,
                ["slot"] = item.InventorySlotIndex,
                ["locked"] = item.InLockedSlot,
                ["dropped"] = item.IsDropped,
                ["grabbed"] = item.IsGrabbed,
            };

            if (item.State != null)
                entry["state"] = item.State;

            array.Add(entry);
        }
        return array;
    }

    private static PlayerItemSaveData[] DeserializeInventory(JArray array)
    {
        List<PlayerItemSaveData> items = [];
        foreach (JToken token in array)
        {
            if (token is not JObject entry)
                continue;

            items.Add(new PlayerItemSaveData
            {
                ItemPrefabName = entry["prefab"]?.Value<string>(),
                BelongsToPlayer = true,
                InventorySlotIndex = entry["slot"]?.Value<int>() ?? -1,
                ContainerSlotIndex = -1,
                InLockedSlot = entry["locked"]?.Value<bool>() ?? false,
                IsDropped = entry["dropped"]?.Value<bool>() ?? false,
                IsGrabbed = entry["grabbed"]?.Value<bool>() ?? false,
                State = entry["state"] as JObject,
            });
        }
        return items.ToArray();
    }

    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedSaveGameManager)}]";
    }
}
