using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(RailTrack), nameof(RailTrack.Awake))]
public static class RailTrack_Awake_Patch
{
    private static void Prefix(NetworkedRailTrack __instance)
    {
        __instance.gameObject.GetOrAddComponent<NetworkedRailTrack>();
    }
}
