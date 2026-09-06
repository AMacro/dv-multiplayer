using DV;
using DV.Localization;
using DV.Platform.Steam;
using DV.UIFramework;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using Multiplayer.Patches.MainMenu;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Multiplayer.Utils;

public static class SteamworksUtils
{
    public const string LOBBY_MP_MOD_KEY = "MP_MOD";
    public const string LOBBY_NET_LOCATION_KEY = "NetLocation";
    public const string LOBBY_HAS_PASSWORD = "HasPassword";

    private static bool hasJoinedCL;

    public static bool GetSteamUser(out string username, out ulong steamId)
    {
        username = null;
        steamId = 0;

        try
        {
            if (!DVSteamworks.Success)
                return false;

            if (!SteamClient.IsValid || !SteamClient.SteamId.IsValid)
            {
                Multiplayer.Log($"Failed to get SteamID. Status: {SteamClient.IsValid}, {SteamClient.SteamId.IsValid}");
                return false;
            }

            steamId = SteamClient.SteamId.Value;
            username = SteamClient.Name;

            if (SteamApps.IsAppInstalled(DVSteamworks.APP_ID))
                Multiplayer.Log($"Found Steam Name: {username}, steamId {steamId}");
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"Failed to obtain Steam user.\r\n{ex.StackTrace}");
        }

