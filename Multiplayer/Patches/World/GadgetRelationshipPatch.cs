using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using System;
using System.Linq;
using DV.Items;
using DV.Items.Snapping;

namespace Multiplayer.Patches.World;

[HarmonyPatch]
public static class GadgetRelationshipPatch
{
    private static int authoritativeSlidingSnapDepth;

    [HarmonyPatch(typeof(Mount), nameof(Mount.MountGadget)), HarmonyPostfix]
    private static void Mounted(Mount __instance, GadgetBase gadget)
    {
        if (CustomizationSyncScope.IsApplyingRemote || __instance.MountedGadget != gadget ||
            !Id(__instance.ThisGadget, out var a) || !Id(gadget, out var b)) return;
        int index = Array.IndexOf(__instance.ThisGadget.GetComponents<Mount>(), __instance);
        if (index >= 0) Send(CustomizationAction.Mount, a, b, index);
    }

    [HarmonyPatch(typeof(Mount), nameof(Mount.UnmountGadget)), HarmonyPrefix]
    private static void BeforeUnmount(Mount __instance, out MountActionState __state)
    {
        __state = default;
        if (CustomizationSyncScope.IsApplyingRemote || __instance.MountedGadget == null || !Id(__instance.ThisGadget, out var ownerId)) return;
        int index = Array.IndexOf(__instance.ThisGadget.GetComponents<Mount>(), __instance);
        if (index >= 0) __state = new MountActionState { ShouldSend = true, OwnerId = ownerId, Index = index };
    }

    [HarmonyPatch(typeof(Mount), nameof(Mount.UnmountGadget)), HarmonyPostfix]
    private static void Unmounted(Mount __instance, MountActionState __state)
    { if (__state.ShouldSend && __instance.MountedGadget == null) Send(CustomizationAction.Unmount, __state.OwnerId, 0, __state.Index); }

    [HarmonyPatch(typeof(GadgetWiringModule.WireLinkPort), nameof(GadgetWiringModule.WireLinkPort.Wire), new[] { typeof(GadgetWiringModule.WireLinkPort), typeof(GadgetWiringModule.WireLinkPort) }), HarmonyPostfix]
    private static void Wired(GadgetWiringModule.WireLinkPort a, GadgetWiringModule.WireLinkPort b, bool __result)
    { if (__result) SendWire(CustomizationAction.Wire, a, b); }

    [HarmonyPatch(typeof(GadgetWiringModule.WireLinkPort), nameof(GadgetWiringModule.WireLinkPort.Unwire), new[] { typeof(GadgetWiringModule.WireLinkPort), typeof(GadgetWiringModule.WireLinkPort) }), HarmonyPostfix]
    private static void Unwired(GadgetWiringModule.WireLinkPort a, GadgetWiringModule.WireLinkPort b, bool __result)
    { if (__result) SendWire(CustomizationAction.Unwire, a, b); }

    [HarmonyPatch(typeof(GadgetSolderingTool), nameof(GadgetSolderingTool.DropEmptySpool)), HarmonyPrefix]
    private static void BeforeDropSpool(GadgetSolderingTool __instance, out SpoolDropState __state)
    {
        __state = default;
        var magazineItems = __instance.magazine?.items;
        var spoolObject = magazineItems != null && magazineItems.Length > 0 ? magazineItems[0] : null;
        if (!CustomizationSyncScope.IsApplyingRemote && __instance.HasEjectableSpool && spoolObject != null &&
            NetworkedItem.TryGetNetworkedItem(__instance.GetComponent<ItemBase>(), out var toolItem) &&
            NetworkedItem.TryGetNetworkedItem(spoolObject.GetComponent<ItemBase>(), out var spoolItem))
            __state = new SpoolDropState { ToolItemNetId = toolItem.NetId, SpoolItem = spoolItem };
    }

