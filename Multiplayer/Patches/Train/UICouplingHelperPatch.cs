using DV.HUD;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data.Train;
using Newtonsoft.Json.Linq;

namespace Multiplayer.Patches.Train;


[HarmonyPatch(typeof(UICouplingHelper))]
public static class UICouplingHelperPatch
{
    [HarmonyPatch(nameof(UICouplingHelper.HandleCoupling))]
    [HarmonyPrefix]
    private static void BeforeHandleCoupling(Coupler coupler, out bool __state)
    {
        __state = coupler != null && coupler.IsCoupled();
    }

    [HarmonyPatch(nameof(UICouplingHelper.HandleCoupling))]
    [HarmonyPostfix]
    private static void HandleCoupling(UICouplingHelper __instance, Coupler coupler, bool advanced, bool __state)
    {
        Multiplayer.LogDebug(() => $"UICouplingHelper.HandleCoupling({coupler?.train?.ID}, {advanced})");

        if (coupler == null || UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        // Native UI has already applied the action, including its chain visuals.
        // Do not restart ChainScript: that can reset the FSM/drag anchors and
        // emit additional Park/Couple events while its transition is running.
        // A failed/no-op UI action must not be broadcast as an uncouple.
        if (__state == coupler.IsCoupled())
        {
            Multiplayer.LogDebug(() => $"UI coupling made no topology change for {coupler.train?.ID}; no network action sent.");
            return;
        }

        Coupler otherCoupler = null;
        CouplerInteractionType interaction = CouplerInteractionType.NoAction;

        if (coupler.IsCoupled())
        {
            interaction |= CouplerInteractionType.CoupleViaUI;
            otherCoupler = coupler.coupledTo;

            if(advanced)
            {
                interaction |= CouplerInteractionType.HoseConnect | CouplerInteractionType.CockOpen;
            }

            Multiplayer.LogDebug(() => $"UICouplingHelper.HandleCoupling({coupler?.train?.ID}, {advanced}) coupler is front: {coupler?.isFrontCoupler}, otherCoupler: {otherCoupler?.train?.ID}, otherCoupler is front: {otherCoupler?.isFrontCoupler}, action: {interaction}");

            if (otherCoupler == null)
                return;

        }
        else
        {
            interaction |= CouplerInteractionType.UncoupleViaUI;

            if (advanced)
            {
                interaction |= CouplerInteractionType.HoseDisconnect | CouplerInteractionType.CockClose;
            }

        }

        NetworkLifecycle.Instance.Client.SendCouplerInteraction(interaction, coupler, otherCoupler);
    }
}
