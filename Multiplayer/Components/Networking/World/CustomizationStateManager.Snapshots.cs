using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Packets.Common.Customization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static partial class CustomizationStateManager
{
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
        IDictionary<string, List<CustomizationHoleData>> deferredHoleStates = null,
        Func<bool> isSuperseded = null)
    {
        if (isSuperseded?.Invoke() == true)
            yield break;
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
            if (isSuperseded?.Invoke() == true)
                yield break;
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
            if (isSuperseded?.Invoke() == true)
                yield break;
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
        state.MagazineStates.Select(packet => packet.SpoolItemNetId)
            .Concat(state.SnappedItems.Select(packet => packet.SnappedItemNetId))
            .Where(itemNetId => itemNetId != 0)
            .ToHashSet();

    private static void CaptureItem(CustomizationStateData state, NetworkedItem item)
    {
        if (item == null)
            return;

        CaptureMagazineState(state, item);

        var gadget = item.GetTrackedItem<GadgetItem>()?.Gadget;
        if (gadget == null || !gadget.IsLinked || !CustomizationRef.TryCreate(gadget.Custom, out var target))
            return;

        CapturePlacement(state, item, gadget, target);
        CaptureMounts(state, item, gadget);
        CaptureSnappedItems(state, item, gadget);
        CaptureWires(state, item, gadget);
    }

    private static void CaptureMagazineState(CustomizationStateData state, NetworkedItem toolItem)
    {
        var tool = toolItem.GetTrackedItem<GadgetSolderingTool>();
        var magazineItems = tool?.magazine?.items;
        if (tool == null || toolItem.NetId == 0 || magazineItems == null || magazineItems.Length == 0)
            return;

        ushort spoolId = 0;
        if (magazineItems[0] != null)
        {
            var spoolItemBase = magazineItems[0].GetComponent<DV.CabControls.ItemBase>();
            if (!NetworkedItem.TryGetNetworkedItem(spoolItemBase, out var spoolItem) || spoolItem.NetId == 0)
                return; // An unidentified occupied slot is not an empty magazine.
            spoolId = spoolItem.NetId;
        }

        state.MagazineStates.Add(new LoadSpoolPacket
            { ToolItemNetId = toolItem.NetId, SpoolItemNetId = spoolId,
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
}
