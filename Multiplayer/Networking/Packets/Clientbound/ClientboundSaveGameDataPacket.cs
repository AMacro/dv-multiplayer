using DV.InventorySystem;
using DV.JObjectExtstensions;
using DV.ServicePenalty;
using DV.UserManagement;
using Multiplayer.Components.Networking;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

    public static ClientboundSaveGameDataPacket CreatePacket(ServerPlayer player)
    {
        if (WorldStreamingInit.isLoaded)
            SaveGameManager.Instance.UpdateInternalData();

        SaveGameData data = SaveGameManager.Instance.data;

        JObject difficulty = new();
        DifficultyDataUtils.SetDifficultyToJSON(difficulty, NetworkLifecycle.Instance.Server.Difficulty);

        JObject playerData = NetworkedSaveGameManager.Instance.Server_GetPlayerData(data, player.Guid);

        return new ClientboundSaveGameDataPacket {
            GameMode = data.GetString(SaveGameKeys.Game_mode),
            SerializedDifficulty = difficulty.ToString(Formatting.None),
            Money = StartingItemsController.Instance == null || !StartingItemsController.Instance.itemsLoaded ? data.GetFloat(SaveGameKeys.Player_money).GetValueOrDefault(0) : (float)Inventory.Instance.PlayerMoney,
            AcquiredGeneralLicenses = data.GetStringArray(SaveGameKeys.Licenses_General),
            AcquiredJobLicenses = data.GetStringArray(SaveGameKeys.Licenses_Jobs),
            UnlockedGarages = data.GetStringArray(SaveGameKeys.Garages),
            Position = playerData?.GetVector3(SaveGameKeys.Player_position) ?? LevelInfo.DefaultSpawnPosition,
            Rotation = playerData?.GetFloat(SaveGameKeys.Player_rotation) ?? LevelInfo.DefaultSpawnRotation.y,
            HasDebt = data.GetFloat(SaveGameKeys.Debt_total).GetValueOrDefault(CareerManagerDebtController.Instance != null ? CareerManagerDebtController.Instance.NumberOfNonZeroPricedDebts : 0) > 0
            // Debt_existing_locos = data.GetJObjectArray(SaveGameKeys.Debt_existing_locos)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_deleted_locos = data.GetJObjectArray(SaveGameKeys.Debt_deleted_locos)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_existing_jobs = data.GetJObjectArray(SaveGameKeys.Debt_existing_jobs)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_staged_jobs = data.GetJObjectArray(SaveGameKeys.Debt_staged_jobs)?.NotNull().Select(j => j.ToString()).ToArray(),
            // Debt_existing_jobless_cars = data.GetJObject(SaveGameKeys.Debt_existing_jobless_cars)?.ToString(),
            // Debt_deleted_jobless_cars = data.GetJObject(SaveGameKeys.Debt_deleted_jobless_cars)?.ToString(),
            // Debt_insurance = data.GetJObject(SaveGameKeys.Debt_insurance)?.ToString()
        };
    }

    public ClientboundSaveGameDataPacket Clone()
    {
        return MemberwiseClone() as ClientboundSaveGameDataPacket;
    }
}
