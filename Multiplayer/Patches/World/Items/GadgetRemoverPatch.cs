using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(GadgetRemover))]
public static class GadgetRemoverPatch
{
    [HarmonyPatch(nameof(GadgetRemover.Remove))]
    [HarmonyPrefix]
    private static void RemoveGadget(GadgetRemover __instance, GadgetBase target)
    {
        target.TryGetComponent<NetworkedItem>(out var netItem);
        netItem?.OnRemove();
        
    }
}
