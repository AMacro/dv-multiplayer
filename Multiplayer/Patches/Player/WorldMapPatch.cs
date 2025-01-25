using HarmonyLib;
using Multiplayer.Components.Networking.Player;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(WorldMap), nameof(WorldMap.Awake))]
public static class WorldMap_Awake_Patch
{
    private static void Postfix(WorldMap __instance)
    {
        __instance.gameObject.AddComponent<NetworkedWorldMap>();
    }
}