    [HarmonyPatch(typeof(GadgetSolderingTool), nameof(GadgetSolderingTool.DropEmptySpool)), HarmonyPostfix]
    private static void DroppedSpool(GadgetSolderingTool __instance, SpoolDropState __state)
    {
        if (__state.ToolItemNetId == 0 || __state.SpoolItem == null || __instance.HasEjectableSpool)
            return;

        CustomizationStateManager.SendAction(new CommonCustomizationPacket
        {
            Action = CustomizationAction.DropEmptySpool,
            ItemNetId = __state.ToolItemNetId,
            OtherItemNetId = __state.SpoolItem.NetId,
            Position = __state.SpoolItem.transform.position - WorldMover.currentMove,
            Rotation = __state.SpoolItem.transform.rotation,
            Flag = true,
        });
    }

    [HarmonyPatch(typeof(GadgetSolderingTool), "ProcessSoldering"), HarmonyPrefix]
    private static void BeforeSoldering(GadgetSolderingTool __instance, out SpoolReplacementState __state)
    {
        __state = default;
        if (CustomizationSyncScope.IsApplyingRemote || __instance.currentSpool == null || __instance.currentSpool.isSpent ||
            !NetworkedItem.TryGetNetworkedItem(__instance.GetComponent<ItemBase>(), out var toolItem))
            return;

        NetworkedItem.TryGetNetworkedItem(__instance.currentSpool.GetComponent<ItemBase>(), out var oldSpool);
        __state = new SpoolReplacementState { ToolItemNetId = toolItem.NetId, OldSpool = oldSpool };
    }

    [HarmonyPatch(typeof(GadgetSolderingTool), "ProcessSoldering"), HarmonyPostfix]
    private static void AfterSoldering(GadgetSolderingTool __instance, SpoolReplacementState __state)
    {
        var magazineItems = __instance.magazine?.items;
        var spentObject = magazineItems != null && magazineItems.Length > 0 ? magazineItems[0] : null;
        var spentAmmo = spentObject?.GetComponent<MagazineAmmo>();
        if (__state.ToolItemNetId == 0 || spentAmmo == null || !spentAmmo.isSpent)
            return;

        __state.OldSpool?.SuppressDestroySync();
        NetworkedItem.TryGetNetworkedItem(spentObject.GetComponent<ItemBase>(), out var spentSpool);
        spentSpool?.MarkAsSynchronized();
        CustomizationStateManager.SendAction(new CommonCustomizationPacket
        {
            Action = CustomizationAction.ReplaceSpool,
            ItemNetId = __state.ToolItemNetId,
            OtherItemNetId = spentSpool?.NetId ?? 0,
        });
    }

    [HarmonyPatch(typeof(ItemMagazine), nameof(ItemMagazine.AddItem)), HarmonyPostfix]
    private static void SpoolLoaded(ItemMagazine __instance, UnityEngine.GameObject item, int index, bool __result)
    {
        var tool = __instance.GetComponent<GadgetSolderingTool>();
        if (!__result || tool == null || tool.ignoreMagazineDataChange || CustomizationSyncScope.IsApplyingRemote ||
            !NetworkedItem.TryGetNetworkedItem(tool.GetComponent<ItemBase>(), out var toolNet) ||
            !NetworkedItem.TryGetNetworkedItem(item.GetComponent<ItemBase>(), out var spoolNet) ||
            spoolNet.NetId == 0) return;
        Send(CustomizationAction.LoadSpool, toolNet.NetId, spoolNet.NetId, 0);
    }

