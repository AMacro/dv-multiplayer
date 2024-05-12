using HarmonyLib;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(StationController), nameof(StationController.Awake))]
public static class StationController_Awake_Patch
{
    public static void Postfix(StationController __instance)
    {
        __instance.gameObject.AddComponent<NetworkedStation>();
    }
}
