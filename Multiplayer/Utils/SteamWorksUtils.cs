using DV.Localization;
using DV.UIFramework;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using Multiplayer.Patches.MainMenu;
using Steamworks;
using Steamworks.Data;
using System;
using System.Linq;

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
        catch(Exception ex)
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
            lobby.SetData(prop.Name, value);
        }
    }

    public static LobbyServerData GetLobbyData(this Lobby lobby)
    {
        var data = new LobbyServerData();
        var properties = typeof(LobbyServerData).GetProperties();

        foreach (var prop in properties)
        {
            var value = lobby.GetData(prop.Name);
            if (string.IsNullOrEmpty(value)) continue;

            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(data, converted);
        }

        return data;
    }

    public static ulong GetLobbyIdFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "+connect_lobby")
            {
                return ulong.Parse(args[i + 1]);
            }
        }

        return 0;
    }

    public static void JoinFromCommandLine()
    {
        if (hasJoinedCL)
            return;
        hasJoinedCL = true;

        SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChanged;

        var id = GetLobbyIdFromArgs();
        var sId = new SteamId
        {
            Value = id
        };

        var lobby = new Lobby(sId);
        var ret = lobby.Refresh();
    }

    private static void OnLobbyDataChanged(Lobby lobby)
    {
        SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChanged;

        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            Multiplayer.Log($"OnLobbyDataChanged({lobby.Id}) count: {lobby.Data?.Count()}");

            foreach (var item in lobby.Data)
            {
                Multiplayer.Log($"OnEnablePost Data: {item.Key}, Value: {item.Value}"); 
            }

            ServerBrowserPane.lobbyToJoin = lobby;
            MainMenuThingsAndStuff.Instance.SwitchToMenu((byte)RightPaneController_Patch.joinMenuIndex);
        });
    }

    public static void OnLobbyInviteRequest(Friend friend, Lobby lobby)
    {
        Multiplayer.Log($"Received lobby invite: {lobby.Id}");

        if (NetworkLifecycle.Instance.IsServerRunning ||  NetworkLifecycle.Instance.IsClientRunning)
            return;

        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            var popup = MainMenuThingsAndStuff.Instance.ShowYesNoPopup();

            if (popup == null)
            {
                Multiplayer.LogError("OnLobbyInviteRequest() Popup not found.");
                return;
            }

            popup.labelTMPro.text = $"{friend.Name} invited you to play!\r\nDo you wish to join?";

            Localize locPos = popup.positiveButton.GetComponentInChildren<Localize>();
            locPos.key = "yes";
            locPos.UpdateLocalization();

            Localize locNeg = popup.negativeButton.GetComponentInChildren<Localize>();
            locNeg.key = "no";
            locNeg.UpdateLocalization();

            popup.Closed += (PopupResult result) =>
            {
                Multiplayer.LogDebug(()=>$"OnLobbyInviteRequest() Popup closed by {result.closedBy}");
                if (result.closedBy == PopupClosedByAction.Positive)
                {
                    MainMenuThingsAndStuff.Instance.SwitchToMenu((byte)RightPaneController_Patch.joinMenuIndex);
                    ServerBrowserPane.lobbyToJoin = lobby;
                }
            }; 

        });

        NetworkLifecycle.Instance.TriggerMainMenuEventLater();
    }

}
