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

        // Put a returning player back where they left off, falling back to the default spawn
        bool hasPlacement = NetworkedSaveGameManager.Instance.Server_TryGetPlacement(data, player.Guid, out Vector3 placement, out float placementRotation);
        Multiplayer.Log($"Spawning {player.Username} at {(hasPlacement ? placement.ToString() : "the default spawn")}");

        Multiplayer.LogDebug(() =>
        {
            string unlockedGen = string.Join(", ", UnlockablesManager.Instance.UnlockedGeneralLicenses);
            string packetGen = string.Join(", ", data.GetStringArray(SaveGameKeys.Licenses_General));

            string unlockedJob = string.Join(", ", UnlockablesManager.Instance.UnlockedJobLicenses);
            string packetJob = string.Join(", ", data.GetStringArray(SaveGameKeys.Licenses_Jobs));

            return $"ClientboundSaveGameDataPacket.CreatePacket() UnlockedGen: {{{unlockedGen}}}, PacketGen: {{{packetGen}}},  UnlockedJob: {{{unlockedJob}}}, PacketJob: {{{packetJob}}}";
        });

        // The player's inventory: the latest report from their client this session,
        // or the one persisted in the save from a previous session. New players get an
        // empty list; StartingItemsController then provides the default starting items.
        PlayerItemSaveData[] playerItems = NetworkedSaveGameManager.Instance.Server_GetPlayerInventory(player, data);

        return new ClientboundSaveGameDataPacket
        {
            GameMode = data.GetString(SaveGameKeys.Game_mode),
            SerializedDifficulty = difficulty.ToString(Formatting.None),
            Money = StartingItemsController.Instance == null || !StartingItemsController.Instance.itemsLoaded ? data.GetFloat(SaveGameKeys.Player_money).GetValueOrDefault(0) : (float)Inventory.Instance.PlayerMoney,
            AcquiredGeneralLicenses = data.GetStringArray(SaveGameKeys.Licenses_General),
            AcquiredJobLicenses = data.GetStringArray(SaveGameKeys.Licenses_Jobs),
            UnlockedGarages = data.GetStringArray(SaveGameKeys.Garages),
            Position = hasPlacement ? placement : LevelInfo.DefaultSpawnPosition,
            Rotation = hasPlacement ? placementRotation : LevelInfo.DefaultSpawnRotation.y,
            HasDebt = data.GetFloat(SaveGameKeys.Debt_total).GetValueOrDefault(CareerManagerDebtController.Instance != null ? CareerManagerDebtController.Instance.NumberOfNonZeroPricedDebts : 0) > 0,
            // Debt_existing_locos = data.GetJObjectArray(SaveGameKeys.Debt_existing_locos)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_deleted_locos = data.GetJObjectArray(SaveGameKeys.Debt_deleted_locos)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_existing_jobs = data.GetJObjectArray(SaveGameKeys.Debt_existing_jobs)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_staged_jobs = data.GetJObjectArray(SaveGameKeys.Debt_staged_jobs)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_existing_jobless_cars = data.GetJObject(SaveGameKeys.Debt_existing_jobless_cars)?.ToString(),
            // Debt_deleted_jobless_cars = data.GetJObject(SaveGameKeys.Debt_deleted_jobless_cars)?.ToString(),
            // Debt_insurance = data.GetJObject(SaveGameKeys.Debt_insurance)?.ToString()

            JobManagerTime = JobsManager.Instance.Time,

            PlayerItems = playerItems
        };
    }

    public ClientboundSaveGameDataPacket Clone()
    {
        return MemberwiseClone() as ClientboundSaveGameDataPacket;
    }
}
