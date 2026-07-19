using DV;
using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Interaction;
using DV.Items;
using DV.Items.Brick;
using DV.TimeKeeping;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

internal static class ItemStateTracking
{
    public static NetworkedItem For(Component component)
    {
        NetworkedItem item = component.gameObject.GetOrAddComponent<NetworkedItem>();
        item.Initialize(component);
        return item;
    }
}

[HarmonyPatch(typeof(PageBook))]
internal static class PageBookPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(PageBook __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "page.current",
            () => __instance.currentPage,
            value =>
            {
                if (__instance.PagesGenerated)
                    __instance.ForceCurrentPage(value);
                else
                    __instance.currentPage = value;
            });
        item.FinaliseTrackedValues();
    }
}

[HarmonyPatch(typeof(LabelableItem))]
internal static class LabelableItemPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(LabelableItem __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "label.text",
            () => __instance.Text ?? string.Empty,
            __instance.UpdateText);
        item.FinaliseTrackedValues();
    }
}

internal static class MagazineAmmoTracking
{
    public static void Register(MagazineAmmo ammo)
    {
        NetworkedItem item = ItemStateTracking.For(ammo);
        item.RegisterTrackedValue(
            "ammo.spent",
            () => ammo.isSpent,
            value => ammo.isSpent = value);
        item.FinaliseTrackedValues();
    }
}

[HarmonyPatch(typeof(Cassette))]
internal static class CassettePatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(Cassette __instance)
    {
        MagazineAmmoTracking.Register(__instance);
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "cassette.track",
            () => __instance.lastPlayedPlaylistEntry,
            value => __instance.lastPlayedPlaylistEntry = value);
        item.FinaliseTrackedValues();
    }
}

[HarmonyPatch(typeof(PaintCan), "Start")]
internal static class PaintCanAmmoPatch
{
    [HarmonyPostfix]
    private static void Start(PaintCan __instance) => MagazineAmmoTracking.Register(__instance);
}

[HarmonyPatch(typeof(GadgetSolderingResource), "Start")]
internal static class SolderingResourceAmmoPatch
{
    [HarmonyPostfix]
    private static void Start(GadgetSolderingResource __instance) => MagazineAmmoTracking.Register(__instance);
}

[HarmonyPatch(typeof(AnalogAlarmClock))]
internal static class AnalogAlarmClockPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(AnalogAlarmClock __instance)
    {
        __instance.StartCoroutine(RegisterWhenReady(__instance));
    }

    private static IEnumerator RegisterWhenReady(AnalogAlarmClock __instance)
    {
        while (!__instance.initialized)
            yield return null;

        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "alarm.minutes",
            () => __instance.alarmTimeInMinutes,
            value => SetMinutes(__instance, value));
        item.RegisterTrackedValue(
            "alarm.armed",
            () => __instance.alarmSet,
            value => SetArmed(__instance, value));
        item.FinaliseTrackedValues();
    }

    private static void SetMinutes(AnalogAlarmClock clock, int value)
    {
        clock.alarmTimeInMinutes = value;
        clock.ClampAlarmTime();
        clock.SetAlarmTime();
    }

    private static void SetArmed(AnalogAlarmClock clock, bool value)
    {
        if (clock.alarmSet != value)
            clock.OnButtonPressed();
    }
}

[HarmonyPatch(typeof(BoomboxInteractionController))]
internal static class BoomboxPatch
{
    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    private static void Awake(BoomboxInteractionController __instance)
    {
        __instance.StartCoroutine(RegisterWhenReady(__instance));
    }

    private static IEnumerator RegisterWhenReady(BoomboxInteractionController __instance)
    {
        while (!__instance.initialized)
            yield return null;

        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue("boombox.power", () => __instance.IsPoweredOn, __instance.SetPower);
        item.RegisterTrackedValue("boombox.radioMode", () => __instance.IsInRadioMode, __instance.SetMode);
        item.RegisterTrackedValue(
            "boombox.volume",
            () => __instance.volumeAndInterferenceController.GetVolume(),
            __instance.SetVolume,
            (current, last) => Math.Abs(current - last) >= 0.01f);
        item.RegisterTrackedValue(
            "boombox.antenna",
            () => __instance.AntennaPosition,
            __instance.SetAntenna,
            (current, last) => Math.Abs(current - last) >= 0.01f);
        item.RegisterTrackedValue(
            "boombox.station",
            () => __instance.radioController.lastPlayedStationIndex,
            __instance.OverrideLastPlayedStationIndex);
        item.RegisterTrackedValue(
            "boombox.door",
            () => __instance.HasDoorOpen,
            value => SetDoor(__instance, value));
        item.RegisterTrackedValue(
            "boombox.track",
            () => __instance.cassetteController.CurrentPlaylistIndex,
            value =>
            {
                Cassette cassette = __instance.cassetteInteractionAreaScript.GetInsertedCassette();
                if (cassette == null)
                    return;

                cassette.lastPlayedPlaylistEntry = value;
                __instance.cassetteController.player?.SetSeekPosition(value, 0L);
            });
        item.RegisterTrackedValue(
            "boombox.playing",
            () => __instance.cassetteController.IsPlaying,
            value => SetPlaying(__instance, value));
        item.FinaliseTrackedValues();
    }

    private static void SetDoor(BoomboxInteractionController boombox, bool open)
    {
        if (open)
            boombox.cassetteInteractionAreaScript.OpenDoor();
        else
            boombox.CassetteDoorClose();
    }

    private static void SetPlaying(BoomboxInteractionController boombox, bool playing)
    {
        if (playing)
        {
            if (boombox.HasCassetteInserted)
                boombox.CassettePlay();
            else
                boombox.StartCoroutine(PlayWhenCassetteArrives(boombox));
        }
        else if (boombox.cassetteController.IsPlaying)
            boombox.CassettePause();
    }