    [HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.SnapItem), new[] { typeof(ItemBase), typeof(bool) }), HarmonyPostfix]
    private static void Snapped(ItemSnapPointBase __instance, ItemBase itemToSnap, bool __result)
    { if (__result) SendSnap(CustomizationAction.SnapItem, __instance, itemToSnap); }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.SnapItem), new[] { typeof(ItemBase), typeof(bool) })]
    internal static bool SnapItemBase(ItemSnapPointBase instance, ItemBase itemToSnap, bool forced)
        => throw new NotImplementedException("Harmony reverse patch was not applied");

    internal static bool SnapItemBaseAuthoritative(ItemSnapPointBase instance, ItemBase itemToSnap, bool forced)
    {
        authoritativeSlidingSnapDepth++;
        try
        {
            return SnapItemBase(instance, itemToSnap, forced);
        }
        finally
        {
            authoritativeSlidingSnapDepth--;
        }
    }

    [HarmonyPatch(typeof(SnapPointGadgetSliding), nameof(SnapPointGadgetSliding.CanSnapCheck),
        new[] { typeof(SnappableItem), typeof(bool) }), HarmonyPrefix]
    private static bool UseAuthoritativeSlidingSnapResult(ref bool __result)
    {
        if (authoritativeSlidingSnapDepth <= 0)
            return true;

        // The relationship packet already contains the host-approved snap point and
        // sliding anchor. Re-running CalculateSlide here depends on transient local
        // colliders and can reject a valid relationship while a train LOD is loading.
        __result = true;
        return false;
    }

    [HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.UnsnapItem)), HarmonyPrefix]
    private static void BeforeUnsnap(ItemSnapPointBase __instance, out ItemBase __state) => __state = __instance.SnappedItem;

    [HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.UnsnapItem)), HarmonyPostfix]
    private static void Unsnapped(ItemSnapPointBase __instance, bool __result, ItemBase __state)
    { if (__result && __state != null) SendSnap(CustomizationAction.UnsnapItem, __instance, __state); }

    private static void SendSnap(CustomizationAction action, ItemSnapPointBase point, ItemBase item)
    {
        if (CustomizationSyncScope.IsApplyingRemote) return;
        if (!CustomizationStateManager.TryGetSnapPointOwner(point, out var owner) ||
            !CustomizationStateManager.TryGetSnapPointIndex(owner, point, out var index) ||
            !Id(owner, out var ownerId) || !NetworkedItem.TryGetNetId(item, out var itemId)) return;

        bool hasAnchorPosition = false;
        UnityEngine.Vector3 anchorPosition = default;
        if (action == CustomizationAction.SnapItem && item.SnappableItem != null)
        {
            var anchor = item.SnappableItem.GetAnchor(point.SnapPointType);
            if (anchor != null)
            {
                hasAnchorPosition = true;
                anchorPosition = anchor.localPosition;
            }
        }

        CustomizationStateManager.SendAction(new CommonCustomizationPacket
        {
            Action = action,
            ItemNetId = ownerId,
            OtherItemNetId = itemId,
            IndexA = index,
            Flag = hasAnchorPosition,
            Position = anchorPosition,
        });
    }

    private static void SendWire(CustomizationAction action, GadgetWiringModule.WireLinkPort a, GadgetWiringModule.WireLinkPort b)
    {
        if (CustomizationSyncScope.IsApplyingRemote || !Id(a.owner, out var ai) || !Id(b.owner, out var bi)) return;
        CustomizationStateManager.SendAction(new CommonCustomizationPacket { Action = action, ItemNetId = ai, OtherItemNetId = bi,
            IndexA = a.owner.WireLinkPorts.IndexOf(a), IndexB = b.owner.WireLinkPorts.IndexOf(b) });
    }
    private static void Send(CustomizationAction action, ushort a, ushort b, int index) => CustomizationStateManager.SendAction(new CommonCustomizationPacket { Action = action, ItemNetId = a, OtherItemNetId = b, IndexA = index });
    private static bool Id(GadgetBase gadget, out ushort id) { id = 0; return gadget?.GadgetItem?.Item != null && NetworkedItem.TryGetNetId(gadget.GadgetItem.Item, out id); }

    private struct MountActionState
    {
        public bool ShouldSend;
        public ushort OwnerId;
        public int Index;
    }

    private struct SpoolReplacementState
    {
        public ushort ToolItemNetId;
        public NetworkedItem OldSpool;
    }

    private struct SpoolDropState
    {
        public ushort ToolItemNetId;
        public NetworkedItem SpoolItem;
    }
}
