using DV.HUD;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data.Train;
using System;


namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CouplerInterfacer))]
public static class CouplerInterfacerPatch
{
    static Action<float> frontCouplerDelegate;
    static Action<float> rearCouplerDelegate;


    [HarmonyPatch(nameof(CouplerInterfacer.SetupListeners))]
    [HarmonyPrefix]
    private static void SetupListeners(CouplerInterfacer __instance, bool on)
    {
        Multiplayer.LogDebug(() => $"CouplerInterfacer.SetupListeners({__instance?.train?.ID}, {on})");
        if (on)
        {
            if(frontCouplerDelegate != null)
            {
                Multiplayer.LogDebug(() => $"CouplerInterfacer.SetupListeners({__instance?.train?.ID}, {on}) not null!");
                return;
            }

            frontCouplerDelegate += (float value)=>SendCouple(__instance, value, true);
            rearCouplerDelegate += (float value)=>SendCouple(__instance, value, false);

            __instance.manager.CouplerMenu.coupleF.controlModule.ValueChanged += frontCouplerDelegate;
            __instance.manager.CouplerMenu.chainF.controlModule.ValueChanged += frontCouplerDelegate;

            __instance.manager.CouplerMenu.coupleR.controlModule.ValueChanged += rearCouplerDelegate;
            __instance.manager.CouplerMenu.chainR.controlModule.ValueChanged += rearCouplerDelegate;
        }
        else
        {
            if (frontCouplerDelegate != null)
            {
                __instance.manager.CouplerMenu.coupleF.controlModule.ValueChanged -= frontCouplerDelegate;
                __instance.manager.CouplerMenu.chainF.controlModule.ValueChanged -= frontCouplerDelegate;

                frontCouplerDelegate = null;
            }

            if (rearCouplerDelegate != null)
            {
                __instance.manager.CouplerMenu.coupleR.controlModule.ValueChanged -= rearCouplerDelegate;
                __instance.manager.CouplerMenu.chainR.controlModule.ValueChanged -= rearCouplerDelegate;

                rearCouplerDelegate = null;
            }
        }
    }

    private static void SendCouple(CouplerInterfacer couplerInterfacer, float value, bool front)
    {
        Multiplayer.LogDebug(() => $"CouplerInterfacer.SendCouple({couplerInterfacer?.train?.ID}, {value}, {front})");

        if (value <= 0.5f)
            return;

        Coupler coupler = couplerInterfacer.GetCoupler(front);
        Coupler otherCoupler = null;
        CouplerInteractionType interaction = CouplerInteractionType.UncoupleViaUI;

        Multiplayer.LogDebug(() => $"CouplerInterfacer.SendCouple({couplerInterfacer?.train?.ID}, {value}, {front}) coupler: {coupler?.train?.ID}, action: {interaction}");

        if (coupler == null)
            return;

        if (!coupler.IsCoupled())
        {
            interaction = CouplerInteractionType.CoupleViaUI;
            otherCoupler = coupler.GetFirstCouplerInRange();

            Multiplayer.LogDebug(() => $"CouplerInterfacer.SendCouple({couplerInterfacer?.train?.ID}, {value}, {front}) coupler: {coupler?.train?.ID}, coupler is front: {coupler?.isFrontCoupler}, otherCoupler: {otherCoupler?.train?.ID}, otherCoupler is front: {otherCoupler?.isFrontCoupler}, action: {interaction}");
            if (otherCoupler == null)
                return;
        }

        NetworkLifecycle.Instance.Client.SendCouplerInteraction(interaction, coupler, otherCoupler);
    }
}
