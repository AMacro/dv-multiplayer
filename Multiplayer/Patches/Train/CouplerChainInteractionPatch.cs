using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data.Train;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(ChainCouplerInteraction))]
public static class ChainCouplerInteractionPatch
{
    [HarmonyPatch(nameof(ChainCouplerInteraction.OnScrewButtonUsed))]
    [HarmonyPostfix]
    private static void OnScrewButtonUsed(ChainCouplerInteraction __instance)
    {

        Multiplayer.LogDebug(() => $"OnScrewButtonUsed({__instance?.couplerAdapter?.coupler?.train?.ID}) state: {__instance.state}");

        CouplerInteractionType flag = CouplerInteractionType.Start;
        if (__instance.state == ChainCouplerInteraction.State.Attached_Tightening_Couple || __instance.state == ChainCouplerInteraction.State.Attached_Tight)
            flag |= CouplerInteractionType.CouplerTighten;
        else if (__instance.state == ChainCouplerInteraction.State.Attached_Loosening_Uncouple || __instance.state == ChainCouplerInteraction.State.Attached_Loose)
            flag |= CouplerInteractionType.CouplerLoosen;
        else
            Multiplayer.LogDebug(() =>
            {
                TrainCar car = __instance?.couplerAdapter?.coupler?.train;
                return $"OnScrewButtonUsed({car?.ID})\r\n{new System.Diagnostics.StackTrace()}";
            });

        if (flag != CouplerInteractionType.NoAction)
            NetworkLifecycle.Instance.Client.SendCouplerInteraction(flag, __instance?.couplerAdapter?.coupler);
    }

}
