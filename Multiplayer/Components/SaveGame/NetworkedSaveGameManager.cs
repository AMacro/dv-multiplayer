using System;
using DV.InventorySystem;
using DV.JObjectExtstensions;
using DV.ThingTypes;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Listeners;
using Newtonsoft.Json.Linq;

namespace Multiplayer.Components.SaveGame;

public class NetworkedSaveGameManager : SingletonBehaviour<NetworkedSaveGameManager>
{
    private const string ROOT_KEY = "Multiplayer";
    private const string PLAYERS_KEY = "Players";

    protected override void Awake()
    {
        base.Awake();
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        Inventory.Instance.MoneyChanged += Server_OnMoneyChanged;
        LicenseManager.Instance.LicenseAcquired += Server_OnLicenseAcquired;
        LicenseManager.Instance.JobLicenseAcquired += Server_OnJobLicenseAcquired;
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
        JObject root = data.GetJObject(ROOT_KEY) ?? new JObject();
        JObject players = root.GetJObject(PLAYERS_KEY) ?? new JObject();

        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.Id == NetworkServer.SelfId || !player.IsLoaded)
                continue;
            JObject playerData = new();
            playerData.SetVector3(SaveGameKeys.Player_position, player.AbsoluteWorldPosition);
            playerData.SetFloat(SaveGameKeys.Player_rotation, player.WorldRotationY);
            players.SetJObject(player.Guid.ToString(), playerData);
        }

        root.SetJObject(PLAYERS_KEY, players);
        data.SetJObject(ROOT_KEY, root);
    }

    public JObject Server_GetPlayerData(SaveGameData data, Guid guid)
    {
        return data?.GetJObject(ROOT_KEY)?.GetJObject(PLAYERS_KEY)?.GetJObject(guid.ToString());
    }

    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedSaveGameManager)}]";
    }
}
