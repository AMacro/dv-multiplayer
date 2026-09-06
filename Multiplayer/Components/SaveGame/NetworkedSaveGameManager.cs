using DV.InventorySystem;
using DV.JObjectExtstensions;
using DV.ThingTypes;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Components.Networking.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Multiplayer.Components.SaveGame;

public class NetworkedSaveGameManager : SingletonBehaviour<NetworkedSaveGameManager>
{
    private const string ROOT_KEY = "Multiplayer";
    private const string PLAYERS_KEY = "Players";
    private const string USERNAME_KEY = "Username";
    private const string STEAM_ID_KEY = "SteamId";
    private const string LAST_CONNECTED_KEY = "LastConnectedUtc";
    private const string INVENTORY_KEY = "Inventory";
    private const string INVENTORY_VERSION_KEY = "Version";
    private const string INVENTORY_ITEMS_KEY = "Items";
    private const int INVENTORY_VERSION = 1;

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

            JObject playerData = players.GetJObject(player.Guid.ToString()) ?? [];
            UpdatePlayerMetadata(playerData, player);
            if (player.InventoryReconciliationComplete &&
                NetworkedItemManager.Instance.TryCaptureCarriedInventory(player, out var inventory))
                SetInventory(playerData, inventory);
            players.SetJObject(player.Guid.ToString(), playerData);
        }

        root.SetJObject(PLAYERS_KEY, players);
        data.SetJObject(ROOT_KEY, root);
    }

    public JObject Server_GetPlayerData(SaveGameData data, Guid guid)
    {
        return data?.GetJObject(ROOT_KEY)?.GetJObject(PLAYERS_KEY)?.GetJObject(guid.ToString());
    }

    public bool TryGetPlayerInventory(SaveGameData data, Guid guid, out PlayerItemSaveData[] items)
    {
        items = [];
        JObject inventory = Server_GetPlayerData(data, guid)?.GetJObject(INVENTORY_KEY);
        JToken itemToken = inventory?[INVENTORY_ITEMS_KEY];
        if (inventory == null || itemToken == null)
            return false;

        try
        {
            items = itemToken.ToObject<PlayerItemSaveData[]>() ?? [];
            return true;
        }
        catch (Exception exception)
        {
            Multiplayer.LogError($"Failed to deserialize stored inventory for {guid}: {exception}");
            return false;
        }
    }

    public void StorePlayerInventory(ServerPlayer player, IReadOnlyCollection<PlayerItemSaveData> items)
    {
        SaveGameData data = SaveGameManager.Instance?.data;
        if (data == null || player == null)
            return;

        JObject root = data.GetJObject(ROOT_KEY) ?? [];
        JObject players = root.GetJObject(PLAYERS_KEY) ?? [];
        JObject playerData = players.GetJObject(player.Guid.ToString()) ?? [];
        UpdatePlayerMetadata(playerData, player);
        SetInventory(playerData, items);
        players.SetJObject(player.Guid.ToString(), playerData);
        root.SetJObject(PLAYERS_KEY, players);
        data.SetJObject(ROOT_KEY, root);
    }

    public void StorePlayerMetadata(ServerPlayer player)
    {
        SaveGameData data = SaveGameManager.Instance?.data;
        if (data == null || player == null)
            return;

        JObject root = data.GetJObject(ROOT_KEY) ?? [];
        JObject players = root.GetJObject(PLAYERS_KEY) ?? [];
        JObject playerData = players.GetJObject(player.Guid.ToString()) ?? [];
        UpdatePlayerMetadata(playerData, player);
        players.SetJObject(player.Guid.ToString(), playerData);
        root.SetJObject(PLAYERS_KEY, players);
        data.SetJObject(ROOT_KEY, root);
    }

    public void RecordPlayerLogin(ServerPlayer player)
    {
        SaveGameData data = SaveGameManager.Instance?.data;
        if (data == null || player == null)
            return;

        JObject root = data.GetJObject(ROOT_KEY) ?? [];
        JObject players = root.GetJObject(PLAYERS_KEY) ?? [];
        JObject playerData = players.GetJObject(player.Guid.ToString()) ?? [];
        UpdateIdentityMetadata(playerData, player);
        players.SetJObject(player.Guid.ToString(), playerData);
        root.SetJObject(PLAYERS_KEY, players);
        data.SetJObject(ROOT_KEY, root);
    }

    public IReadOnlyList<SavedPlayerInventory> GetSavedPlayerInventories()
    {
        var result = new List<SavedPlayerInventory>();
        SaveGameData data = SaveGameManager.Instance?.data;
        JObject players = data?.GetJObject(ROOT_KEY)?.GetJObject(PLAYERS_KEY);
        if (players == null)
            return result;

        foreach (JProperty property in players.Properties())
        {
            if (!Guid.TryParse(property.Name, out Guid guid) || property.Value is not JObject playerData ||
                !TryGetPlayerInventory(data, guid, out PlayerItemSaveData[] items))
                continue;

            ulong.TryParse(playerData.GetString(STEAM_ID_KEY), out ulong steamId);
            bool hasTimestamp = DateTime.TryParse(playerData.GetString(LAST_CONNECTED_KEY), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime lastConnectedUtc);
            if (hasTimestamp)
                lastConnectedUtc = lastConnectedUtc.ToUniversalTime();
            else
                lastConnectedUtc = DateTime.MinValue;
            result.Add(new SavedPlayerInventory(guid, playerData.GetString(USERNAME_KEY), steamId,
                lastConnectedUtc, items));
        }
        return result;
    }

    public void ResetPlayerInventory(Guid guid)
    {
        SaveGameData data = SaveGameManager.Instance?.data;
        JObject playerData = Server_GetPlayerData(data, guid);
        if (playerData == null)
            return;
        // Absence means first-time provisioning on the next connection. Keep the
        // identity/timestamp metadata so administrative lookup remains available.
        playerData.Remove(INVENTORY_KEY);
        ShopPurchaseCoordinator.RequestStockRecount();
    }

    private static void UpdatePlayerMetadata(JObject playerData, ServerPlayer player)
    {
        UpdateIdentityMetadata(playerData, player);
        playerData.SetVector3(SaveGameKeys.Player_position, player.AbsoluteWorldPosition);
        playerData.SetFloat(SaveGameKeys.Player_rotation, player.WorldRotationY);
    }

    private static void UpdateIdentityMetadata(JObject playerData, ServerPlayer player)
    {
        playerData.SetString(USERNAME_KEY, player.OriginalUsername);
        playerData.SetString(STEAM_ID_KEY, player.SteamId == 0 ? string.Empty : player.SteamId.ToString(CultureInfo.InvariantCulture));
        playerData.SetString(LAST_CONNECTED_KEY, player.LastLogin.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static void SetInventory(JObject playerData, IReadOnlyCollection<PlayerItemSaveData> items)
    {
        playerData.SetJObject(INVENTORY_KEY, new JObject
        {
            [INVENTORY_VERSION_KEY] = INVENTORY_VERSION,
            [INVENTORY_ITEMS_KEY] = JArray.FromObject(items ?? [], JsonSerializer.CreateDefault()),
        });
    }

    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedSaveGameManager)}]";
    }
}

public sealed class SavedPlayerInventory
{
    public Guid Guid { get; }
    public string Username { get; }
    public ulong SteamId { get; }
    public DateTime LastConnectedUtc { get; }
    public PlayerItemSaveData[] Items { get; }

    public SavedPlayerInventory(Guid guid, string username, ulong steamId, DateTime lastConnectedUtc,
        PlayerItemSaveData[] items)
    {
        Guid = guid;
        Username = username;
        SteamId = steamId;
        LastConnectedUtc = lastConnectedUtc;
        Items = items ?? [];
    }
}
