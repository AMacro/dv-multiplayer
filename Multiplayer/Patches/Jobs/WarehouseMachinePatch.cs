using DV.Logic.Job;
using HarmonyLib;
using Multiplayer.Components.Networking.Jobs;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(WarehouseMachine))]
public class WarehouseMachinePatch
{
    [HarmonyPatch(nameof(WarehouseMachine.ID))]
    [HarmonyPatch(MethodType.Setter)]
    [HarmonyPostfix]
    public static void ID_Set(WarehouseMachine __instance)
    {
        WarehouseMachineLookup.Instance.RegisterWarehouseMachine( __instance );
    }
}
