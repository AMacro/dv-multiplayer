using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(GadgetItem))]
public static class GadgetItemPatch
{
    [HarmonyPatch(nameof(GadgetItem.Awake))]
    [HarmonyPostfix]
    private static void Awake(GadgetItem __instance)
    {
        var networkedItem = __instance.gameObject.GetOrAddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        GadgetTrackedValueRegistry.Register(networkedItem, __instance.Gadget);
        networkedItem.FinaliseTrackedValues();
    }
}
