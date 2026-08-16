using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static class GadgetTrackedValueRegistry
{
    public static void Register(NetworkedItem item, GadgetBase gadget)
    {
        if (gadget == null)
            return;

        item.RegisterTrackedValue("gadget.onGlass", () => gadget.IsOnGlass, value => gadget.IsOnGlass = value);
        RegisterSoldering(item, gadget);
        RegisterDrillable(item, gadget);
        RegisterText(item, gadget);
        RegisterConcreteGadget(item, gadget);
    }

    private static void RegisterSoldering(NetworkedItem item, GadgetBase gadget)
    {
        if (!gadget.IsSolderable)
            return;

        item.RegisterTrackedValue(
            "solder.units",
            () => gadget.SolderingProgressUnits,
            units => ApplySolderingUnits(gadget, units));
        item.RegisterTrackedValue(
            "solder.complete",
            () => gadget.IsSoldered,
            complete => ApplySolderingCompletion(gadget, complete));
    }

    private static void ApplySolderingUnits(GadgetBase gadget, int units)
    {
        int target = Clamp(units, 0, 65536);
        int delta = target - gadget.SolderingProgressUnits;
        if (delta > 0)
            gadget.MakeSoldered(delta);
        else if (delta < 0)
            gadget.RemoveSoldering(-delta);
    }

    private static void ApplySolderingCompletion(GadgetBase gadget, bool complete)
    {
        if (complete && !gadget.IsSoldered)
            gadget.MakeSoldered();
        else if (!complete && gadget.IsSoldered)
            gadget.RemoveSoldering();
    }

    private static void RegisterDrillable(NetworkedItem item, GadgetBase gadget)
    {
        var drillable = gadget.GetComponent<Drillable>();
        if (drillable == null)
            return;

        for (int pointIndex = 0; pointIndex < drillable.MountPointCount; pointIndex++)
            RegisterMountPoint(item, drillable, pointIndex);
    }

    private static void RegisterMountPoint(
        NetworkedItem item,
        Drillable drillable,
        int pointIndex)
    {
        string keyPrefix = $"drill.{pointIndex}";
        item.RegisterTrackedValue(
            $"{keyPrefix}.state",
            () => (int)drillable.GetMountPointState(pointIndex),
            state => drillable.SetMountPointState(pointIndex, (MountPoint.States)state));
        item.RegisterTrackedValue(
            $"{keyPrefix}.glass",
            () => drillable.GetMountPoint(pointIndex).IsOnGlass,
            isOnGlass => drillable.GetMountPoint(pointIndex).IsOnGlass = isOnGlass);
    }

    private static void RegisterText(NetworkedItem item, GadgetBase gadget)
    {
        var text = gadget.GetComponent<TextGadget>();
        if (text == null)
            return;

        item.RegisterTrackedValue(
            "text.value",
            () => text.textMesh.text,
            value => SetText(text, value));
    }

    private static void SetText(TextGadget text, string value)
    {
        text.textMesh.text = value ?? string.Empty;
        text.UpdateItemText();
    }

    private static void RegisterConcreteGadget(NetworkedItem item, GadgetBase gadget)
    {
        if (gadget is GadgetProximityScreen proximity)
        {
            item.RegisterTrackedValue("proximity.channel", () => proximity.CurrentChannel,
                value => SetAndRefresh(proximity, () => proximity.CurrentChannel = value));
            item.RegisterTrackedValue("proximity.mode", () => proximity.CurrentMode,
                value => SetAndRefresh(proximity, () => proximity.CurrentMode = value));
            return;
        }

        if (gadget is GadgetRoadrunner roadrunner)
        {
            item.RegisterTrackedValue(
                "roadrunner.target",
                () => roadrunner.LengthMeters,
                value => roadrunner.LengthMeters = Clamp(value, 0, roadrunner.MaxLength));
            item.RegisterTrackedValue("roadrunner.countup", () => roadrunner.Countup, value => roadrunner.countup = value);
            return;
        }

        if (gadget is AlternatingController alternating)
        {
            item.RegisterTrackedValue(
                "alternating.interval",
                () => alternating.SelectedInterval,
                value => SetAndRefresh(alternating, () => alternating.SelectedInterval = value));
            item.RegisterTrackedValue(
                "alternating.state",
                () => alternating.alternatorState,
                value => alternating.alternatorState = value);
            // The timer changes every frame. It belongs in full/create snapshots for
            // late observers, but state transitions provide incremental phase updates.
            item.RegisterTrackedValue(
                "alternating.timer",
                () => alternating.timer,
                value => alternating.timer = value,
                thresholdComparer: (_, _) => false);
            return;
        }

        if (gadget is GadgetSwitch gadgetSwitch)
        {
            item.RegisterTrackedValue("switch.output", () => gadgetSwitch.RawOutputValue,
                value => SetAndRefresh(gadgetSwitch, () => gadgetSwitch.SetOutputValue(value)));
            return;
        }

        if (gadget is GadgetATS ats)
        {
            item.RegisterTrackedValue("ats.regime", () => ats.Regime,
                value => SetAndRefresh(ats, () => ats.SetRegime(value)));
            return;
        }

        if (gadget is GadgetSwitchSetter switchSetter)
        {
            item.RegisterTrackedValue("switchSetter.mode", () => switchSetter.Mode,
                value => SetAndRefresh(switchSetter, () => switchSetter.Mode = value));
            item.RegisterTrackedValue(
                "switchSetter.sideCorrection",
                () => switchSetter.SideCorrectRegime,
                value => SetAndRefresh(switchSetter, () => switchSetter.SideCorrectRegime = value));
            item.RegisterTrackedValue(
                "switchSetter.direction",
                () => switchSetter.DirectionMode,
                value => SetAndRefresh(switchSetter, () => switchSetter.DirectionMode = value));
            return;
        }

        if (gadget is GadgetOverheatProtection overheat)
        {
            item.RegisterTrackedValue("overheat.mode", () => overheat.ModeIndex,
                value => SetAndRefresh(overheat, () => overheat.ModeIndex = value));
            item.RegisterTrackedValue("overheat.cutEngine", () => overheat.cutEngine,
                value => SetAndRefresh(overheat, () => overheat.cutEngine = value));
            return;
        }

        if (gadget is GadgetDPU dpu)
        {
            item.RegisterTrackedValue("dpu.regime", () => (int)dpu.Regime,
                value => SetAndRefresh(dpu, () => dpu.Regime = (GadgetDPU.WirelessMode)value));
            item.RegisterTrackedValue("dpu.reverse", () => dpu.ReverseOrientation,
                value => SetAndRefresh(dpu, () => dpu.ReverseOrientation = value));
            item.RegisterTrackedValue("dpu.channel", () => dpu.Channel,
                value => SetAndRefresh(dpu, () => dpu.Channel = value));
            return;
        }

        if (gadget is GadgetControlPanel controlPanel)
        {
            item.RegisterTrackedValue("controlPanel.tilt", () => controlPanel.tilt,
                value => SetAndRefresh(controlPanel, () => controlPanel.tilt = value));
            return;
        }

        if (gadget is GadgetAmpLimiter amp)
        {
            item.RegisterTrackedValue("ampLimiter.mode", () => amp.ModeIndex,
                value => SetAndRefresh(amp, () => amp.ModeIndex = value));
            return;
        }
    }

    private static void SetAndRefresh(GadgetBase gadget, Action setter)
    {
        setter();
        RefreshLoadedLods(gadget);
    }

    internal static void ApplyAlternatingPhase(
        AlternatingController alternating,
        Dictionary<string, object> values,
        uint sentTick)
    {
        if (alternating == null || values == null ||
            !values.TryGetValue("alternating.state", out object stateValue) || stateValue is not int state)
            return;

        float interval = alternating.intervals[alternating.SelectedInterval];
        float timer = values.TryGetValue("alternating.timer", out object timerValue) && timerValue is float sentTimer
            ? sentTimer
            : 0f;

        if (!alternating.PowerState || interval <= 0f)
        {
            alternating.alternatorState = 0;
            alternating.timer = 0f;
        }
        else if (float.IsInfinity(interval))
        {
            alternating.alternatorState = state;
            alternating.timer = timer;
        }
        else
        {
            double elapsedSeconds = sentTick != 0
                ? NetworkLifecycle.Instance.SecondsSinceTick(sentTick)
                : 0d;
            double elapsedInCycle = Math.Max(0d, timer) + elapsedSeconds;
            long transitions = (long)Math.Floor(elapsedInCycle / interval);
            int stateCount = Math.Max(2, alternating.subscribers?.Count ?? 0);

            alternating.alternatorState = PositiveModulo(state + transitions, stateCount);
            alternating.timer = (float)(elapsedInCycle % interval);
        }

        alternating.FireOnOutputValueUpdated();
        RefreshLoadedLods(alternating);
    }

    private static int PositiveModulo(long value, int modulus)
    {
        long result = value % modulus;
        return (int)(result < 0 ? result + modulus : result);
    }

    private static void RefreshLoadedLods(GadgetBase gadget)
    {
        if (gadget == null || !gadget.IsLODLoaded || gadget.LODObjects == null)
            return;

        foreach (CustomizerLODObject lod in gadget.LODObjects)
        {
            switch (lod)
            {
                case AlternatingControllerLOD alternatingLod:
                    if (alternatingLod.modeSwitchController != null)
                        alternatingLod.SyncControls();
                    alternatingLod.UpdateLamp();
                    break;
                case GadgetSwitchLOD switchLod when gadget is GadgetSwitch gadgetSwitch:
                    if (switchLod.controlKnobControl != null)
                        switchLod.SyncControls();
                    switchLod.UpdateIndicatorLight(gadgetSwitch);
                    break;
                case GadgetAmpLimiterLOD ampLod:
                    if (ampLod.limitKnobControl != null)
                        ampLod.SyncControls();
                    ampLod.OnStateUpdated();
                    break;
                case GadgetSwitchSetterLOD switchSetterLod:
                    if (switchSetterLod.knbRangeControl != null && switchSetterLod.knbModeControl != null &&
                        switchSetterLod.knbSideControl != null)
                        switchSetterLod.SyncControls();
                    break;
                case GadgetOverheatProtectionLOD overheatLod:
                    if (overheatLod.switchLimitControl != null && overheatLod.switchMethodControl != null)
                        overheatLod.SyncControls();
                    overheatLod.UpdateLamp();
                    break;
                case GadgetDPULOD dpuLod:
                    if (dpuLod.powerSwitchControl != null && dpuLod.orientationSwitchControl != null &&
                        dpuLod.channelSwitchControl != null)
                        dpuLod.SyncControls();
                    break;
                case GadgetATSLOD atsLod:
                    if (atsLod.regimeSelectorControl != null)
                        atsLod.UpdateDisplay();
                    break;
                case GadgetProximityScreenLOD proximityLod when gadget is GadgetProximityScreen proximity:
                    proximityLod.channelSwitchControl?.SetValue(proximity.CurrentChannel / 8f, default);
                    proximityLod.settingSwitchControl?.SetValue(proximity.CurrentMode, default);
                    if (proximityLod.channelSwitchControl != null && proximityLod.settingSwitchControl != null)
                        proximityLod.FindSensor(null);
                    break;
                case GadgetControlPanelLODObject panelLod when gadget is GadgetControlPanel panel:
                    if (panelLod.tiltControl != null && panelLod.panelDegreesPerKnobRound > 0f)
                    {
                        panelLod.tiltControl.SetValue(
                            Mathf.Repeat(panel.tilt, panelLod.panelDegreesPerKnobRound) / panelLod.panelDegreesPerKnobRound,
                            default);
                    }
                    panelLod.smoothedTilt = panel.tilt;
                    panelLod.panel.localRotation = Quaternion.AngleAxis(panel.tilt, panelLod.tiltAxis);
                    break;
            }
        }
    }

    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(value, maximum));
}
