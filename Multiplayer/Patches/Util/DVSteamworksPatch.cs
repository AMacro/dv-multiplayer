using HarmonyLib;
using Steamworks;


namespace Multiplayer.Patches.Util;

[HarmonyPatch(typeof(DVSteamworks))]
public static class DVSteamworksPatch
{
    [HarmonyPatch(nameof(DVSteamworks.Awake))]
    [HarmonyPostfix]
    public static void Awake()
    {
        if (DVSteamworks.Success)
            SteamNetworkingUtils.InitRelayNetworkAccess();
    }
}
