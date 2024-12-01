using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Common;
using DV.UserManagement;
using DV.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Patches.SaveGame;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Multiplayer.Components.SaveGame;

public class StartGameData_ServerSave : AStartGameData
{
    private SaveGameData saveGameData;

    private ClientboundSaveGameDataPacket packet;

    public override bool IsStartingNewSession => false;

    public void SetFromPacket(ClientboundSaveGameDataPacket packet)
    {
        this.packet = packet.Clone();

        saveGameData = SaveGameManager.MakeEmptySave();
        saveGameData.SetString(SaveGameKeys.Game_mode, packet.GameMode);
        DifficultyToUse = DifficultyDataUtils.GetDifficultyFromJSON(JObject.Parse(packet.SerializedDifficulty), false);

        saveGameData.SetFloat(SaveGameKeys.Player_money, packet.Money);

        saveGameData.SetStringArray(SaveGameKeys.Licenses_Jobs, packet.AcquiredJobLicenses);
        saveGameData.SetStringArray(SaveGameKeys.Licenses_General, packet.AcquiredGeneralLicenses);
        saveGameData.SetStringArray(SaveGameKeys.Garages, packet.UnlockedGarages);

        saveGameData.SetBool(SaveGameKeys.Tutorial_01_completed, true);
        saveGameData.SetBool(SaveGameKeys.Tutorial_02_completed, true);
        saveGameData.SetBool(SaveGameKeys.Tutorial_03_completed, true);
        saveGameData.SetBool(SaveGameKeys.Derail_Popup_Shown, true);
        saveGameData.SetBool(SaveGameKeys.Damage_Popup_Shown, true);

        CareerManagerDebtControllerPatch.HasDebt = packet.HasDebt;

        Multiplayer.LogDebug(() =>
        {
            string unlockedGen = string.Join(", ", UnlockablesManager.Instance.UnlockedGeneralLicenses);
            string packetGen = string.Join(", ", packet.AcquiredGeneralLicenses);

            string unlockedJob = string.Join(", ", UnlockablesManager.Instance.UnlockedJobLicenses);
            string packetJob = string.Join(", ", packet.AcquiredJobLicenses);

            return $"StartGameData_ServerSave.SetFromPacket() UnlockedGen: {{{unlockedGen}}}, PacketGen: {{{packetGen}}},  UnlockedJob: {{{unlockedJob}}}, PacketJob: {{{packetJob}}}";
        });


        //For clients we need to have a session - new users may not have a session and this may also be causing problems with licenses syncing
        if (NetworkLifecycle.Instance.IsHost())
            return;

        Client_GameSession.SetCurrent(new Client_GameSession(packet.GameMode, DifficultyToUse));
    }

    public override void Initialize()
    {
        throw new InvalidOperationException($"Use {nameof(SetFromPacket)} instead!");
    }

    public override SaveGameData GetSaveGameData()
    {
        if (saveGameData == null)
            throw new InvalidOperationException($"{nameof(SetFromPacket)} must be called before {nameof(GetSaveGameData)}!");
        return saveGameData;
    }

    public override IEnumerator DoLoad(Transform playerContainer)
    {

        Transform playerTransform = playerContainer.transform;
        playerTransform.position = PlayerManager.IsPlayerPositionValid(packet.Position) ? packet.Position : LevelInfo.Instance.defaultSpawnPosition;
        playerTransform.eulerAngles = new Vector3(0, packet.Rotation, 0);

        LicenseManager.Instance.LoadData(saveGameData);

        if (saveGameData.GetString(SaveGameKeys.Game_mode) == "FreeRoam")
            LicenseManager.Instance.GrabAllGameModeSpecificUnlockables(SaveGameKeys.Game_mode);
        //else
        StartingItemsController.Instance.AddStartingItems(saveGameData, true);

        // if (packet.Debt_existing_locos != null)
        //     LocoDebtController.Instance.LoadExistingLocosDebtsSaveData(packet.Debt_existing_locos.Select(JObject.Parse).ToArray());
        // if (packet.Debt_deleted_locos != null)
        //     LocoDebtController.Instance.LoadDestroyedLocosDebtsSaveData(packet.Debt_deleted_locos.Select(JObject.Parse).ToArray());
        // if (packet.Debt_existing_jobs != null)
        //     LocoDebtController.Instance.LoadExistingLocosDebtsSaveData(packet.Debt_existing_jobs.Select(JObject.Parse).ToArray());
        // if (packet.Debt_staged_jobs != null)
        //     JobDebtController.Instance.LoadStagedJobsDebtsSaveData(packet.Debt_staged_jobs.Select(JObject.Parse).ToArray());
        // if (!string.IsNullOrEmpty(packet.Debt_existing_jobless_cars))
        //     JobDebtController.Instance.LoadExistingJoblessCarsDebtsSaveData(JObject.Parse(packet.Debt_existing_jobless_cars));
        // if (!string.IsNullOrEmpty(packet.Debt_deleted_jobless_cars))
        //     JobDebtController.Instance.LoadDeletedJoblessCarDebtsSaveData(JObject.Parse(packet.Debt_deleted_jobless_cars));
        // if (!string.IsNullOrEmpty(packet.Debt_insurance))
        //     CareerManagerDebtController.Instance.feeQuota.LoadSaveData(JObject.Parse(packet.Debt_insurance));

        carsAndJobsLoadingFinished = true;
        yield break;
    }

    public override string GetPostLoadMessage()
    {
        return null;
    }

    public override bool ShouldCreateSaveGameAfterLoad()
    {
        return false;
    }

    public override void MakeCurrent(){}
}