    private static IEnumerator PlayWhenCassetteArrives(BoomboxInteractionController boombox)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames && !boombox.HasCassetteInserted; frame++)
            yield return null;

        if (boombox.HasCassetteInserted && !boombox.cassetteController.IsPlaying)
            boombox.CassettePlay();
    }
}

[HarmonyPatch(typeof(LocomotiveRemoteController))]
internal static class LocomotiveRemoteItemPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(LocomotiveRemoteController __instance)
    {
        __instance.StartCoroutine(RegisterWhenReady(__instance));
    }

    private static IEnumerator RegisterWhenReady(LocomotiveRemoteController remote)
    {
        while (!remote.initialized)
            yield return null;

        NetworkedItem item = ItemStateTracking.For(remote);
        item.RegisterTrackedValue(
            "locoRemote.power",
            () => remote.isOn,
            value =>
            {
                remote.isOn = value;
                remote.TogglePower(value, false);
            });
        item.RegisterTrackedValue(
            "locoRemote.battery",
            () => remote.battery.CurrentPower,
            value => SetBattery(remote.battery, value),
            (current, last) => Math.Abs(current - last) >= 1f);
        item.RegisterTrackedValue(
            "locoRemote.pairedLoco",
            () => remote.pairedLocomotive is Component ? remote.pairedLocomotive.GetLocoGuid() : string.Empty,
            value => SetPairedLocomotive(remote, value));
        item.RegisterTrackedValue(
            "locoRemote.coupler",
            () => remote.selectedCoupler,
            value =>
            {
                remote.selectedCoupler = value;
                remote.UpdateCouplerSelection(0);
            });
        item.FinaliseTrackedValues();
    }

    private static void SetBattery(Battery battery, float power)
    {
        float difference = power - battery.CurrentPower;
        if (difference > 0f)
            battery.Charge(difference);
        else if (difference < 0f)
            battery.Drain(-difference);
    }

    private static void SetPairedLocomotive(LocomotiveRemoteController remote, string locoGuid)
    {
        string currentGuid = remote.pairedLocomotive is Component
            ? remote.pairedLocomotive.GetLocoGuid()
            : string.Empty;
        if (currentGuid == locoGuid)
            return;

        if (string.IsNullOrEmpty(locoGuid))
        {
            remote.Unpair();
            return;
        }

        TrainCar car = SingletonBehaviour<TrainCarRegistry>.Instance.GetTrainCarByCarGuid(locoGuid);
        if (car != null)
            remote.Pair(car);
        else
            remote.StartCoroutine(RetryPairing(remote, locoGuid));
    }

    private static IEnumerator RetryPairing(LocomotiveRemoteController remote, string locoGuid)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            TrainCar car = SingletonBehaviour<TrainCarRegistry>.Instance.GetTrainCarByCarGuid(locoGuid);
            if (car == null)
                continue;

            remote.Pair(car);
            yield break;
        }

        Multiplayer.LogWarning($"Timed out resolving paired locomotive '{locoGuid}' for {remote.name}.");
    }
}

[HarmonyPatch(typeof(ProximitySensor))]
internal static class ProximitySensorPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(ProximitySensor __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "proximity.channel",
            () => __instance.rotaryChannel.Value,
            value => __instance.rotaryChannel.SetValue(value, ControlImplBase.SetValueSource.Default));
        item.RegisterTrackedValue(
            "proximity.range",
            () => __instance.rotaryRange.Value,
            value => __instance.rotaryRange.SetValue(value, ControlImplBase.SetValueSource.Default));
        item.FinaliseTrackedValues();
    }
}

[HarmonyPatch(typeof(CommsRadioController))]
internal static class CommsRadioItemPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(CommsRadioController __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "comms.mode",
            () => __instance.activeModeIndex,
            value => SetMode(__instance, value));
        item.FinaliseTrackedValues();
    }

    private static void SetMode(CommsRadioController radio, int index)
    {
        if (radio.allModes == null || index < 0 || index >= radio.allModes.Count)
            return;

        radio.activeModeIndex = index;
        radio.SetMode(radio.allModes[index]);
    }
}

internal sealed class BrickSyncState : MonoBehaviour
{
    public int Sequence;
    public int LastAction;
    public bool Applying;
}

[HarmonyPatch(typeof(BrickConsole))]
internal static class BrickConsolePatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(BrickConsole __instance)
    {
        BrickSyncState state = __instance.gameObject.GetOrAddComponent<BrickSyncState>();
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "brick.action",
            () => state.LastAction,
            value => state.LastAction = value);
        item.RegisterTrackedValue(
            "brick.sequence",
            () => state.Sequence,
            value => ApplyAction(__instance, state, value));
        item.FinaliseTrackedValues();
    }

    [HarmonyPatch(nameof(BrickConsole.ExecuteInputAction))]
    [HarmonyPrefix]
    private static void ExecuteInputAction(BrickConsole __instance, BrickInput.BrickInputAction brickInputAction)
    {
        BrickSyncState state = __instance.GetComponent<BrickSyncState>();
        if (state == null || state.Applying)
            return;

        state.LastAction = (int)brickInputAction;
        state.Sequence++;
    }

    private static void ApplyAction(BrickConsole console, BrickSyncState state, int sequence)
    {
        if (sequence <= state.Sequence)
            return;

        state.Applying = true;
        try
        {
            console.ExecuteInputAction((BrickInput.BrickInputAction)state.LastAction);
            state.Sequence = sequence;
        }
        finally
        {
            state.Applying = false;
        }
    }
}
