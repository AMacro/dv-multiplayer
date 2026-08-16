using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using DV.InventorySystem;
using Multiplayer.Networking.Packets.Common;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch]
public static class CustomizationToolPatch
{
    [HarmonyPatch(typeof(DuctTape), "Awake"), HarmonyPostfix]
    private static void DuctTapeAwake(DuctTape __instance)
    {
        var item = __instance.gameObject.GetOrAddComponent<NetworkedItem>();
        item.Initialize(__instance);
        item.RegisterTrackedValue("ductTape.usesLeft", () => __instance.usesLeft, value =>
        {
            __instance.usesLeft = Math.Max(0, Math.Min(value, __instance.numberOfUses));
            float usesRatio = __instance.numberOfUses > 0
                ? (float)__instance.usesLeft / __instance.numberOfUses
                : 0f;
            __instance.tapeModelUpdater?.UpdateActiveStates(usesRatio);
        });
        item.FinaliseTrackedValues();
    }

    [HarmonyPatch(typeof(DuctTape), nameof(DuctTape.ConsumeOneUse)), HarmonyPrefix]
    private static void BeforeConsumeDuctTape(DuctTape __instance, out DuctTapeReplacementState __state)
    {
        __state = default;
        if (CustomizationSyncScope.IsApplyingRemote || __instance.usesLeft != 1 ||
            !NetworkedItem.TryGetNetworkedItem(__instance.GetComponent<DV.CabControls.ItemBase>(), out var oldItem))
            return;

        int equipSlot = Inventory.Instance.GetEquipSlotForItem(__instance.gameObject);
        if (equipSlot >= 0)
            __state = new DuctTapeReplacementState { EquipSlot = equipSlot, OldItem = oldItem };
    }

    [HarmonyPatch(typeof(DuctTape), nameof(DuctTape.ConsumeOneUse)), HarmonyPostfix]
    private static void AfterConsumeDuctTape(DuctTapeReplacementState __state)
    {
        if (__state.OldItem == null || __state.EquipSlot < 0)
            return;

        var replacementObject = Inventory.Instance.GetEquippedItemAtSlot(__state.EquipSlot);
        if (replacementObject == null || replacementObject.GetComponent<DuctTapeEmpty>() == null ||
            !NetworkedItem.TryGetNetworkedItem(replacementObject.GetComponent<DV.CabControls.ItemBase>(), out var replacementItem))
            return;

        __state.OldItem.SuppressDestroySync();
        replacementItem.MarkAsSynchronized();
        CustomizationStateManager.RegisterPendingDuctTapeReplacement(__state.OldItem.NetId, replacementItem);
        CustomizationStateManager.SendAction(new CommonCustomizationPacket
        {
            Action = CustomizationAction.ReplaceDuctTape,
            ItemNetId = __state.OldItem.NetId,
            OtherItemNetId = replacementItem.NetId,
            OwnerPlayerId = __state.OldItem.OwnerPlayerId,
            Position = replacementObject.transform.position - WorldMover.currentMove,
            Rotation = replacementObject.transform.rotation,
            Flag = true,
        });
    }

    [HarmonyPatch(typeof(GadgetSolderingTool), "Awake"), HarmonyPostfix]
    private static void SolderingToolAwake(GadgetSolderingTool __instance)
    {
        var item = __instance.gameObject.GetOrAddComponent<NetworkedItem>();
        item.Initialize(__instance);
        item.RegisterTrackedValue("soldering.remaining", () => __instance.remainingUnits, value =>
        {
            // -1 is the base game's loaded-empty/ejectable sentinel.
            __instance.remainingUnits = Math.Max(-1, value);
            __instance.OnUnitsChanged();
        });
        item.FinaliseTrackedValues();
    }

    private struct DuctTapeReplacementState
    {
        public int EquipSlot;
        public NetworkedItem OldItem;
    }
}
