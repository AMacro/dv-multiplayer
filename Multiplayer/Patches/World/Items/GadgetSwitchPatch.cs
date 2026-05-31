using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;


namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(GadgetSwitch))]
internal class GadgetSwitchPatch
{
    [HarmonyPatch(nameof(GadgetSwitch.Awake))]
    [HarmonyPostfix]
    private static void Awake(GadgetSwitch __instance)
    {
        Multiplayer.LogDebug(() => $"GadgetSwitch.Awake() GadgetItem: {__instance?.GadgetItem?.name}");

        if (__instance?.GadgetItem == null)
            __instance.ItemAssigned += RegisterTrackedValue;
        else
            RegisterTrackedValue(__instance);
    }

    private static void RegisterTrackedValue(GadgetBase gadget)
    {
        gadget.ItemAssigned -= RegisterTrackedValue;

        if (gadget is not GadgetSwitch gadgetSwitch)
        {
            Multiplayer.LogError($"GadgetSwitchPatch.RegisterTrackedValue() gadget is not GadgetSwitch! {gadget?.GetType()}");
            return;
        }

        var netItem = gadgetSwitch.GadgetItem?.gameObject?.GetOrAddComponent<NetworkedItem>();
        netItem.Initialize(gadgetSwitch.GadgetItem);

        var switchLOD = gadgetSwitch.GetComponentInChildren<GadgetSwitchLOD>();

        if (switchLOD == null)
        {
            Multiplayer.LogError("GadgetSwitchPatch.RegisterTrackedValue() Could not find GadgetSwitchLOD!");
            return;
        }

        netItem.RegisterTrackedValue(
            "value",
            () => gadgetSwitch.outputValue,
            value =>
            {
                gadgetSwitch.SetOutputValue(value);
                switchLOD.SyncControls();   //update visuals
            }
        );

        netItem.FinaliseTrackedValues();
    }
}
