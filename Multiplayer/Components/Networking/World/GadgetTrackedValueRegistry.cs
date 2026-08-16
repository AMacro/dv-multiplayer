using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using System;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static class GadgetTrackedValueRegistry
{
    public static void Register(NetworkedItem item, GadgetBase gadget)
    {
        if (gadget == null)
            return;

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
            // AlternatingController inherits GadgetSwitch; its selected interval is the canonical state.
            item.RegisterTrackedValue(
                "alternating.interval",
                () => alternating.SelectedInterval,
                value => SetAndRefresh(alternating, () => alternating.SelectedInterval = value));
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
