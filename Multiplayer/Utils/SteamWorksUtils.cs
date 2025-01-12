using Steamworks;
using System;

namespace Multiplayer.Utils;

public static class SteamWorksUtils
{
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
}
