using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.InventorySystem;
using DV.Items.Snapping;
using LiteNetLib.Utils;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Player;
using Multiplayer.Networking.Packets.Common.Customization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static class CustomizationStateManager
{
    private static readonly Dictionary<ushort, NetworkedItem> pendingLocalDuctTapeReplacements = [];

    public static void RegisterPendingDuctTapeReplacement(ushort consumedItemNetId, NetworkedItem replacement)
    {
        if (consumedItemNetId != 0 && replacement != null && replacement.NetId == 0)
            pendingLocalDuctTapeReplacements[consumedItemNetId] = replacement;
    }

    public static void ClearPendingLocalState() => pendingLocalDuctTapeReplacements.Clear();

    public static CustomizationStateData CaptureCurrentState()
    {
        var state = new CustomizationStateData();

        foreach (var item in NetworkedItem.GetAll())
            CaptureItem(state, item);

        // Native OnGadgetWired appends subscribers. Replaying in that same order
        // restores the alternating sequence without assigning its private list.
        state.Wires = state.Wires.OrderBy(GetWireReplayOrder).ToList();

        CaptureFreeHoles(state);
        LogSnapshot("captured", state);
        return state;
    }

    internal static IEnumerator ApplyCurrentStateWhenReady(CustomizationStateData state,
        ICollection<GadgetPlacementData> deferredPlacements = null,
        ICollection<ICustomizationActionPacket> deferredRelationships = null,
        IDictionary<string, List<CustomizationHoleData>> deferredHoleStates = null)
    {
        LogSnapshot("applying", state);

        const int maxItemInitializationFrames = 600;
        int itemInitializationFrames = 0;
        bool loggedWait = false;
        bool itemsReady = AreSnapshotItemsReady(state, out string waitingFor);
        while (!itemsReady && itemInitializationFrames < maxItemInitializationFrames)
        {
            if (!loggedWait)
            {
                Multiplayer.LogDebug(() => $"Waiting to apply customization snapshot until {waitingFor} is initialized");
                loggedWait = true;
            }
            yield return null;
            itemInitializationFrames++;
            itemsReady = AreSnapshotItemsReady(state, out waitingFor);
        }

        if (!itemsReady)
            Multiplayer.LogWarning($"Customization snapshot timed out waiting for {waitingFor}; continuing with available items");
        else if (loggedWait)
            Multiplayer.LogDebug(() => "Customization snapshot items initialized; continuing reconstruction");

        using (CustomizationSyncScope.Remote())
        {
            ResetRelationships();
            ReconcileGadgetPlacements(state.Gadgets, deferredPlacements);
        }

        // NetworkedItem registration can finish before SnappableItem.Initialize in
        // the same Unity frame. Wait for the item's compatible anchor before
        // restoring snap relationships; SnapItem otherwise returns false once and
        // leaves items such as shovels lying at their captured world position.
        const int maxSnapInitializationFrames = 120;
        int snapInitializationFrames = 0;
        bool loggedSnapWait = false;
        bool snapRelationshipsReady = AreSnapRelationshipsReady(state.SnappedItems, out string snapWaitingFor);
        while (!snapRelationshipsReady && snapInitializationFrames < maxSnapInitializationFrames)
        {
            if (!loggedSnapWait)
            {
                Multiplayer.LogDebug(() => $"Waiting to apply customization snapshot until {snapWaitingFor}");
                loggedSnapWait = true;
            }
            yield return null;
            snapInitializationFrames++;
            snapRelationshipsReady = AreSnapRelationshipsReady(state.SnappedItems, out snapWaitingFor);
        }

        if (!snapRelationshipsReady)
            Multiplayer.LogWarning($"Customization snapshot timed out waiting for {snapWaitingFor}; deferring unavailable relationships");
        else if (loggedSnapWait)
            Multiplayer.LogDebug(() => $"Customization snap relationships ready after {snapInitializationFrames} frame(s)");

        using (CustomizationSyncScope.Remote())
        {
            ApplyRelationships(GetSnapshotRelationships(state), deferredRelationships);
            ApplyFreeHoles(state.CustomizationKeys, state.Holes, deferredHoleStates);
            ApplyTrackedValues(state.Gadgets);
        }

        Multiplayer.LogDebug(() => "Customization snapshot application completed");
    }

    public static IEnumerable<ushort> GetReferencedItemIds(CustomizationStateData state)
    {
        if (state == null)
            return [];

        return state.Gadgets.Select(gadget => gadget.ItemNetId)
            .Concat(GetSnapshotRelationships(state).SelectMany(GetActionItemIds))
            .Where(itemNetId => itemNetId != 0)
            .Distinct();
    }

    public static HashSet<ushort> GetItemsRequiringDroppedCreate(CustomizationStateData state) =>
        state.LoadedSpools.Select(packet => packet.SpoolItemNetId)
            .Concat(state.SnappedItems.Select(packet => packet.SnappedItemNetId))
            .Where(itemNetId => itemNetId != 0)
            .ToHashSet();

    internal static void SendAction<T>(T packet)
        where T : CustomizationActionPacket, INetSerializable, new()
    {
        if (NetworkLifecycle.Instance.IsHost())
            NetworkLifecycle.Instance.Server.SendCustomizationAction(packet);
        else
            NetworkLifecycle.Instance.Client.SendCustomizationAction(packet);
    }

    internal static void ApplyAction(ICustomizationActionPacket packet)
    {
        Multiplayer.LogDebug(() => $"Applying customization action={packet.GetType().Name}");

        using (CustomizationSyncScope.Remote())
        {
            switch (packet)
            {
                case PlaceGadgetPacket action: ApplyPlacement(action); break;
                case RemoveGadgetPacket action: ApplyRemoval(action); break;
                case AddHolePacket action: ApplyHoleAction(action); break;
                case MoveHolePacket action: ApplyHoleAction(action); break;
                case RemoveHolePacket action: ApplyHoleAction(action); break;
                case MountGadgetPacket action: ApplyMountAction(action); break;
                case UnmountGadgetPacket action: ApplyMountAction(action); break;
                case WireGadgetsPacket action: ApplyWireAction(action); break;
                case UnwireGadgetsPacket action: ApplyWireAction(action); break;
                case DropEmptySpoolPacket action: ApplySpoolAction(action); break;
                case LoadSpoolPacket action: ApplySpoolAction(action); break;
                case ReplaceSpoolPacket action: ApplySpoolAction(action); break;
                case ReplaceDuctTapePacket action: ApplyDuctTapeReplacement(action); break;
                case SnapItemPacket action: ApplySnapAction(action); break;
                case UnsnapItemPacket action: ApplySnapAction(action); break;
            }
        }
    }

    internal static bool ProcessActionAsHost(ICustomizationActionPacket packet, ServerPlayer sender)
    {
        if (packet == null || sender == null || !IsActionReady(packet, out _))
            return false;

        // Customization interactions are client-authoritative. In particular, the
        // host may not have the sender's area loaded, making host-side item and
        // target positions unsuitable for reach validation.
        if (packet is ReplaceSpoolPacket spool)
            spool.OwnerPlayerId = sender.PlayerId;
        else if (packet is ReplaceDuctTapePacket tape)
            tape.OwnerPlayerId = sender.PlayerId;

        ApplyAction(packet);
        bool applied = WasActionApplied(packet);
        if (!applied)
            Multiplayer.LogWarning($"Host did not apply customization action {packet.GetType().Name}; mutation will not be relayed");
        return applied;
    }

    private static bool WasActionApplied(ICustomizationActionPacket packet)
    {
        switch (packet)
        {
            case PlaceGadgetPacket action:
                return TryGadget(action.ItemNetId, out var placed) && placed.IsLinked &&
                    CustomizationRef.TryResolve(TargetOf(action.TargetKey), out var placementTarget) &&
                    placed.Custom == placementTarget;
            case RemoveGadgetPacket action:
                return TryGadget(action.ItemNetId, out var removed) && !removed.IsLinked;
            case AddHolePacket action:
                return CustomizationRef.TryResolve(TargetOf(action.TargetKey), out var addedTarget) &&
                    addedTarget.FindHole(action.Position, out _);
            case MoveHolePacket action:
                return CustomizationRef.TryResolve(TargetOf(action.TargetKey), out var movedTarget) &&
                    movedTarget.FindHole(action.Position, out _);
            case RemoveHolePacket action:
                return CustomizationRef.TryResolve(TargetOf(action.TargetKey), out var removalTarget) &&
                    !removalTarget.FindHole(action.PreviousPosition, out _);
            case MountGadgetPacket action:
                if (!TryGadget(action.MountItemNetId, out var mountOwner))
                    return false;
                var mounts = mountOwner.GetComponents<Mount>();
                if (!IsValidIndex(action.MountIndex, mounts.Length))
                    return false;
                return TryGadget(action.MountedItemNetId, out var mounted) &&
                    mounts[action.MountIndex].MountedGadget == mounted;
            case UnmountGadgetPacket action:
                return TryGadget(action.MountItemNetId, out var unmountOwner) &&
                    IsValidIndex(action.MountIndex, unmountOwner.GetComponents<Mount>().Length) &&
                    unmountOwner.GetComponents<Mount>()[action.MountIndex].MountedGadget == null;
            case WireGadgetsPacket action:
                return IsWireStateApplied(action.FirstItemNetId, action.SecondItemNetId,
                    action.FirstPortIndex, action.SecondPortIndex, shouldBeLinked: true);
            case UnwireGadgetsPacket action:
                return IsWireStateApplied(action.FirstItemNetId, action.SecondItemNetId,
                    action.FirstPortIndex, action.SecondPortIndex, shouldBeLinked: false);
            case DropEmptySpoolPacket action:
                return NetworkedItem.TryGet(action.ToolItemNetId, out var dropToolItem) &&
                    dropToolItem.GetTrackedItem<GadgetSolderingTool>() is { HasEjectableSpool: false };
            case LoadSpoolPacket action:
                return NetworkedItem.TryGet(action.ToolItemNetId, out var loadToolItem) &&
                    loadToolItem.GetTrackedItem<GadgetSolderingTool>()?.magazine?.items?.FirstOrDefault() is GameObject loaded &&
                    NetworkedItem.TryGetNetworkedItem(loaded.GetComponent<DV.CabControls.ItemBase>(), out var loadedItem) &&
                    loadedItem.NetId == action.SpoolItemNetId;
            case ReplaceSpoolPacket action:
                return action.ReplacementSpoolItemNetId != 0 && NetworkedItem.TryGet(action.ReplacementSpoolItemNetId, out _);
            case ReplaceDuctTapePacket action:
                return action.ReplacementItemNetId != 0 && NetworkedItem.TryGet(action.ReplacementItemNetId, out _);
            case SnapItemPacket action:
                if (!TryGadget(action.GadgetItemNetId, out var snapOwner))
                    return false;
                var snapPoints = GetSnapPoints(snapOwner);
                return IsValidIndex(action.SnapPointIndex, snapPoints.Length) &&
                    snapPoints[action.SnapPointIndex].SnappedItem != null &&
                    NetworkedItem.TryGetNetId(snapPoints[action.SnapPointIndex].SnappedItem, out ushort snappedId) &&
                    snappedId == action.SnappedItemNetId;
            case UnsnapItemPacket action:
                if (!TryGadget(action.GadgetItemNetId, out var unsnapOwner))
                    return false;
                var unsnapPoints = GetSnapPoints(unsnapOwner);
                return IsValidIndex(action.SnapPointIndex, unsnapPoints.Length) &&
                    unsnapPoints[action.SnapPointIndex].SnappedItem == null;
            default:
                return false;
        }
    }

    internal static IEnumerable<ServerPlayer> GetCustomizationRecipients(ICustomizationActionPacket packet)
    {
        if (packet == null || NetworkLifecycle.Instance?.Server == null)
            return [];

        // Customization state is global session state. Every initialized peer must
        // receive every mutation even when it is currently in another streamed
        // area; otherwise approaching the car later exposes stale gadget state.
        // This is routing only—clients are intentionally trusted by this mod.
        return NetworkLifecycle.Instance.Server.ServerPlayers.Where(player =>
            player.LoadingState >= PlayerLoadingState.ReadyForCustomizers &&
            player.CustomizationSnapshotSent);
    }

    internal static IEnumerable<ushort> GetActionItemIds(ICustomizationActionPacket packet) => packet switch
    {
        PlaceGadgetPacket p => NonZero(p.ItemNetId), RemoveGadgetPacket p => NonZero(p.ItemNetId),
        MountGadgetPacket p => NonZero(p.MountItemNetId, p.MountedItemNetId),
        UnmountGadgetPacket p => NonZero(p.MountItemNetId),
        WireGadgetsPacket p => NonZero(p.FirstItemNetId, p.SecondItemNetId),
        UnwireGadgetsPacket p => NonZero(p.FirstItemNetId, p.SecondItemNetId),
        LoadSpoolPacket p => NonZero(p.ToolItemNetId, p.SpoolItemNetId),
        ReplaceSpoolPacket p => NonZero(p.ToolItemNetId, p.ReplacementSpoolItemNetId),
        DropEmptySpoolPacket p => NonZero(p.ToolItemNetId, p.SpoolItemNetId),
        SnapItemPacket p => NonZero(p.GadgetItemNetId, p.SnappedItemNetId),
        UnsnapItemPacket p => NonZero(p.GadgetItemNetId, p.SnappedItemNetId),
        ReplaceDuctTapePacket p => NonZero(p.ConsumedItemNetId, p.ReplacementItemNetId),
        _ => [],
    };

    internal static bool ActionsConflict(ICustomizationActionPacket first, ICustomizationActionPacket second)
    {
        if (first == null || second == null)
            return false;

        var dependencies = GetActionDependencyKeys(first).ToHashSet();
        return GetActionDependencyKeys(second).Any(dependencies.Contains);
    }

    internal static bool SnapshotPlacementConflicts(GadgetPlacementData placement,
        ICustomizationActionPacket action)
    {
        if (placement == null || action == null)
            return false;

        return placement.ItemNetId != 0 && GetActionItemIds(action).Contains(placement.ItemNetId) ||
            !string.IsNullOrEmpty(placement.Target.IdentificationKey) &&
                placement.Target.IdentificationKey == GetTargetKey(action);
    }

    internal static bool SnapshotHoleStateConflicts(string targetKey, ICustomizationActionPacket action) =>
        action != null && !string.IsNullOrEmpty(targetKey) && targetKey == GetTargetKey(action);

    private static IEnumerable<string> GetActionDependencyKeys(ICustomizationActionPacket packet)
    {
        if (packet == null)
            yield break;

        foreach (ushort itemNetId in GetActionItemIds(packet))
            yield return $"item:{itemNetId}";
        string targetKey = GetTargetKey(packet);
        if (!string.IsNullOrEmpty(targetKey))
            yield return $"target:{targetKey}";
    }

    private static IEnumerable<ushort> NonZero(params ushort[] values) => values.Where(value => value != 0);

    private static string GetTargetKey(ICustomizationActionPacket packet) => packet switch
    {
        PlaceGadgetPacket p => p.TargetKey, AddHolePacket p => p.TargetKey,
        MoveHolePacket p => p.TargetKey, RemoveHolePacket p => p.TargetKey, _ => null,
    };

    private static bool IsWireStateApplied(ushort firstId, ushort secondId, int firstIndex,
        int secondIndex, bool shouldBeLinked)
    {
        if (!TryGadget(firstId, out var first) || !TryGadget(secondId, out var second) ||
            !IsValidIndex(firstIndex, first.WireLinkPorts.Count) ||
            !IsValidIndex(secondIndex, second.WireLinkPorts.Count))
            return false;
        var links = new List<GadgetWiringModule.WireLinkPort>();
        first.WireLinkPorts[firstIndex].GetLinks(links);
        return links.Contains(second.WireLinkPorts[secondIndex]) == shouldBeLinked;
    }

    public static bool TryGadget(ushort itemNetId, out GadgetBase gadget)
    {
        gadget = null;
        if (!NetworkedItem.TryGet(itemNetId, out var item))
            return false;

        gadget = item.GetTrackedItem<GadgetItem>()?.Gadget;
        return gadget != null;
    }

    internal static bool TryGetSnapPointOwner(ItemSnapPointBase snapPoint, out GadgetBase owner)
    {
        owner = null;
        if (snapPoint == null)
            return false;

        if (snapPoint is SnapPointGadgetSliding sliding && sliding.drillable?.ThisGadget != null)
            owner = sliding.drillable.ThisGadget;
        else if (snapPoint is SnapPointGadget gadgetSnapPoint && gadgetSnapPoint.gadgetBase != null)
            owner = gadgetSnapPoint.gadgetBase;
        else
            owner = snapPoint.GetComponentInParent<GadgetBase>();

        return owner != null;
    }

    internal static ItemSnapPointBase[] GetSnapPoints(GadgetBase owner)
    {
        if (owner == null)
            return [];

        if (owner is GadgetWithSnapPoint gadgetWithSnapPoint && gadgetWithSnapPoint.snapPoint != null)
            return [gadgetWithSnapPoint.snapPoint];

        return owner.GetComponentsInChildren<ItemSnapPointBase>(true)
            .Where(point => TryGetSnapPointOwner(point, out var pointOwner) && pointOwner == owner)
            .ToArray();
    }

    internal static bool TryGetSnapPointIndex(GadgetBase owner, ItemSnapPointBase snapPoint, out int index)
    {
        index = System.Array.IndexOf(GetSnapPoints(owner), snapPoint);
        return index >= 0;
    }

    private static void CaptureItem(CustomizationStateData state, NetworkedItem item)
    {
        if (item == null)
            return;

        CaptureLoadedSpool(state, item);

        var gadget = item.GetTrackedItem<GadgetItem>()?.Gadget;
        if (gadget == null || !gadget.IsLinked || !CustomizationRef.TryCreate(gadget.Custom, out var target))
            return;

        CapturePlacement(state, item, gadget, target);
        CaptureMounts(state, item, gadget);
        CaptureSnappedItems(state, item, gadget);
        CaptureWires(state, item, gadget);
    }

    private static void CaptureLoadedSpool(CustomizationStateData state, NetworkedItem toolItem)
    {
        var tool = toolItem.GetTrackedItem<GadgetSolderingTool>();
        var magazineItems = tool?.magazine?.items;
        if (magazineItems == null || magazineItems.Length == 0 || magazineItems[0] == null)
            return;

        var spoolItemBase = magazineItems[0].GetComponent<DV.CabControls.ItemBase>();
        if (!NetworkedItem.TryGetNetworkedItem(spoolItemBase, out var spoolItem))
            return;

        state.LoadedSpools.Add(new LoadSpoolPacket
            { ToolItemNetId = toolItem.NetId, SpoolItemNetId = spoolItem.NetId,
                HasRemainingUnits = true, RemainingUnits = tool.remainingUnits });
    }

    private static void CapturePlacement(
        CustomizationStateData state,
        NetworkedItem item,
        GadgetBase gadget,
        CustomizationRefData target)
    {
        var fullState = item.CreateUpdateData(ItemUpdateData.ItemUpdateType.FullSync);
        state.Gadgets.Add(new GadgetPlacementData
        {
            ItemNetId = item.NetId,
            SentTick = NetworkLifecycle.Instance.SynchronizedTick,
            Target = target,
            LocalPosition = gadget.transform.localPosition,
            LocalRotation = gadget.transform.localRotation,
            TrackedValues = fullState?.States ?? new Dictionary<string, object>(),
        });

        Multiplayer.LogDebug(() => $"Customization snapshot gadget={item.NetId} type={gadget.GetType().Name} " +
            $"target={target.IdentificationKey} pos={gadget.transform.localPosition} " +
            $"solder={gadget.SolderingProgressUnits} tracked={fullState?.States?.Count ?? 0}");
    }

    private static void CaptureMounts(CustomizationStateData state, NetworkedItem ownerItem, GadgetBase owner)
    {
        var mounts = owner.GetComponents<Mount>();
        for (int index = 0; index < mounts.Length; index++)
        {
            if (mounts[index].MountedGadget != null && TryItemId(mounts[index].MountedGadget, out var mountedId))
                state.Mounts.Add(new MountGadgetPacket
                    { MountItemNetId = ownerItem.NetId, MountedItemNetId = mountedId, MountIndex = index });
        }
    }

    private static void CaptureSnappedItems(CustomizationStateData state, NetworkedItem ownerItem, GadgetBase owner)
    {
        var snapPoints = GetSnapPoints(owner);
        for (int index = 0; index < snapPoints.Length; index++)
        {
            var snappedItem = snapPoints[index].SnappedItem;
            if (snappedItem == null || !NetworkedItem.TryGetNetId(snappedItem, out var snappedId))
                continue;

            bool hasAnchorPosition = false;
            Vector3 anchorPosition = Vector3.zero;
            if (snappedItem.SnappableItem != null)
            {
                var anchor = snappedItem.SnappableItem.GetAnchor(snapPoints[index].SnapPointType);
                if (anchor != null)
                {
                    hasAnchorPosition = true;
                    anchorPosition = anchor.localPosition;
                }
            }

            state.SnappedItems.Add(new SnapItemPacket { GadgetItemNetId = ownerItem.NetId,
                SnappedItemNetId = snappedId, SnapPointIndex = index,
                HasAnchorPosition = hasAnchorPosition, AnchorPosition = anchorPosition });
            Multiplayer.LogDebug(() => $"Customization snapshot snap owner={ownerItem.NetId} item={snappedId} " +
                $"point={snapPoints[index].GetType().Name}:{snapPoints[index].SnapPointType} " +
                $"anchor={hasAnchorPosition} offset={anchorPosition}");
        }
    }

    private static void CaptureWires(CustomizationStateData state, NetworkedItem ownerItem, GadgetBase owner)
    {
        for (int portIndex = 0; portIndex < owner.WireLinkPorts.Count; portIndex++)
        {
            var links = new List<GadgetWiringModule.WireLinkPort>();
            owner.WireLinkPorts[portIndex].GetLinks(links);

            foreach (var link in links)
            {
                if (!TryItemId(link.owner, out var linkedId) || ownerItem.NetId >= linkedId)
                    continue;

                int linkedPortIndex = link.owner.WireLinkPorts.ToList().IndexOf(link);
                if (linkedPortIndex >= 0)
                    state.Wires.Add(new WireGadgetsPacket { FirstItemNetId = ownerItem.NetId,
                        SecondItemNetId = linkedId, FirstPortIndex = portIndex, SecondPortIndex = linkedPortIndex });
            }
        }
    }

    private static (int ControllerId, int SubscriberIndex) GetWireReplayOrder(WireGadgetsPacket wire)
    {
        if (TryGadget(wire.FirstItemNetId, out var first) && TryGadget(wire.SecondItemNetId, out var second))
        {
            if (first is AlternatingController a &&
                first.WireLinkPorts[wire.FirstPortIndex] is GadgetWiringModule.WireLinkPortMulti<GadgetBase>)
                return (wire.FirstItemNetId, a.subscribers.IndexOf(second));
            if (second is AlternatingController b &&
                second.WireLinkPorts[wire.SecondPortIndex] is GadgetWiringModule.WireLinkPortMulti<GadgetBase>)
                return (wire.SecondItemNetId, b.subscribers.IndexOf(first));
        }
        return (int.MaxValue, 0);
    }

    private static void CaptureFreeHoles(CustomizationStateData state)
    {
        var customizations = Resources.FindObjectsOfTypeAll<Customization>()
            .Where(customization => customization != null && customization.gameObject.scene.IsValid());

        foreach (var customization in customizations)
        {
            if (!CustomizationRef.TryCreate(customization, out var target))
                continue;

            if (!state.CustomizationKeys.Contains(target.IdentificationKey))
                state.CustomizationKeys.Add(target.IdentificationKey);

            foreach (var hole in customization.Holes)
            {
                state.Holes.Add(new CustomizationHoleData
                {
                    Target = target,
                    LocalPosition = hole.transform.localPosition,
                    LocalNormal = hole.transform.localRotation * Vector3.forward,
                });
            }
        }
    }

    private static bool AreSnapshotItemsReady(CustomizationStateData state, out string waitingFor)
    {
        foreach (ushort itemNetId in GetReferencedItemIds(state))
        {
            if (!NetworkedItem.TryGet(itemNetId, out var item) || !item.IsReadyForSnapshots)
            {
                waitingFor = $"network item {itemNetId}";
                return false;
            }
        }

        foreach (var placement in state.Gadgets)
        {
            if (!NetworkedItem.TryGet(placement.ItemNetId, out var item) || !item.IsReadyForSnapshots)
            {
                waitingFor = $"gadget item {placement.ItemNetId}";
                return false;
            }

            var gadgetItem = item.GetTrackedItem<GadgetItem>();
            if (gadgetItem?.Gadget == null || gadgetItem.Item == null)
            {
                waitingFor = $"GadgetItem {placement.ItemNetId} Start";
                return false;
            }
        }

        waitingFor = null;
        return true;
    }

    private static bool AreSnapRelationshipsReady(IEnumerable<SnapItemPacket> relationships, out string waitingFor)
    {
        foreach (var relationship in relationships)
        {
            if (!IsSnapActionReady(relationship, out waitingFor))
                return false;
        }

        waitingFor = null;
        return true;
    }

    private static void ResetRelationships()
    {
        foreach (var item in NetworkedItem.GetAll())
        {
            var gadget = item?.GetTrackedItem<GadgetItem>()?.Gadget;
            if (gadget == null)
                continue;

            try
            {
                foreach (var mount in gadget.GetComponents<Mount>())
                    if (mount.MountedGadget != null)
                        mount.UnmountGadget();

                foreach (var port in gadget.WireLinkPorts)
                    GadgetWiringModule.WireLinkPort.Unwire(port);

                foreach (var snapPoint in GetSnapPoints(gadget))
                    if (snapPoint.SnappedItem != null)
                        snapPoint.UnsnapItem(forced: true);
            }
            catch (Exception exception)
            {
                Multiplayer.LogError($"Failed to reset relationships for snapshot gadget {item.NetId}: {exception}");
            }
        }
    }

    private static void ReconcileGadgetPlacements(IEnumerable<GadgetPlacementData> placements,
        ICollection<GadgetPlacementData> deferredPlacements)
    {
        var desiredPlacements = placements
            .GroupBy(placement => placement.ItemNetId)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var item in NetworkedItem.GetAll())
        {
            var gadgetItem = item?.GetTrackedItem<GadgetItem>();
            var gadget = gadgetItem?.Gadget;
            if (gadget == null || !gadget.IsLinked)
                continue;

            if (desiredPlacements.TryGetValue(item.NetId, out var placement))
            {
                // A timed-out target is not evidence that the placement is stale.
                // Preserve the existing link and let a later snapshot reconcile it.
                if (!CustomizationRef.TryResolve(placement.Target, out var target))
                    continue;

                if (gadget.Custom == target)
                {
                    gadget.transform.SetParent(target.GetParentingTransform(), false);
                    gadget.transform.localPosition = placement.LocalPosition;
                    gadget.transform.localRotation = placement.LocalRotation;
                    gadget.gameObject.SetActive(true);
                    desiredPlacements.Remove(item.NetId);
                    continue;
                }
            }

            try
            {
                if (gadget.Remove(reparentToTrainCar: false) == null)
                {
                    desiredPlacements.Remove(item.NetId);
                    Multiplayer.LogWarning($"Could not remove stale snapshot gadget {item.NetId}; preserving its current placement");
                    continue;
                }

                item.gameObject.SetActive(false);
                item.MarkAsSynchronized();
            }
            catch (Exception exception)
            {
                desiredPlacements.Remove(item.NetId);
                Multiplayer.LogError($"Failed to remove stale snapshot gadget {item.NetId}: {exception}");
            }
        }

        PlaceGadgets(desiredPlacements.Values, deferredPlacements);
    }

    private static void PlaceGadgets(IEnumerable<GadgetPlacementData> placements,
        ICollection<GadgetPlacementData> deferredPlacements)
    {
        foreach (var placement in placements)
        {
            if (!IsSnapshotPlacementReady(placement, out string waitingFor))
            {
                deferredPlacements?.Add(placement);
                Multiplayer.LogDebug(() => $"Deferring snapshot gadget {placement.ItemNetId} until {waitingFor}");
                continue;
            }

            ApplySnapshotPlacement(placement, applyTrackedValues: false);
        }
    }

    private static void ApplyRelationships(IEnumerable<ICustomizationActionPacket> relationships,
        ICollection<ICustomizationActionPacket> deferredRelationships)
    {
        foreach (var relationship in relationships)
        {
            string waitingFor = null;
            bool blockedByEarlierDependency = deferredRelationships?.Any(
                earlier => ActionsConflict(earlier, relationship)) == true;
            if (blockedByEarlierDependency || !IsActionReady(relationship, out waitingFor))
            {
                deferredRelationships?.Add(relationship);
                Multiplayer.LogDebug(() => $"Deferring snapshot relationship {relationship.GetType().Name} " +
                    $"until {waitingFor ?? "an earlier dependency"}");
                continue;
            }

            try
            {
                ApplyAction(relationship);
            }
            catch (Exception exception)
            {
                Multiplayer.LogError($"Failed to apply snapshot relationship {relationship.GetType().Name}: {exception}");
            }
        }
    }

    internal static bool IsSnapshotPlacementReady(GadgetPlacementData placement, out string waitingFor)
    {
        if (!NetworkedItem.TryGet(placement.ItemNetId, out var item) || !item.IsReadyForSnapshots)
        {
            waitingFor = $"gadget item {placement.ItemNetId}";
            return false;
        }

        var gadgetItem = item.GetTrackedItem<GadgetItem>();
        if (gadgetItem?.Gadget == null || gadgetItem.Item == null)
        {
            waitingFor = $"GadgetItem {placement.ItemNetId} Start";
            return false;
        }

        if (!CustomizationRef.TryResolve(placement.Target, out _))
        {
            waitingFor = $"customization target '{placement.Target.IdentificationKey}'";
            return false;
        }

        waitingFor = null;
        return true;
    }

    internal static void ApplySnapshotPlacement(GadgetPlacementData placement, bool applyTrackedValues = true)
    {
        if (!NetworkedItem.TryGet(placement.ItemNetId, out var item) ||
            !CustomizationRef.TryResolve(placement.Target, out var target))
            return;

        var gadgetItem = item.GetTrackedItem<GadgetItem>();
        using (CustomizationSyncScope.Remote())
        {
            try
            {
                var gadget = gadgetItem.Gadget;
                if (gadget.IsLinked && gadget.Custom != target && gadget.Remove(reparentToTrainCar: false) == null)
                {
                    Multiplayer.LogWarning($"Could not remove stale snapshot gadget {placement.ItemNetId}; preserving its current placement");
                    return;
                }

                if (gadget.IsLinked && gadget.Custom == target)
                {
                    gadget.transform.SetParent(target.GetParentingTransform(), false);
                    gadget.transform.localPosition = placement.LocalPosition;
                    gadget.transform.localRotation = placement.LocalRotation;
                    gadget.gameObject.SetActive(true);
                }
                else
                {
                    GadgetItem.Place(target, placement.LocalPosition, placement.LocalRotation, gadgetItem,
                        colliderForPlacementData: null);
                }

                if (applyTrackedValues)
                    ApplyTrackedValues([placement]);
            }
            catch (Exception exception)
            {
                Multiplayer.LogError($"Failed to place snapshot gadget {placement.ItemNetId} on " +
                    $"'{placement.Target.IdentificationKey}': {exception}");
            }
        }
    }

    private static void ApplyFreeHoles(IEnumerable<string> customizationKeys,
        IEnumerable<CustomizationHoleData> holes,
        IDictionary<string, List<CustomizationHoleData>> deferredHoleStates)
    {
        var holesByTarget = holes
            .Where(hole => !string.IsNullOrEmpty(hole.Target.IdentificationKey))
            .GroupBy(hole => hole.Target.IdentificationKey)
            .ToDictionary(group => group.Key, group => group.ToList());
        var targetKeys = customizationKeys
            .Concat(holesByTarget.Keys)
            .Where(key => !string.IsNullOrEmpty(key))
            .Distinct();

        foreach (string key in targetKeys)
        {
            List<CustomizationHoleData> targetHoles = holesByTarget.TryGetValue(key, out var captured)
                ? captured
                : [];
            if (!ApplySnapshotHoleState(key, targetHoles))
            {
                deferredHoleStates?[key] = targetHoles;
                Multiplayer.LogDebug(() => $"Deferring snapshot holes until customization target '{key}' is initialized");
            }
        }
    }

    internal static bool ApplySnapshotHoleState(string targetKey,
        IEnumerable<CustomizationHoleData> holes)
    {
        if (!Customization.TryGetFromIdentificationKey(targetKey, out var target))
            return false;

        using (CustomizationSyncScope.Remote())
        {
            target.ClearHoles();
            foreach (var hole in holes ?? [])
                target.AddHole(hole.LocalPosition, hole.LocalNormal);
        }
        return true;
    }

    private static void ApplyTrackedValues(IEnumerable<GadgetPlacementData> placements)
    {
        foreach (var placement in placements)
        {
            if (!NetworkedItem.TryGet(placement.ItemNetId, out var item))
                continue;

            item.ReceiveSnapshot(new ItemUpdateData
            {
                UpdateType = ItemUpdateData.ItemUpdateType.ObjectState,
                ItemNetId = placement.ItemNetId,
                SentTick = placement.SentTick,
                States = placement.TrackedValues,
            });
        }
    }

    internal static bool IsActionReady(ICustomizationActionPacket packet, out string waitingFor)
    {
        waitingFor = null;
        if (packet == null)
            return false;

        string targetKey = GetTargetKey(packet);
        if (targetKey != null && !CustomizationRef.TryResolve(TargetOf(targetKey), out _))
        {
            waitingFor = $"customization target '{targetKey}'";
            return false;
        }

        bool hasLocalDuctTapeReplacement = packet is ReplaceDuctTapePacket tape &&
            pendingLocalDuctTapeReplacements.TryGetValue(tape.ConsumedItemNetId, out var localReplacement) &&
            localReplacement != null && localReplacement.NetId == 0;

        IEnumerable<ushort> requiredItems = packet switch
        {
            // These actions construct or adopt the replacement locally. The old
            // duct tape may already have been destroyed on the originating peer.
            ReplaceDuctTapePacket when hasLocalDuctTapeReplacement => [],
            ReplaceDuctTapePacket action => NonZero(action.ConsumedItemNetId),
            ReplaceSpoolPacket action => NonZero(action.ToolItemNetId),
            // The contained reel is recoverable from the tool's magazine even when
            // a replacement reassigned its ID before the lookup cache caught up.
            DropEmptySpoolPacket action => NonZero(action.ToolItemNetId),
            _ => GetActionItemIds(packet),
        };

        foreach (ushort itemNetId in requiredItems.Distinct())
        {
            if (!NetworkedItem.TryGet(itemNetId, out var item) || !item.IsReadyForSnapshots)
            {
                waitingFor = $"network item {itemNetId}";
                return false;
            }
        }

        if (packet is PlaceGadgetPacket placement &&
            (!NetworkedItem.TryGet(placement.ItemNetId, out var placedItem) ||
             placedItem.GetTrackedItem<GadgetItem>() is not { Gadget: not null, Item: not null }))
        {
            waitingFor = $"gadget item {placement.ItemNetId}";
            return false;
        }

        ushort firstGadgetId = packet switch
        {
            RemoveGadgetPacket p => p.ItemNetId, MountGadgetPacket p => p.MountItemNetId,
            UnmountGadgetPacket p => p.MountItemNetId, WireGadgetsPacket p => p.FirstItemNetId,
            UnwireGadgetsPacket p => p.FirstItemNetId, SnapItemPacket p => p.GadgetItemNetId,
            UnsnapItemPacket p => p.GadgetItemNetId, _ => 0,
        };
        if (firstGadgetId != 0 && !TryGadget(firstGadgetId, out _))
        {
            waitingFor = $"gadget {firstGadgetId}";
            return false;
        }

        ushort secondGadgetId = packet switch
        {
            MountGadgetPacket p => p.MountedItemNetId, WireGadgetsPacket p => p.SecondItemNetId,
            UnwireGadgetsPacket p => p.SecondItemNetId, _ => 0,
        };
        if (secondGadgetId != 0 && !TryGadget(secondGadgetId, out _))
        {
            waitingFor = $"gadget {secondGadgetId}";
            return false;
        }

        if (packet is MountGadgetPacket or UnmountGadgetPacket)
        {
            int mountIndex = packet is MountGadgetPacket mount ? mount.MountIndex : ((UnmountGadgetPacket)packet).MountIndex;
            TryGadget(firstGadgetId, out var owner);
            if (!IsValidIndex(mountIndex, owner.GetComponents<Mount>().Length))
            {
                waitingFor = $"mount {mountIndex} on gadget {firstGadgetId}";
                return false;
            }
        }

        if (packet is WireGadgetsPacket or UnwireGadgetsPacket)
        {
            int firstPort = packet is WireGadgetsPacket wire ? wire.FirstPortIndex : ((UnwireGadgetsPacket)packet).FirstPortIndex;
            int secondPort = packet is WireGadgetsPacket wire2 ? wire2.SecondPortIndex : ((UnwireGadgetsPacket)packet).SecondPortIndex;
            TryGadget(firstGadgetId, out var first);
            TryGadget(secondGadgetId, out var second);
            if (!IsValidIndex(firstPort, first.WireLinkPorts.Count))
            {
                waitingFor = $"wire port {firstPort} on gadget {firstGadgetId}";
                return false;
            }

            if (!IsValidIndex(secondPort, second.WireLinkPorts.Count))
            {
                waitingFor = $"wire port {secondPort} on gadget {secondGadgetId}";
                return false;
            }

            // Gadget registration can complete while placement is still being
            // reconciled. Wire() rejects ports whose owners do not yet reference
            // the same Customization, and the old client drain discarded that
            // failed action permanently.
            if (packet is WireGadgetsPacket &&
                (first.Custom == null || second.Custom == null || first.Custom != second.Custom))
            {
                waitingFor = $"gadgets {firstGadgetId} and {secondGadgetId} to share a customization target";
                return false;
            }
        }

        if (packet is SnapItemPacket snap && !IsSnapActionReady(snap, out waitingFor))
            return false;
        if (packet is UnsnapItemPacket unsnap && !IsSnapActionReady(unsnap, out waitingFor))
            return false;

        ushort toolId = packet switch
        {
            DropEmptySpoolPacket p => p.ToolItemNetId, LoadSpoolPacket p => p.ToolItemNetId,
            ReplaceSpoolPacket p => p.ToolItemNetId, _ => 0,
        };
        if (toolId != 0)
        {
            NetworkedItem.TryGet(toolId, out var toolItem);
            if (toolItem?.GetTrackedItem<GadgetSolderingTool>()?.magazine == null)
            {
                waitingFor = $"soldering tool {toolId}";
                return false;
            }
        }

        return true;
    }

    private static bool IsSnapActionReady(SnapItemPacket packet, out string waitingFor)
    {
        if (!TryGadget(packet.GadgetItemNetId, out var owner))
        {
            waitingFor = $"snap-point owner {packet.GadgetItemNetId}";
            return false;
        }

        var snapPoints = GetSnapPoints(owner);
        if (!IsValidIndex(packet.SnapPointIndex, snapPoints.Length))
        {
            waitingFor = $"snap point {packet.SnapPointIndex} on gadget {packet.GadgetItemNetId}";
            return false;
        }

        if (!NetworkedItem.TryGet(packet.SnappedItemNetId, out var snappedItem) ||
            snappedItem.Item?.SnappableItem == null)
        {
            waitingFor = $"snappable item {packet.SnappedItemNetId}";
            return false;
        }

        var snapPoint = snapPoints[packet.SnapPointIndex];
        var snappable = snappedItem.Item.SnappableItem;
        if (((int)snappable.AllowedSnapPointTypes & (int)snapPoint.SnapPointType) == 0 ||
            snappable.GetAnchor(snapPoint.SnapPointType) == null)
        {
            waitingFor = $"snap anchor on item {packet.SnappedItemNetId}";
            return false;
        }

        waitingFor = null;
        return true;
    }

    private static bool IsSnapActionReady(UnsnapItemPacket packet, out string waitingFor)
    {
        if (!TryGadget(packet.GadgetItemNetId, out var owner))
        {
            waitingFor = $"snap-point owner {packet.GadgetItemNetId}";
            return false;
        }
        if (!IsValidIndex(packet.SnapPointIndex, GetSnapPoints(owner).Length))
        {
            waitingFor = $"snap point {packet.SnapPointIndex} on gadget {packet.GadgetItemNetId}";
            return false;
        }
        waitingFor = null;
        return true;
    }

    private static void ApplyPlacement(PlaceGadgetPacket packet)
    {
        if (!NetworkedItem.TryGet(packet.ItemNetId, out var item) ||
            item.GetTrackedItem<GadgetItem>() is not GadgetItem gadgetItem ||
            !CustomizationRef.TryResolve(TargetOf(packet.TargetKey), out var target))
            return;

        GadgetItem.Place(target, packet.Position, packet.Rotation, gadgetItem, colliderForPlacementData: null);
    }

    private static void ApplyRemoval(RemoveGadgetPacket packet)
    {
        if (!NetworkedItem.TryGet(packet.ItemNetId, out var item))
            return;

        var removedItem = item.GetTrackedItem<GadgetItem>()?.Gadget?.Remove(packet.ReparentToTrainCar);
        if (removedItem != null)
        {
            if (packet.ReparentToTrainCar)
            {
                // Gadget remover: native removal reparents the item to the train car,
                // where it must remain visible until somebody actually picks it up.
                item.gameObject.SetActive(true);
                item.MarkAsSynchronized();
            }
            else
            {
                // Direct hand removal is relayed before its resulting ownership
                // update. Conceal the remote copy until InHand/InInventory arrives.
                item.gameObject.SetActive(false);
            }
        }
    }

    private static void ApplyHoleAction(AddHolePacket packet)
    {
        if (CustomizationRef.TryResolve(TargetOf(packet.TargetKey), out var target))
            target.AddHole(packet.Position, packet.Normal);
    }

    private static void ApplyHoleAction(MoveHolePacket packet)
    {
        if (CustomizationRef.TryResolve(TargetOf(packet.TargetKey), out var target) &&
            target.FindHole(packet.PreviousPosition, out var movedHole))
            target.MoveHole(movedHole, packet.Position, packet.Normal);
    }

    private static void ApplyHoleAction(RemoveHolePacket packet)
    {
        if (CustomizationRef.TryResolve(TargetOf(packet.TargetKey), out var target) &&
            target.FindHole(packet.PreviousPosition, out var removedHole))
            target.RemoveHole(removedHole);
    }

    private static void ApplyMountAction(MountGadgetPacket packet)
    {
        if (!TryGadget(packet.MountItemNetId, out var owner)) return;
        var mounts = owner.GetComponents<Mount>();
        if (IsValidIndex(packet.MountIndex, mounts.Length) && TryGadget(packet.MountedItemNetId, out var mounted))
            mounts[packet.MountIndex].MountGadget(mounted);
    }

    private static void ApplyMountAction(UnmountGadgetPacket packet)
    {
        if (!TryGadget(packet.MountItemNetId, out var owner)) return;
        var mounts = owner.GetComponents<Mount>();
        if (IsValidIndex(packet.MountIndex, mounts.Length))
            mounts[packet.MountIndex].UnmountGadget();
    }

    private static void ApplyWireAction(WireGadgetsPacket packet) =>
        ApplyWireAction(packet.FirstItemNetId, packet.SecondItemNetId, packet.FirstPortIndex,
            packet.SecondPortIndex, unwire: false);

    private static void ApplyWireAction(UnwireGadgetsPacket packet) =>
        ApplyWireAction(packet.FirstItemNetId, packet.SecondItemNetId, packet.FirstPortIndex,
            packet.SecondPortIndex, unwire: true);

    private static void ApplyWireAction(ushort firstItemNetId, ushort secondItemNetId,
        int firstPortIndex, int secondPortIndex, bool unwire)
    {
        if (!TryGadget(firstItemNetId, out var first) || !TryGadget(secondItemNetId, out var second) ||
            !IsValidIndex(firstPortIndex, first.WireLinkPorts.Count) ||
            !IsValidIndex(secondPortIndex, second.WireLinkPorts.Count))
            return;

        var firstPort = first.WireLinkPorts[firstPortIndex];
        var secondPort = second.WireLinkPorts[secondPortIndex];
        if (!unwire)
        {
            if (GadgetWiringModule.WireLinkPort.AreWired(firstPort, secondPort))
                return;

            // The relationship packet is authoritative. A mono port can retain a
            // stale local counterpart after placement/LOD reconstruction, causing
            // native Wire() to fail silently. Clear only mono ports; multi ports
            // (such as an alternating controller) must retain their other lights.
            ClearStaleMonoWireLinks(firstPort);
            ClearStaleMonoWireLinks(secondPort);

            bool wired = GadgetWiringModule.WireLinkPort.Wire(firstPort, secondPort);
            if (!wired || !GadgetWiringModule.WireLinkPort.AreWired(firstPort, secondPort))
            {
                Multiplayer.LogWarning($"Could not apply authoritative wire {firstItemNetId}:{firstPortIndex} -> " +
                    $"{secondItemNetId}:{secondPortIndex}; first={first.GetType().Name}, " +
                    $"second={second.GetType().Name}, sameTarget={first.Custom != null && first.Custom == second.Custom}");
            }
            else
            {
                Multiplayer.LogDebug(() => $"Wired gadget {firstItemNetId}:{firstPortIndex} to " +
                    $"{secondItemNetId}:{secondPortIndex}");
            }
        }
        else
        {
            if (!GadgetWiringModule.WireLinkPort.AreWired(firstPort, secondPort))
                return;

            if (!GadgetWiringModule.WireLinkPort.Unwire(firstPort, secondPort))
                Multiplayer.LogWarning($"Could not unwire gadget {firstItemNetId}:{firstPortIndex} from " +
                    $"{secondItemNetId}:{secondPortIndex}");
        }
    }

    private static void ClearStaleMonoWireLinks(GadgetWiringModule.WireLinkPort port)
    {
        Type type = port?.GetType();
        while (type != null)
        {
            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(GadgetWiringModule.WireLinkPortMono<>))
            {
                GadgetWiringModule.WireLinkPort.Unwire(port);
                return;
            }

            type = type.BaseType;
        }
    }

    private static void ApplySpoolAction(ReplaceSpoolPacket packet)
    {
        if (NetworkedItem.TryGet(packet.ToolItemNetId, out var toolItem))
            ApplySpoolReplacement(toolItem, toolItem.GetTrackedItem<GadgetSolderingTool>(), packet);
    }

    private static void ApplySpoolAction(DropEmptySpoolPacket packet)
    {
        if (NetworkedItem.TryGet(packet.ToolItemNetId, out var toolItem))
            DropEmptySpool(toolItem.GetTrackedItem<GadgetSolderingTool>(), packet);
    }

    private static void ApplySpoolAction(LoadSpoolPacket packet)
    {
        if (NetworkedItem.TryGet(packet.ToolItemNetId, out var toolItem) &&
            toolItem.GetTrackedItem<GadgetSolderingTool>() is { magazine: not null } tool &&
            NetworkedItem.TryGet(packet.SpoolItemNetId, out var spoolItem))
        {
            LoadSpool(toolItem, tool, spoolItem);
            if (packet.HasRemainingUnits)
            {
                // Native loading restores AMMO after magazine callbacks have run.
                tool.OnAfterMagazineDataLoaded(new Newtonsoft.Json.Linq.JObject { ["AMMO"] = packet.RemainingUnits });
                toolItem.MarkAsSynchronized();
            }
        }
    }

    private static void LoadSpool(NetworkedItem toolItem, GadgetSolderingTool tool, NetworkedItem spoolItem)
    {
        var magazineItems = tool.magazine.items;
        var current = magazineItems != null && magazineItems.Length > 0 ? magazineItems[0] : null;
        NetworkedItem currentItem = null;
        if (current != null)
            NetworkedItem.TryGetNetworkedItem(current.GetComponent<DV.CabControls.ItemBase>(), out currentItem);

        if (currentItem == spoolItem)
        {
            FinalizeContainedSpool(spoolItem);
            return;
        }

        if (current != null)
        {
            var currentAmmo = current.GetComponent<DV.Items.MagazineAmmo>();
            if (currentItem == null || currentAmmo == null)
            {
                Multiplayer.LogWarning($"Could not reconcile reel load for soldering tool {toolItem.NetId}: " +
                    $"contained item hasNetworkIdentity={currentItem != null}, isMagazineAmmo={currentAmmo != null}");
                return;
            }

            // Replace a stale local representation through the native magazine.
            // Relabelling a spent reel as full changes both its model and resource.
            if (!tool.magazine.RemoveItem(0, true, true))
                return;
            currentItem.SuppressDestroySync();
            Inventory.Instance.DestroyItem(current);
        }

        EnsureInWorldStorage(spoolItem.Item);
        if (tool.magazine.AddItem(spoolItem.gameObject, 0))
            FinalizeContainedSpool(spoolItem);
        else
            Multiplayer.LogWarning($"Could not load reel {spoolItem.NetId} into soldering tool {toolItem.NetId}");
    }

    private static void ApplySpoolReplacement(NetworkedItem toolItem, GadgetSolderingTool tool, ReplaceSpoolPacket packet)
    {
        if (tool?.magazine == null)
            return;

        // The base game changes the physical full reel into its empty reel model on
        // the first soldering tick, while the wire resource remains available in
        // remainingUnits. Magazine.AddItem sees isSpent and temporarily sets -1, so
        // preserve the actual resource count across the model replacement.
        int remainingUnits = tool.remainingUnits;

        var magazineItems = tool.magazine.items;
        var current = magazineItems != null && magazineItems.Length > 0 ? magazineItems[0] : null;
        var currentAmmo = current?.GetComponent<DV.Items.MagazineAmmo>();
        NetworkedItem currentItem = null;
        if (current != null)
            NetworkedItem.TryGetNetworkedItem(current.GetComponent<DV.CabControls.ItemBase>(), out currentItem);
        byte replacementOwner = packet.OwnerPlayerId != 0
            ? packet.OwnerPlayerId
            : currentItem?.OwnerPlayerId ?? toolItem?.OwnerPlayerId ?? 0;
        bool belongsToPlayer = currentItem?.Item?.InventorySpecs?.BelongsToPlayer ?? true;
        GameObject spentObject = currentAmmo != null && currentAmmo.isSpent ? current : null;

        if (spentObject == null)
        {
            if (current != null)
            {
                if (NetworkedItem.TryGetNetworkedItem(current.GetComponent<DV.CabControls.ItemBase>(), out var oldSpool))
                    oldSpool.SuppressDestroySync();
                try
                {
                    tool.ignoreMagazineDataChange = true;
                    tool.magazine.RemoveItem(0, true, true);
                    Inventory.Instance.DestroyItem(current);
                }
                finally
                {
                    tool.ignoreMagazineDataChange = false;
                }
            }

            spentObject = UnityEngine.Object.Instantiate(
                tool.emptyCoilItemPrefab,
                tool.reelInteractionPoint.transform.position,
                tool.reelInteractionPoint.transform.rotation);
            tool.UpdateEmptySpoolItemParams(spentObject);
            tool.magazine.AddItem(spentObject, 0);
        }

        var spentItemBase = spentObject.GetComponent<DV.CabControls.ItemBase>();
        if (!NetworkedItem.TryGetNetworkedItem(spentItemBase, out var spentItem))
            return;

        if (packet.ReplacementSpoolItemNetId == 0 && NetworkLifecycle.Instance.IsHost())
            packet.ReplacementSpoolItemNetId = spentItem.NetId;
        else if (packet.ReplacementSpoolItemNetId != 0)
            spentItem.NetId = packet.ReplacementSpoolItemNetId;

        spentItem.SetOwner(replacementOwner);
        if (spentItem.Item?.InventorySpecs != null)
            spentItem.Item.InventorySpecs.BelongsToPlayer = belongsToPlayer;
        spentItem.MarkAsSynchronized();
        tool.currentSpool = spentObject.GetComponent<DV.Items.MagazineAmmo>();
        tool.remainingUnits = remainingUnits;
        tool.OnUnitsChanged();
    }

    private static void DropEmptySpool(GadgetSolderingTool tool, DropEmptySpoolPacket packet)
    {
        if (tool?.magazine == null)
        {
            Multiplayer.LogWarning($"Cannot apply empty-reel ejection: tool={tool != null}, magazine={tool?.magazine != null}");
            return;
        }

        var magazineItems = tool.magazine.items;
        var current = magazineItems != null && magazineItems.Length > 0 ? magazineItems[0] : null;
        NetworkedItem currentItem = null;
        if (current != null)
            NetworkedItem.TryGetNetworkedItem(current.GetComponent<DV.CabControls.ItemBase>(), out currentItem);

        NetworkedItem spoolItem = currentItem != null && currentItem.NetId == packet.SpoolItemNetId
            ? currentItem
            : null;
        if (spoolItem == null)
            NetworkedItem.TryGet(packet.SpoolItemNetId, out spoolItem);
        if (spoolItem == null && currentItem != null)
        {
            Multiplayer.LogWarning($"Recover empty-reel identity for soldering tool {packet.ToolItemNetId}: " +
                $"binding contained reel {currentItem.NetId} to packet reel {packet.SpoolItemNetId}");
            currentItem.NetId = packet.SpoolItemNetId;
            spoolItem = currentItem;
        }

        if (spoolItem == null)
        {
            Multiplayer.LogWarning($"Cannot apply empty-reel ejection for tool {packet.ToolItemNetId}: " +
                $"reel {packet.SpoolItemNetId} is unavailable and magazine slot 0 has no networked reel");
            return;
        }

        if (currentItem != spoolItem)
        {
            Multiplayer.LogWarning($"Reconcile empty-reel ejection for soldering tool {packet.ToolItemNetId}: replacing stale reel " +
                $"{currentItem?.NetId ?? 0} with authoritative reel {spoolItem.NetId}");
            if (current != null)
            {
                if (!tool.magazine.RemoveItem(0, true, true))
                    return;

                // This object represents the full reel that was logically consumed.
                // Let its normal host-side destroy sync remove any phantom copy on
                // peers that missed the physical full-to-empty replacement.
                Inventory.Instance.DestroyItem(current);
            }

            EnsureInWorldStorage(spoolItem.Item);
            if (!tool.magazine.AddItem(spoolItem.gameObject, 0))
            {
                Multiplayer.LogWarning($"Could not place authoritative reel {spoolItem.NetId} in soldering tool {packet.ToolItemNetId} before ejection");
                return;
            }
        }

        var spoolObject = spoolItem.gameObject;
        var spoolAmmo = spoolObject.GetComponent<DV.Items.MagazineAmmo>();
        if (spoolAmmo == null)
        {
            Multiplayer.LogWarning($"Cannot apply empty-reel ejection: item {spoolItem.NetId} is not magazine ammunition");
            return;
        }

        // The action and synchronized remainingUnits are authoritative. A peer may
        // still have the full-reel prefab/flag after a missed replacement or load.
        spoolAmmo.isSpent = true;
        tool.currentSpool = spoolAmmo;
        if (!tool.HasEjectableSpool)
        {
            tool.remainingUnits = -1;
            tool.OnUnitsChanged();
        }

        tool.DropEmptySpool();
        if (tool.HasEjectableSpool)
        {
            Multiplayer.LogWarning($"Empty-spool ejection did not remove reel {spoolItem?.NetId ?? 0} from the soldering tool");
            return;
        }

        spoolObject.SetActive(true);
        if (packet.HasWorldTransform)
        {
            spoolObject.transform.SetPositionAndRotation(
                packet.Position + WorldMover.currentMove,
                packet.Rotation);
        }
        FinalizeDroppedSpool(spoolItem, spoolObject);
    }

    private static void FinalizeContainedSpool(NetworkedItem spoolItem)
    {
        spoolItem.MarkAsSynchronized();
    }

    private static void FinalizeDroppedSpool(NetworkedItem spoolItem, GameObject spoolObject)
    {
        spoolObject.SetActive(true);
        var spoolItemBase = spoolObject.GetComponent<DV.CabControls.ItemBase>();
        EnsureInWorldStorage(spoolItemBase);
        spoolItem?.MarkAsSynchronized();
    }

    private static void EnsureInWorldStorage(DV.CabControls.ItemBase item)
    {
        var storage = StorageController.Instance;
        if (storage != null && item != null && item.BelongsToPlayer() && !storage.AnyStorageContains(item))
            storage.AddItemToWorldStorage(item);
    }

    private static void ApplyDuctTapeReplacement(ReplaceDuctTapePacket packet)
    {
        NetworkedItem.TryGet(packet.ConsumedItemNetId, out var oldItem);
        var oldTape = oldItem?.GetTrackedItem<DV.Customization.Gadgets.Implementations.DuctTape>();
        byte replacementOwner = packet.OwnerPlayerId != 0 ? packet.OwnerPlayerId : oldItem?.OwnerPlayerId ?? 0;
        bool belongsToPlayer = oldItem?.Item?.InventorySpecs?.BelongsToPlayer ?? true;
        Vector3 position = packet.HasWorldTransform ? packet.Position + WorldMover.currentMove : oldItem?.transform.position ?? Vector3.zero;
        Quaternion rotation = packet.HasWorldTransform ? packet.Rotation : oldItem?.transform.rotation ?? Quaternion.identity;

        // The originating client records the exact object produced by the native
        // replacement path. Do not infer identity from position: inactive inventory
        // and cache entries can legitimately share the same transform.
        NetworkedItem replacementItem = null;
        if (packet.ReplacementItemNetId != 0)
            NetworkedItem.TryGet(packet.ReplacementItemNetId, out replacementItem);

        if (replacementItem == null)
        {
            pendingLocalDuctTapeReplacements.TryGetValue(packet.ConsumedItemNetId, out replacementItem);
            if (replacementItem == null || replacementItem.NetId != 0)
                replacementItem = null;
            else
                pendingLocalDuctTapeReplacements.Remove(packet.ConsumedItemNetId);
        }
        else
        {
            pendingLocalDuctTapeReplacements.Remove(packet.ConsumedItemNetId);
        }
        bool existingReplacement = replacementItem != null;

        if (replacementItem == null)
        {
            if (oldTape?.emptyTapeItemPrefab == null)
            {
                Multiplayer.LogWarning($"Cannot replace consumed duct tape {packet.ConsumedItemNetId}: empty-tape prefab unavailable");
                return;
            }

            var replacementObject = UnityEngine.Object.Instantiate(oldTape.emptyTapeItemPrefab, position, rotation);
            var itemBase = replacementObject.GetComponent<DV.CabControls.ItemBase>();
            if (!NetworkedItem.TryGetNetworkedItem(itemBase, out replacementItem))
                replacementItem = replacementObject.AddComponent<NetworkedItem>();
            replacementObject.GetComponent<RespawnOnDrop>().ignoreDistanceFromSpawnPosition = true;
        }

        if (oldItem != null)
        {
            oldItem.SuppressDestroySync();
            oldItem.gameObject.SetActive(false);
            Inventory.Instance.DestroyItem(oldItem.gameObject);
        }

        if (packet.ReplacementItemNetId == 0 && NetworkLifecycle.Instance.IsHost())
            packet.ReplacementItemNetId = replacementItem.NetId;
        else if (packet.ReplacementItemNetId != 0)
            replacementItem.NetId = packet.ReplacementItemNetId;

        replacementItem.SetOwner(replacementOwner);
        if (replacementItem.Item?.InventorySpecs != null)
            replacementItem.Item.InventorySpecs.BelongsToPlayer = belongsToPlayer;
        if (!existingReplacement)
        {
            replacementItem.transform.SetPositionAndRotation(position, rotation);
            // A newly instantiated inactive item never reaches Start(), so finish
            // registration now. Otherwise every subsequent drop/hand snapshot is
            // queued indefinitely on the host.
            replacementItem.FinaliseTrackedValues();
            replacementItem.gameObject.SetActive(false);
        }
        replacementItem.MarkAsSynchronized();
    }

    private static void ApplySnapAction(UnsnapItemPacket packet)
    {
        if (!TryGadget(packet.GadgetItemNetId, out var owner)) return;
        var snapPoints = GetSnapPoints(owner);
        if (IsValidIndex(packet.SnapPointIndex, snapPoints.Length) &&
            !snapPoints[packet.SnapPointIndex].UnsnapItem(forced: true))
            Multiplayer.LogWarning($"Could not unsnap item {packet.SnappedItemNetId} from gadget {packet.GadgetItemNetId} point {packet.SnapPointIndex}");
    }

    private static void ApplySnapAction(SnapItemPacket packet)
    {
        if (!TryGadget(packet.GadgetItemNetId, out var owner)) return;
        var snapPoints = GetSnapPoints(owner);
        if (!IsValidIndex(packet.SnapPointIndex, snapPoints.Length)) return;
        if (!NetworkedItem.TryGet(packet.SnappedItemNetId, out var snappedItem))
        {
            Multiplayer.LogWarning($"Cannot snap item {packet.SnappedItemNetId}: networked item was not found");
            return;
        }

        var snapPoint = snapPoints[packet.SnapPointIndex];
        bool snapSucceeded;
        if (snapPoint is SnapPointGadgetSliding)
        {
            var anchor = snappedItem.Item.SnappableItem?.GetAnchor(snapPoint.SnapPointType);
            if (anchor == null)
            {
                Multiplayer.LogWarning($"Could not restore sliding snap for item {packet.SnappedItemNetId}: anchor {snapPoint.SnapPointType} was not found");
                return;
            }

            // The sliding override performs local physics queries even for a forced
            // snap. Prefer the authoritative anchor offset. Older/incomplete state
            // can derive the same offset from the synchronized snapped item pose.
            if (packet.HasAnchorPosition)
            {
                anchor.localPosition = packet.AnchorPosition;
            }
            else
            {
                var target = snapPoint.snapPointTarget != null ? snapPoint.snapPointTarget : snapPoint.transform;
                anchor.localPosition = snappedItem.transform.InverseTransformPoint(target.position);
                Multiplayer.LogWarning($"Sliding snap {packet.GadgetItemNetId}:{packet.SnapPointIndex} for item {packet.SnappedItemNetId} " +
                    $"had no anchor offset; derived {anchor.localPosition} from synchronized transforms");
            }

            // Keep the rigidbody kinematic while applying an authoritative sliding
            // relationship. Native FinalizeSnap also leaves snapped items kinematic.
            snapSucceeded = global::Multiplayer.Patches.World.GadgetRelationshipPatch.SnapItemBaseAuthoritative(
                snapPoint, snappedItem.Item, forced: true);
        }
        else
        {
            // Match the native save loader for ordinary snap points.
            if (snappedItem.Item?.ItemRigidbody != null && snappedItem.Item.ItemRigidbody.isKinematic)
                snappedItem.Item.ItemRigidbody.isKinematic = false;
            snapSucceeded = snapPoint.SnapItem(snappedItem.Item, forced: true);
        }

        if (!snapSucceeded)
        {
            var snappable = snappedItem.Item?.SnappableItem;
            Multiplayer.LogWarning($"Could not snap item {packet.SnappedItemNetId} to gadget {packet.GadgetItemNetId} point {packet.SnapPointIndex}: " +
                $"pointType={snapPoint.SnapPointType}, allowedTypes={snappable?.AllowedSnapPointTypes}, occupied={snapPoint.SnappedItem != null}");
        }
        else
        {
            if (snappedItem.Item?.ItemRigidbody != null)
                snappedItem.Item.ItemRigidbody.isKinematic = true;
            snappedItem.MarkAsSynchronized();
            Multiplayer.LogDebug(() => $"Snapped item {packet.SnappedItemNetId} to gadget {packet.GadgetItemNetId} point {packet.SnapPointIndex}");
        }
    }

    private static CustomizationRefData TargetOf(string targetKey)
    {
        return new CustomizationRefData
        {
            IdentificationKey = targetKey,
        };
    }

    private static IEnumerable<ICustomizationActionPacket> GetSnapshotRelationships(CustomizationStateData state) =>
        state.Mounts.Cast<ICustomizationActionPacket>()
            .Concat(state.Wires)
            .Concat(state.LoadedSpools)
            .Concat(state.SnappedItems);

    private static bool TryItemId(GadgetBase gadget, out ushort id)
    {
        id = 0;
        return gadget?.GadgetItem?.Item != null && NetworkedItem.TryGetNetId(gadget.GadgetItem.Item, out id);
    }

    private static bool IsValidIndex(int index, int count) => index >= 0 && index < count;

    private static void LogSnapshot(string operation, CustomizationStateData state)
    {
        Multiplayer.LogDebug(() => $"Customization snapshot {operation} targets={state.CustomizationKeys.Count} " +
            $"gadgets={state.Gadgets.Count} relationships={GetSnapshotRelationships(state).Count()} holes={state.Holes.Count}");
    }
}
