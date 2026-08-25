using DV;
using DV.InventorySystem;
using DV.Localization;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.RPCs;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static Multiplayer.Networking.Data.RPCs.SpawnResponse;

namespace Multiplayer.Patches.CommsRadio;

[HarmonyPatch(typeof(CommsRadioCrewVehicle))]
public static class CommsRadioCrewVehiclePatch
{
    private readonly static Dictionary<CommsRadioCrewVehicle, bool> cooldown = new();

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCrewVehicle.Awake))]
    private static void Awake_Prefix(CommsRadioCrewVehicle __instance)
    {
        cooldown[__instance] = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCrewVehicle.OnDestroy))]
    private static void OnDestroy_Prefix(CommsRadioCrewVehicle __instance)
    {
        cooldown.Remove(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCrewVehicle.OnUse))]
    private static bool OnUse_Prefix(CommsRadioCrewVehicle __instance)
    {
        if (cooldown.TryGetValue(__instance, out var isOnCooldown) && isOnCooldown)
        {
            CommsRadioController.PlayAudioFromRadio(__instance.warningSound, __instance.transform);
            return false;
        }

        if (__instance.CurrentState != CommsRadioCrewVehicle.State.ConfirmSummon)
            return true;
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        if (Inventory.Instance.PlayerMoney < __instance.SummonPrice)
            return true;

        if (__instance.destinationTrack == null || !__instance.closestPointOnDestinationTrack.HasValue)
        {
            CallWorkCarFail(__instance, "unable to request car, destination track does not exist");
            return false;
        }

        if (!NetworkedRailTrack.TryGetNetId(__instance.destinationTrack, out var trackNetId) || trackNetId == 0)
        {
            CallWorkCarFail(__instance, $"NetworkedRailTrack not found for: {__instance.destinationTrack?.name}");
            return false;
        }

        if (__instance.selectedCar.livery == null)
        {
            CallWorkCarFail(__instance, "car prefab not found");
            return false;
        }

        var ticket = RpcManager.Instance
            .CreateTicket(Mathf.Max(NetworkLifecycle.Instance.Client.RPC_Timeout, 2f))
            .OnResolve
            (
                response =>
                {
                    if (response is SpawnResponse spawnResponse)
                    {
                        if (spawnResponse.Response == SpawnResponse.ResponseType.Success)
                        {
                            if (__instance.SummonPrice > 0 && __instance.moneyRemovedSound != null)
                                __instance.moneyRemovedSound.Play2D();

                            var locoPos = (Vector3)__instance.closestPointOnDestinationTrack.Value.position;
                            __instance.spawnVehicleSound.Play(locoPos, 1f, 1f, 0f, CommsRadioController.CAR_AUDIO_SOURCE_MIN_DISTANCE, 500f);
                            CommsRadioController.PlayAudioFromRadio(__instance.confirmSound, __instance.transform);

                            return;
                        }

                        string text = CommsRadioLocalization.WORK_TRAIN_SUMMON_PROMPT(LocalizationAPI.L(__instance.selectedCar.livery.localizationKey), __instance.SummonPrice);

                        string responseText;
                        switch (spawnResponse.Response)
                        {
                            case ResponseType.InsufficientPermissions:
                                responseText = Locale.PERMISSIONS_INSUFFICIENT;
                                break;
                            case ResponseType.InsufficientFunds:
                                responseText = CommsRadioLocalization.INSUFFICIENT_FUNDS;
                                break;
                            case ResponseType.InUse:
                                responseText = Locale.COMMS_RADIO_WORK_TRAIN_IN_USE;
                                if (spawnResponse.Reason != Networking.Data.Train.LocoInUseData.LocoInUseReason.Timeout)
                                    responseText += $"\n[{Locale.COMMS_RADIO_WORK_TRAIN_IN_USE_REASON(spawnResponse.Reason.ToString())}]";
                                else
                                {
                                    bool isMinutes = spawnResponse.Timeout > 60f;
                                    int timeOutFormatted = isMinutes ?
                                                            Mathf.RoundToInt(spawnResponse.Timeout / 60f) :
                                                            Mathf.RoundToInt(spawnResponse.Timeout);

                                    responseText += $"\n[{timeOutFormatted}{(isMinutes ? " min" : " sec")}]";
                                }
                                break;
                            default:
                                responseText = "";
                                break;
                        }

                        text += "\n" + responseText;

                        __instance.display.SetContent(text, FontStyles.UpperCase);
                        __instance.SetState(CommsRadioCrewVehicle.State.CancelSummon);
                        CommsRadioController.PlayAudioFromRadio(__instance.warningSound, __instance.transform);

                        CoroutineManager.Instance.StartCoroutine(CoolDownCoro(__instance));
                    }
                }
            )
            .OnTimeout
            (
                () =>
                {
                    CallWorkCarFail(__instance, "summon request timed out");
                }
            );

        NetworkLifecycle.Instance.Client.SendWorkTrainRequest
        (
            ticket.TicketId,
            __instance.selectedCar.livery.id,
            trackNetId,
            __instance.closestPointOnDestinationTrack.Value.index,
            __instance.spawnWithTrackDirection
        );

        return false;
    }

    private static void CallWorkCarFail(CommsRadioCrewVehicle instance, string message)
    {
        Multiplayer.LogWarning($"{nameof(CommsRadioCrewVehicle)} {message}");
        //Multiplayer.LogDebug(() => $"{nameof(CommsRadioCrewVehicle)} spawnCategory: {instance.category}, selectedLocoIndex: {instance.selectedLocoIndex}, selectedCarTypeIndex: {instance.selectedCarTypeIndex}, selectedLiveryIndex: {instance.selectedCarLiveryIndex}");

        CommsRadioController.PlayAudioFromRadio(instance.cancelSound, instance.transform);
        instance.ClearFlags();
    }

    private static IEnumerator CoolDownCoro(CommsRadioCrewVehicle instance)
    {
        cooldown[instance] = true;
        yield return new WaitForSeconds(2f);
        cooldown[instance] = false;
    }
}
