using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(Junction), nameof(Junction.Awake))]
public static class Junction_Awake_Patch
{
    private static void Prefix(Junction __instance)
    {
        __instance.gameObject.GetOrAddComponent<NetworkedJunction>();
    }
}