        return true;
    }

    public static void SetLobbyData(Lobby lobby, LobbyServerData data, string[] exclude)
    {
        var properties = typeof(LobbyServerData).GetProperties().Where(p => !exclude.Contains(p.Name));
        foreach (var prop in properties)
        {
            var value = prop.GetValue(data)?.ToString() ?? "";
            if (prop.Name == nameof(LobbyServerData.RequiredMods))
            {
                try
                {
                    value = Newtonsoft.Json.JsonConvert.SerializeObject((ModInfo[])prop.GetValue(data));
                }
                catch (Exception ex)
                {
                    Multiplayer.LogException($"SetLobbyData() Error serializing RequiredMods property", ex);
                }

                Multiplayer.LogDebug(() => $"SetLobbyData() Setting property: {prop.Name}, value: {value}");
            }
            lobby.SetData(prop.Name, value);
        }
    }

    public static LobbyServerData GetLobbyData(this Lobby lobby)
    {
        var data = new LobbyServerData();
        var properties = typeof(LobbyServerData).GetProperties();
        string value = null;

        foreach (var prop in properties)
        {
            try
            {
                value = lobby.GetData(prop.Name);
                if (string.IsNullOrEmpty(value)) continue;

                Multiplayer.LogDebug(() => $"GetLobbyData() Retrieving property: {prop.Name}, value: {value}");

                // Backward compatibility for non-JSON strings
                if (prop.Name == nameof(LobbyServerData.RequiredMods))
                {
                    var mods = ModInfo.DeserializeRequiredMods(value);

                    prop.SetValue(data, mods);
                    continue;
                }

                if (prop.PropertyType.IsEnum)
                {
                    var enumValue = Enum.Parse(prop.PropertyType, value);
                    prop.SetValue(data, enumValue);
                }
                else
                {
                    var converted = Convert.ChangeType(value, prop.PropertyType);
                    prop.SetValue(data, converted);
                }

                value = null;
            }
            catch (Exception ex)
            {
                Multiplayer.LogException($"GetLobbyData() Error parsing property: {prop?.Name}, value: {value}", ex);
            }
        }

        return data;
    }

    public static ulong GetLobbyIdFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "+connect_lobby")
                return ulong.Parse(args[i + 1]);

        return 0;
    }

    public static IEnumerator JoinFromCommandLine()
    {
        float time = Time.time;

        Multiplayer.LogDebug(() => $"JoinFromCommandLine() {DVSteamworks.Success}");

        if (hasJoinedCL || BuildInfo.BUILD_DESTINATION != "steam")
            yield break;

        hasJoinedCL = true;

        //allow steamworks to initialise
        yield return new WaitUntil(() => { return DVSteamworks.Success || (Time.deltaTime - time) > 5; });

        if (!DVSteamworks.Success)
            yield break;

        var id = GetLobbyIdFromArgs();
        var sId = new SteamId
        {
            Value = id
        };

        var lobby = new Lobby(sId);
        lobby.Refresh();

        QueueLobbyInvite(lobby);
    }

    private static bool CanHandleLobbyRequest()
    {
        return !NetworkLifecycle.Instance.IsServerRunning &&
               !NetworkLifecycle.Instance.IsClientRunning;
    }

    public static async void OnLobbyJoinRequest(Lobby lobby, SteamId id)
    {
        Multiplayer.LogDebug(() => $"OnLobbyJoinRequest() {lobby.Id} from {id.Value}");

        if (!CanHandleLobbyRequest())
            return;

        try
        {
            await RefreshAsync(lobby);
        }
        catch (Exception ex)
        {
            Multiplayer.LogException($"Lobby join request failed to refresh lobby {lobby.Id}", ex);
            return;
        }

        if (!IsDVMP(lobby))
        {
            Multiplayer.LogDebug(() => $"Lobby join request failed, lobby {lobby.Id} is not a DVMP lobby");
            return;
        }

        QueueLobbyInvite(lobby);
    }

    public static async void OnLobbyInviteRequest(Friend friend, Lobby lobby)
    {
        Multiplayer.LogDebug(() => $"OnLobbyInviteRequest() {lobby.Id} from {friend.Name} ({friend.Id.Value})");

        if (!CanHandleLobbyRequest())
            return;

        try
        {
            await RefreshAsync(lobby);
        }
        catch (Exception ex)
        {
            Multiplayer.LogException($"Lobby invite failed to refresh lobby {lobby.Id}", ex);
            return;
        }

        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            var popup = MainMenuThingsAndStuff.Instance.ShowYesNoPopup();

            if (popup == null)
                return;

            popup.labelTMPro.text = $"{friend.Name} invited you to play!\r\nDo you wish to join?";

            Localize locPos = popup.positiveButton.GetComponentInChildren<Localize>();
            locPos.key = "yes";
            locPos.UpdateLocalization();

            Localize locNeg = popup.negativeButton.GetComponentInChildren<Localize>();
            locNeg.key = "no";
            locNeg.UpdateLocalization();

            popup.Closed += (PopupResult result) =>
            {
                Multiplayer.LogDebug(() => $"Agreed to join: {result.closedBy}");
                if (result.closedBy == PopupClosedByAction.Positive)
                    QueueLobbyInvite(lobby);
            };

        });

        NetworkLifecycle.Instance.TriggerMainMenuEventLater();
    }

    public static void QueueLobbyInvite(Lobby lobby)
    {
        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            ServerBrowserPane.lobbyToJoin = lobby;
            MainMenuThingsAndStuff.Instance.SwitchToMenu((byte)RightPaneController_Patch.joinMenuIndex);
        });

        NetworkLifecycle.Instance.TriggerMainMenuEventLater();
    }

    public static async Task RefreshAsync(this Lobby lobby)
    {
        TaskCompletionSource<bool> resultWaiter = new();
        Action<Lobby> eventHandler = (Lobby queriedLobby) =>
        {
            if (lobby.Id != queriedLobby.Id) return;
            resultWaiter.SetResult(true);
        };

        SteamMatchmaking.OnLobbyDataChanged += eventHandler;
        lobby.Refresh();
        var result = await resultWaiter.Task;
        SteamMatchmaking.OnLobbyDataChanged -= eventHandler;
    }

    public static bool IsDVMP(Lobby lobby)
    {
        var gameCheck = lobby.GetData(LOBBY_MP_MOD_KEY);
        return gameCheck == LOBBY_MP_MOD_KEY;
    }
}
