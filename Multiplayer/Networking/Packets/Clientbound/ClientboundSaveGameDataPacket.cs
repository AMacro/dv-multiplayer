using DV.InventorySystem;
using DV.JObjectExtstensions;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.UserManagement;
using Multiplayer.Components.Networking;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundSaveGameDataPacket
{
    public string GameMode { get; set; }
    public string SerializedDifficulty { get; set; }
    public float Money { get; set; }
    public string[] AcquiredGeneralLicenses { get; set; }
    public string[] AcquiredJobLicenses { get; set; }
    public string[] UnlockedGarages { get; set; }
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }

    public bool HasDebt { get; set; }
    // public string[] Debt_existing_locos { get; set; }
    // public string[] Debt_deleted_locos { get; set; }
    // public string[] Debt_existing_jobs { get; set; }
    // public string[] Debt_staged_jobs { get; set; }
    // public string Debt_existing_jobless_cars { get; set; }
    // public string Debt_deleted_jobless_cars { get; set; }
    // public string Debt_insurance { get; set; }

    public PlayerItemSaveData[] PlayerItems { get; set; }
    public bool HasSavedInventory { get; set; }

    public float JobManagerTime { get; set; }

    public static ClientboundSaveGameDataPacket CreatePacket(ServerPlayer player)
    {
        Multiplayer.LogDebug(() => $"ClientboundSaveGameDataPacket.CreatePacket() for player (is null: {player == null}) {player?.Username} ({player?.Guid})");
        if (WorldStreamingInit.isLoaded)
            SaveGameManager.Instance.UpdateInternalData();

        SaveGameData data = SaveGameManager.Instance.data;

        JObject difficulty = new();
        DifficultyDataUtils.SetDifficultyToJSON(difficulty, NetworkLifecycle.Instance.Server.Difficulty);

        JObject playerData = NetworkedSaveGameManager.Instance.Server_GetPlayerData(data, player.Guid);

        Multiplayer.LogDebug(() =>
        {
            string unlockedGen = string.Join(", ", UnlockablesManager.Instance.UnlockedGeneralLicenses);
            string packetGen = string.Join(", ", data.GetStringArray(SaveGameKeys.Licenses_General));

            string unlockedJob = string.Join(", ", UnlockablesManager.Instance.UnlockedJobLicenses);
            string packetJob = string.Join(", ", data.GetStringArray(SaveGameKeys.Licenses_Jobs));

            return $"ClientboundSaveGameDataPacket.CreatePacket() UnlockedGen: {{{unlockedGen}}}, PacketGen: {{{packetGen}}},  UnlockedJob: {{{unlockedJob}}}, PacketJob: {{{packetJob}}}";
        });

        bool hasSavedInventory = NetworkedSaveGameManager.Instance.TryGetPlayerInventory(
            data, player.Guid, out PlayerItemSaveData[] savedItems);
        List<PlayerItemSaveData> playerItems = hasSavedInventory
            ? new List<PlayerItemSaveData>(savedItems)
            : CreateDefaultInventory();
        if (hasSavedInventory)
            RepairInvalidRootInventorySlots(playerItems, player.Guid);

        return new ClientboundSaveGameDataPacket
        {
            GameMode = data.GetString(SaveGameKeys.Game_mode),
            SerializedDifficulty = difficulty.ToString(Formatting.None),
            Money = StartingItemsController.Instance == null || !StartingItemsController.Instance.itemsLoaded ? data.GetFloat(SaveGameKeys.Player_money).GetValueOrDefault(0) : (float)Inventory.Instance.PlayerMoney,
            AcquiredGeneralLicenses = data.GetStringArray(SaveGameKeys.Licenses_General),
            AcquiredJobLicenses = data.GetStringArray(SaveGameKeys.Licenses_Jobs),
            UnlockedGarages = data.GetStringArray(SaveGameKeys.Garages),
            Position = playerData?.GetVector3(SaveGameKeys.Player_position) ?? LevelInfo.DefaultSpawnPosition,
            Rotation = playerData?.GetFloat(SaveGameKeys.Player_rotation) ?? LevelInfo.DefaultSpawnRotation.y,
            HasDebt = data.GetFloat(SaveGameKeys.Debt_total).GetValueOrDefault(CareerManagerDebtController.Instance != null ? CareerManagerDebtController.Instance.NumberOfNonZeroPricedDebts : 0) > 0,
            JobManagerTime = JobsManager.Instance.Time,
            PlayerItems = playerItems.ToArray(),
            HasSavedInventory = hasSavedInventory,
        };
    }

    private static List<PlayerItemSaveData> CreateDefaultInventory()
    {
        List<PlayerItemSaveData> playerItems = [];
        string[] items = ["shovel", "lighter", "Oiler", "Lantern", "Flashlight", "Hanger", "DuctTape"];
        string[] states = ["", "", "", "", "{\"Restock\": true,\"Battery_power\": 100}", "", ""];

        for (int i = 0; i < items.Length; i++)
        {
            JObject state;

            if (!string.IsNullOrEmpty(states[i]))
                state = JObject.Parse(states[i]);
            else
                state = [];

            var testItem = new PlayerItemSaveData()
            {
                ItemPrefabName = items[i],
                BelongsToPlayer = true,
                InventorySlotIndex = 14 + i,
                State = state
            };
            playerItems.Add(testItem);
        }

        return playerItems;
    }

    private static void RepairInvalidRootInventorySlots(List<PlayerItemSaveData> items, System.Guid playerGuid)
    {
        const int inventoryCapacity = 36;
        var usedSlots = new HashSet<int>();
        int repaired = 0;

        for (int i = 0; i < items.Count; i++)
        {
            PlayerItemSaveData item = items[i];
            if (item.IsGrabbed || !string.IsNullOrEmpty(item.ContainerId))
                continue;

            bool slotIsValidAndFree = item.InventorySlotIndex >= 0 &&
                item.InventorySlotIndex < inventoryCapacity && usedSlots.Add(item.InventorySlotIndex);
            if (slotIsValidAndFree)
                continue;

            int freeSlot = -1;
            for (int slot = 0; slot < inventoryCapacity; slot++)
            {
                if (usedSlots.Add(slot))
                {
                    freeSlot = slot;
                    break;
                }
            }

            item.InventorySlotIndex = freeSlot;
            items[i] = item;
            repaired++;
        }

        if (repaired > 0)
            Multiplayer.LogWarning($"Repaired {repaired} invalid persisted inventory slots for {playerGuid}");
    }

    public ClientboundSaveGameDataPacket Clone()
    {
        return MemberwiseClone() as ClientboundSaveGameDataPacket;
    }
}
