using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.InventorySystem;
using DV.Items.Snapping;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Player;
using Multiplayer.Networking.Packets.Common;
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

        CaptureFreeHoles(state);
        LogSnapshot("captured", state);
        return state;
    }

    public static IEnumerator ApplyCurrentStateWhenReady(CustomizationStateData state,
        ICollection<GadgetPlacementData> deferredPlacements = null,
        ICollection<CommonCustomizationPacket> deferredRelationships = null,
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
        bool snapRelationshipsReady = AreSnapRelationshipsReady(state.Relationships, out string snapWaitingFor);
        while (!snapRelationshipsReady && snapInitializationFrames < maxSnapInitializationFrames)
        {
            if (!loggedSnapWait)
            {
                Multiplayer.LogDebug(() => $"Waiting to apply customization snapshot until {snapWaitingFor}");
                loggedSnapWait = true;
            }
            yield return null;
            snapInitializationFrames++;
            snapRelationshipsReady = AreSnapRelationshipsReady(state.Relationships, out snapWaitingFor);
        }

        if (!snapRelationshipsReady)
            Multiplayer.LogWarning($"Customization snapshot timed out waiting for {snapWaitingFor}; deferring unavailable relationships");
        else if (loggedSnapWait)
            Multiplayer.LogDebug(() => $"Customization snap relationships ready after {snapInitializationFrames} frame(s)");

        using (CustomizationSyncScope.Remote())
        {
            ApplyRelationships(state.Relationships, deferredRelationships);
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
            .Concat(state.Relationships.SelectMany(relationship => new[] { relationship.ItemNetId, relationship.OtherItemNetId }))
            .Where(itemNetId => itemNetId != 0)
            .Distinct();
    }

    public static HashSet<ushort> GetItemsRequiringDroppedCreate(CustomizationStateData state) =>
        state.Relationships
            .Where(relationship => relationship.Action is
                CustomizationAction.LoadSpool or CustomizationAction.SnapItem)
            .Select(relationship => relationship.OtherItemNetId)
            .Where(itemNetId => itemNetId != 0)
            .ToHashSet();

    public static void SendAction(CommonCustomizationPacket packet)
    {
        if (NetworkLifecycle.Instance.IsHost())
            NetworkLifecycle.Instance.Server.SendCustomizationAction(packet);
        else
            NetworkLifecycle.Instance.Client.SendCustomizationAction(packet);
    }

    public static void ApplyAction(CommonCustomizationPacket packet)
    {
        Multiplayer.LogDebug(() => $"Applying customization action={packet.Action} item={packet.ItemNetId} " +
            $"other={packet.OtherItemNetId} indices={packet.IndexA}:{packet.IndexB}");

        using (CustomizationSyncScope.Remote())
        {
            switch (packet.Action)
            {
                case CustomizationAction.PlaceGadget:
                    ApplyPlacement(packet);
                    break;
                case CustomizationAction.RemoveGadget:
                    ApplyRemoval(packet);
                    break;
                case CustomizationAction.AddHole:
                case CustomizationAction.MoveHole:
                case CustomizationAction.RemoveHole:
                    ApplyHoleAction(packet);
                    break;
                case CustomizationAction.Mount:
                case CustomizationAction.Unmount:
                    ApplyMountAction(packet);
                    break;
                case CustomizationAction.Wire:
                case CustomizationAction.Unwire:
                    ApplyWireAction(packet);
                    break;
                case CustomizationAction.DropEmptySpool:
                case CustomizationAction.LoadSpool:
                case CustomizationAction.ReplaceSpool:
                    ApplySpoolAction(packet);
                    break;
                case CustomizationAction.ReplaceDuctTape:
                    ApplyDuctTapeReplacement(packet);
                    break;
                case CustomizationAction.SnapItem:
                case CustomizationAction.UnsnapItem:
                    ApplySnapAction(packet);
                    break;
            }
        }
    }

    public static bool ProcessActionAsHost(CommonCustomizationPacket packet, ServerPlayer sender)
    {
        if (packet == null || sender == null || !IsActionReady(packet, out _))
            return false;

        // Customization interactions are client-authoritative. In particular, the
        // host may not have the sender's area loaded, making host-side item and
        // target positions unsuitable for reach validation.
        if (packet.Action is CustomizationAction.ReplaceSpool or CustomizationAction.ReplaceDuctTape)
            packet.OwnerPlayerId = sender.PlayerId;

        ApplyAction(packet);
        bool applied = WasActionApplied(packet);
        if (!applied)
            Multiplayer.LogWarning($"Host did not apply customization action {packet.Action} " +
                $"({packet.ItemNetId}, {packet.OtherItemNetId}); mutation will not be relayed");
        return applied;
    }

    private static bool WasActionApplied(CommonCustomizationPacket packet)
    {
        switch (packet.Action)
        {
            case CustomizationAction.PlaceGadget:
                return TryGadget(packet.ItemNetId, out var placed) && placed.IsLinked &&
                    CustomizationRef.TryResolve(TargetOf(packet), out var placementTarget) &&
                    placed.Custom == placementTarget;
            case CustomizationAction.RemoveGadget:
                return TryGadget(packet.ItemNetId, out var removed) && !removed.IsLinked;
            case CustomizationAction.AddHole:
            case CustomizationAction.MoveHole:
                return CustomizationRef.TryResolve(TargetOf(packet), out var holeTarget) &&
                    holeTarget.FindHole(packet.Position, out _);
            case CustomizationAction.RemoveHole:
                return CustomizationRef.TryResolve(TargetOf(packet), out var removalTarget) &&
                    !removalTarget.FindHole(packet.PreviousPosition, out _);
            case CustomizationAction.Mount:
            case CustomizationAction.Unmount:
                if (!TryGadget(packet.ItemNetId, out var mountOwner))
                    return false;
                var mounts = mountOwner.GetComponents<Mount>();
                if (!IsValidIndex(packet.IndexA, mounts.Length))
                    return false;
                return packet.Action == CustomizationAction.Unmount
                    ? mounts[packet.IndexA].MountedGadget == null
                    : TryGadget(packet.OtherItemNetId, out var mounted) &&
                      mounts[packet.IndexA].MountedGadget == mounted;
            case CustomizationAction.Wire:
            case CustomizationAction.Unwire:
                if (!TryGadget(packet.ItemNetId, out var first) ||
                    !TryGadget(packet.OtherItemNetId, out var second) ||
                    !IsValidIndex(packet.IndexA, first.WireLinkPorts.Count) ||
                    !IsValidIndex(packet.IndexB, second.WireLinkPorts.Count))
                    return false;
                var links = new List<GadgetWiringModule.WireLinkPort>();
                first.WireLinkPorts[packet.IndexA].GetLinks(links);
                bool linked = links.Contains(second.WireLinkPorts[packet.IndexB]);
                return packet.Action == CustomizationAction.Wire ? linked : !linked;
            case CustomizationAction.DropEmptySpool:
                return NetworkedItem.TryGet(packet.ItemNetId, out var dropToolItem) &&
                    dropToolItem.GetTrackedItem<GadgetSolderingTool>() is { HasEjectableSpool: false };
            case CustomizationAction.LoadSpool:
                return NetworkedItem.TryGet(packet.ItemNetId, out var loadToolItem) &&
                    loadToolItem.GetTrackedItem<GadgetSolderingTool>()?.magazine?.items?.FirstOrDefault() is GameObject loaded &&
                    NetworkedItem.TryGetNetworkedItem(loaded.GetComponent<DV.CabControls.ItemBase>(), out var loadedItem) &&
                    loadedItem.NetId == packet.OtherItemNetId;
            case CustomizationAction.ReplaceSpool:
                return packet.OtherItemNetId != 0 && NetworkedItem.TryGet(packet.OtherItemNetId, out _);
            case CustomizationAction.ReplaceDuctTape:
                return packet.OtherItemNetId != 0 && NetworkedItem.TryGet(packet.OtherItemNetId, out _);
            case CustomizationAction.SnapItem:
            case CustomizationAction.UnsnapItem:
                if (!TryGadget(packet.ItemNetId, out var snapOwner))
                    return false;
                var snapPoints = GetSnapPoints(snapOwner);
                if (!IsValidIndex(packet.IndexA, snapPoints.Length))
                    return false;
                if (packet.Action == CustomizationAction.UnsnapItem)
                    return snapPoints[packet.IndexA].SnappedItem == null;
                return snapPoints[packet.IndexA].SnappedItem != null &&
                    NetworkedItem.TryGetNetId(snapPoints[packet.IndexA].SnappedItem, out ushort snappedId) &&
                    snappedId == packet.OtherItemNetId;
            default:
                return false;
        }
    }

    public static IEnumerable<ServerPlayer> GetCustomizationRecipients(CommonCustomizationPacket packet)
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

    public static IEnumerable<ushort> GetActionItemIds(CommonCustomizationPacket packet)
    {
        if (packet?.ItemNetId > 0)
            yield return packet.ItemNetId;
        if (packet?.OtherItemNetId > 0)
            yield return packet.OtherItemNetId;
    }

    internal static bool ActionsConflict(CommonCustomizationPacket first, CommonCustomizationPacket second)
    {
        if (first == null || second == null)
            return false;

        var dependencies = GetActionDependencyKeys(first).ToHashSet();
        return GetActionDependencyKeys(second).Any(dependencies.Contains);
    }

    internal static bool SnapshotPlacementConflicts(GadgetPlacementData placement,
        CommonCustomizationPacket action)
    {
        if (placement == null || action == null)
            return false;

        return placement.ItemNetId != 0 &&
                (placement.ItemNetId == action.ItemNetId || placement.ItemNetId == action.OtherItemNetId) ||
            !string.IsNullOrEmpty(placement.Target.IdentificationKey) &&
                placement.Target.IdentificationKey == action.TargetKey;
    }

    internal static bool SnapshotHoleStateConflicts(string targetKey, CommonCustomizationPacket action) =>
        action != null && !string.IsNullOrEmpty(targetKey) && targetKey == action.TargetKey;

    private static IEnumerable<string> GetActionDependencyKeys(CommonCustomizationPacket packet)
    {
        if (packet == null)
            yield break;

        if (packet.ItemNetId != 0)
            yield return $"item:{packet.ItemNetId}";
        if (packet.OtherItemNetId != 0)
            yield return $"item:{packet.OtherItemNetId}";
        if (!string.IsNullOrEmpty(packet.TargetKey))
            yield return $"target:{packet.TargetKey}";
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

        AddRelationship(state, CustomizationAction.LoadSpool, toolItem.NetId, spoolItem.NetId);
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
                AddRelationship(state, CustomizationAction.Mount, ownerItem.NetId, mountedId, index);
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

            AddRelationship(state, CustomizationAction.SnapItem, ownerItem.NetId, snappedId, index,
                hasPosition: hasAnchorPosition, position: anchorPosition);
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
                    AddRelationship(state, CustomizationAction.Wire, ownerItem.NetId, linkedId, portIndex, linkedPortIndex);
            }
        }
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

    private static bool AreSnapRelationshipsReady(IEnumerable<CommonCustomizationPacket> relationships, out string waitingFor)
    {
        foreach (var relationship in relationships)
        {
            if (relationship.Action != CustomizationAction.SnapItem)
                continue;

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

    private static void ApplyRelationships(IEnumerable<CommonCustomizationPacket> relationships,
        ICollection<CommonCustomizationPacket> deferredRelationships)
    {
        foreach (var relationship in relationships)
        {
            string waitingFor = null;
            bool blockedByEarlierDependency = deferredRelationships?.Any(
                earlier => ActionsConflict(earlier, relationship)) == true;
            if (blockedByEarlierDependency || !IsActionReady(relationship, out waitingFor))
            {
                deferredRelationships?.Add(relationship);
                Multiplayer.LogDebug(() => $"Deferring snapshot relationship {relationship.Action} " +
                    $"({relationship.ItemNetId}, {relationship.OtherItemNetId}) until {waitingFor ?? "an earlier dependency"}");
                continue;
            }

            try
            {
                ApplyAction(relationship);
            }
            catch (Exception exception)
            {
                Multiplayer.LogError($"Failed to apply snapshot relationship {relationship.Action} " +
                    $"({relationship.ItemNetId}, {relationship.OtherItemNetId}): {exception}");
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

    internal static bool IsActionReady(CommonCustomizationPacket packet, out string waitingFor)
    {
        waitingFor = null;
        if (packet == null)
            return false;

        bool needsTarget = packet.Action is CustomizationAction.PlaceGadget or
            CustomizationAction.AddHole or CustomizationAction.MoveHole or CustomizationAction.RemoveHole;
        if (needsTarget && !CustomizationRef.TryResolve(TargetOf(packet), out _))
        {
            waitingFor = $"customization target '{packet.TargetKey}'";
            return false;
        }

        bool hasLocalDuctTapeReplacement = packet.Action == CustomizationAction.ReplaceDuctTape &&
            pendingLocalDuctTapeReplacements.TryGetValue(packet.ItemNetId, out var localReplacement) &&
            localReplacement != null && localReplacement.NetId == 0;

        IEnumerable<ushort> requiredItems = packet.Action switch
        {
            // These actions construct or adopt the replacement locally. The old
            // duct tape may already have been destroyed on the originating peer.
            CustomizationAction.ReplaceDuctTape when hasLocalDuctTapeReplacement => [],
            CustomizationAction.ReplaceDuctTape => [packet.ItemNetId],
            CustomizationAction.ReplaceSpool => [packet.ItemNetId],
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

        if (packet.Action == CustomizationAction.PlaceGadget &&
            (!NetworkedItem.TryGet(packet.ItemNetId, out var placedItem) ||
             placedItem.GetTrackedItem<GadgetItem>() is not { Gadget: not null, Item: not null }))
        {
            waitingFor = $"gadget item {packet.ItemNetId}";
            return false;
        }

        bool needsFirstGadget = packet.Action is CustomizationAction.RemoveGadget or
            CustomizationAction.Mount or CustomizationAction.Unmount or CustomizationAction.Wire or
            CustomizationAction.Unwire or CustomizationAction.SnapItem or CustomizationAction.UnsnapItem;
        if (needsFirstGadget && !TryGadget(packet.ItemNetId, out _))
        {
            waitingFor = $"gadget {packet.ItemNetId}";
            return false;
        }

        bool needsSecondGadget = packet.Action is CustomizationAction.Mount or
            CustomizationAction.Wire or CustomizationAction.Unwire;
        if (needsSecondGadget && !TryGadget(packet.OtherItemNetId, out _))
        {
            waitingFor = $"gadget {packet.OtherItemNetId}";
            return false;
        }

        if (packet.Action is CustomizationAction.Mount or CustomizationAction.Unmount)
        {
            TryGadget(packet.ItemNetId, out var owner);
            if (!IsValidIndex(packet.IndexA, owner.GetComponents<Mount>().Length))
            {
                waitingFor = $"mount {packet.IndexA} on gadget {packet.ItemNetId}";
                return false;
            }
        }

        if (packet.Action is CustomizationAction.Wire or CustomizationAction.Unwire)
        {
            TryGadget(packet.ItemNetId, out var first);
            TryGadget(packet.OtherItemNetId, out var second);
            if (!IsValidIndex(packet.IndexA, first.WireLinkPorts.Count))
            {
                waitingFor = $"wire port {packet.IndexA} on gadget {packet.ItemNetId}";
                return false;
            }

            if (!IsValidIndex(packet.IndexB, second.WireLinkPorts.Count))
            {
                waitingFor = $"wire port {packet.IndexB} on gadget {packet.OtherItemNetId}";
                return false;
            }
        }

        if ((packet.Action is CustomizationAction.SnapItem or CustomizationAction.UnsnapItem) &&
            !IsSnapActionReady(packet, out waitingFor))
            return false;

        if (packet.Action is CustomizationAction.DropEmptySpool or CustomizationAction.LoadSpool or
            CustomizationAction.ReplaceSpool)
        {
            NetworkedItem.TryGet(packet.ItemNetId, out var toolItem);
            if (toolItem?.GetTrackedItem<GadgetSolderingTool>()?.magazine == null)
            {
                waitingFor = $"soldering tool {packet.ItemNetId}";
                return false;
            }
        }

        return true;
    }

    private static bool IsSnapActionReady(CommonCustomizationPacket packet, out string waitingFor)
    {
        if (!TryGadget(packet.ItemNetId, out var owner))
        {
            waitingFor = $"snap-point owner {packet.ItemNetId}";
            return false;
        }

        var snapPoints = GetSnapPoints(owner);
        if (!IsValidIndex(packet.IndexA, snapPoints.Length))
        {
            waitingFor = $"snap point {packet.IndexA} on gadget {packet.ItemNetId}";
            return false;
        }

        if (packet.Action == CustomizationAction.UnsnapItem)
        {
            waitingFor = null;
            return true;
        }

        if (!NetworkedItem.TryGet(packet.OtherItemNetId, out var snappedItem) ||
            snappedItem.Item?.SnappableItem == null)
        {
            waitingFor = $"snappable item {packet.OtherItemNetId}";
            return false;
        }

        var snapPoint = snapPoints[packet.IndexA];
        var snappable = snappedItem.Item.SnappableItem;
        if (((int)snappable.AllowedSnapPointTypes & (int)snapPoint.SnapPointType) == 0 ||
            snappable.GetAnchor(snapPoint.SnapPointType) == null)
        {
            waitingFor = $"snap anchor on item {packet.OtherItemNetId}";
            return false;
        }

        waitingFor = null;
        return true;
    }

    private static void ApplyPlacement(CommonCustomizationPacket packet)
    {
        if (!NetworkedItem.TryGet(packet.ItemNetId, out var item) ||
            item.GetTrackedItem<GadgetItem>() is not GadgetItem gadgetItem ||
            !CustomizationRef.TryResolve(TargetOf(packet), out var target))
            return;

        GadgetItem.Place(target, packet.Position, packet.Rotation, gadgetItem, colliderForPlacementData: null);
    }

    private static void ApplyRemoval(CommonCustomizationPacket packet)
    {
        if (!NetworkedItem.TryGet(packet.ItemNetId, out var item))
            return;

        var removedItem = item.GetTrackedItem<GadgetItem>()?.Gadget?.Remove(packet.Flag);
        if (removedItem != null)
        {
            if (packet.Flag)
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

    private static void ApplyHoleAction(CommonCustomizationPacket packet)
    {
        if (!CustomizationRef.TryResolve(TargetOf(packet), out var target))
            return;

        switch (packet.Action)
        {
            case CustomizationAction.AddHole:
                target.AddHole(packet.Position, packet.Normal);
                break;
            case CustomizationAction.MoveHole:
                if (target.FindHole(packet.PreviousPosition, out var movedHole))
                    target.MoveHole(movedHole, packet.Position, packet.Normal);
                break;
            case CustomizationAction.RemoveHole:
                if (target.FindHole(packet.PreviousPosition, out var removedHole))
                    target.RemoveHole(removedHole);
                break;
        }
    }

    private static void ApplyMountAction(CommonCustomizationPacket packet)
    {
        if (!TryGadget(packet.ItemNetId, out var owner))
            return;

        var mounts = owner.GetComponents<Mount>();
        if (!IsValidIndex(packet.IndexA, mounts.Length))
            return;

        if (packet.Action == CustomizationAction.Unmount)
        {
            mounts[packet.IndexA].UnmountGadget();
            return;
        }

        if (TryGadget(packet.OtherItemNetId, out var mounted))
            mounts[packet.IndexA].MountGadget(mounted);
    }

    private static void ApplyWireAction(CommonCustomizationPacket packet)
    {
        if (!TryGadget(packet.ItemNetId, out var first) ||
            !TryGadget(packet.OtherItemNetId, out var second) ||
            !IsValidIndex(packet.IndexA, first.WireLinkPorts.Count) ||
            !IsValidIndex(packet.IndexB, second.WireLinkPorts.Count))
            return;

        var firstPort = first.WireLinkPorts[packet.IndexA];
        var secondPort = second.WireLinkPorts[packet.IndexB];
        if (packet.Action == CustomizationAction.Wire)
            GadgetWiringModule.WireLinkPort.Wire(firstPort, secondPort);
        else
            GadgetWiringModule.WireLinkPort.Unwire(firstPort, secondPort);
    }

    private static void ApplySpoolAction(CommonCustomizationPacket packet)
    {
        if (!NetworkedItem.TryGet(packet.ItemNetId, out var toolItem))
            return;

        var tool = toolItem.GetTrackedItem<GadgetSolderingTool>();
        if (packet.Action == CustomizationAction.ReplaceSpool)
        {
            ApplySpoolReplacement(toolItem, tool, packet);
            return;
        }

        if (packet.Action == CustomizationAction.DropEmptySpool)
        {
            DropEmptySpool(tool, packet);
            return;
        }

        if (tool?.magazine != null && NetworkedItem.TryGet(packet.OtherItemNetId, out var spoolItem))
        {
            if (tool.magazine.AddItem(spoolItem.gameObject, 0))
                FinalizeContainedSpool(spoolItem);
            else
                Multiplayer.LogWarning($"Could not load reel {spoolItem.NetId} into soldering tool {toolItem.NetId}");
        }
    }

    private static void ApplySpoolReplacement(NetworkedItem toolItem, GadgetSolderingTool tool, CommonCustomizationPacket packet)
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

        if (packet.OtherItemNetId == 0 && NetworkLifecycle.Instance.IsHost())
            packet.OtherItemNetId = spentItem.NetId;
        else if (packet.OtherItemNetId != 0)
            spentItem.NetId = packet.OtherItemNetId;

        spentItem.SetOwner(replacementOwner);
        if (spentItem.Item?.InventorySpecs != null)
            spentItem.Item.InventorySpecs.BelongsToPlayer = belongsToPlayer;
        spentItem.MarkAsSynchronized();
        tool.currentSpool = spentObject.GetComponent<DV.Items.MagazineAmmo>();
        tool.remainingUnits = remainingUnits;
        tool.OnUnitsChanged();
    }

    private static void DropEmptySpool(GadgetSolderingTool tool, CommonCustomizationPacket packet)
    {
        var magazineItems = tool?.magazine?.items;
        var spoolObject = magazineItems != null && magazineItems.Length > 0 ? magazineItems[0] : null;
        var spoolAmmo = spoolObject?.GetComponent<DV.Items.MagazineAmmo>();
        NetworkedItem spoolItem = null;
        if (packet.OtherItemNetId != 0)
            NetworkedItem.TryGet(packet.OtherItemNetId, out spoolItem);
        if (spoolObject != null)
            NetworkedItem.TryGetNetworkedItem(spoolObject.GetComponent<DV.CabControls.ItemBase>(), out spoolItem);

        if (tool == null || spoolAmmo == null || !spoolAmmo.isSpent)
        {
            Multiplayer.LogWarning($"Cannot apply empty-spool ejection: tool={tool != null}, spool={spoolObject != null}, spent={spoolAmmo?.isSpent}");
            return;
        }

        tool.DropEmptySpool();
        if (tool.HasEjectableSpool)
        {
            Multiplayer.LogWarning($"Empty-spool ejection did not remove reel {spoolItem?.NetId ?? 0} from the soldering tool");
            return;
        }

        spoolObject.SetActive(true);
        if (packet.Flag)
        {
            spoolObject.transform.SetPositionAndRotation(
                packet.Position + WorldMover.currentMove,
                packet.Rotation);
        }
        var storage = StorageController.Instance;
        var spoolItemBase = spoolObject.GetComponent<DV.CabControls.ItemBase>();
        if (storage != null && spoolItemBase.BelongsToPlayer() && !storage.AnyStorageContains(spoolItemBase))
            storage.AddItemToWorldStorage(spoolItemBase);
        spoolItem?.MarkAsSynchronized();
    }

    private static void FinalizeContainedSpool(NetworkedItem spoolItem)
    {
        spoolItem.MarkAsSynchronized();
    }

    private static void ApplyDuctTapeReplacement(CommonCustomizationPacket packet)
    {
        NetworkedItem.TryGet(packet.ItemNetId, out var oldItem);
        var oldTape = oldItem?.GetTrackedItem<DV.Customization.Gadgets.Implementations.DuctTape>();
        byte replacementOwner = packet.OwnerPlayerId != 0 ? packet.OwnerPlayerId : oldItem?.OwnerPlayerId ?? 0;
        bool belongsToPlayer = oldItem?.Item?.InventorySpecs?.BelongsToPlayer ?? true;
        Vector3 position = packet.Flag ? packet.Position + WorldMover.currentMove : oldItem?.transform.position ?? Vector3.zero;
        Quaternion rotation = packet.Flag ? packet.Rotation : oldItem?.transform.rotation ?? Quaternion.identity;

        // The originating client records the exact object produced by the native
        // replacement path. Do not infer identity from position: inactive inventory
        // and cache entries can legitimately share the same transform.
        NetworkedItem replacementItem = null;
        if (packet.OtherItemNetId != 0)
            NetworkedItem.TryGet(packet.OtherItemNetId, out replacementItem);

        if (replacementItem == null)
        {
            pendingLocalDuctTapeReplacements.TryGetValue(packet.ItemNetId, out replacementItem);
            if (replacementItem == null || replacementItem.NetId != 0)
                replacementItem = null;
            else
                pendingLocalDuctTapeReplacements.Remove(packet.ItemNetId);
        }
        else
        {
            pendingLocalDuctTapeReplacements.Remove(packet.ItemNetId);
        }
        bool existingReplacement = replacementItem != null;

        if (replacementItem == null)
        {
            if (oldTape?.emptyTapeItemPrefab == null)
            {
                Multiplayer.LogWarning($"Cannot replace consumed duct tape {packet.ItemNetId}: empty-tape prefab unavailable");
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

        if (packet.OtherItemNetId == 0 && NetworkLifecycle.Instance.IsHost())
            packet.OtherItemNetId = replacementItem.NetId;
        else if (packet.OtherItemNetId != 0)
            replacementItem.NetId = packet.OtherItemNetId;

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

    private static void ApplySnapAction(CommonCustomizationPacket packet)
    {
        if (!TryGadget(packet.ItemNetId, out var owner))
        {
            Multiplayer.LogWarning($"Cannot apply {packet.Action}: gadget item {packet.ItemNetId} was not found");
            return;
        }

        var snapPoints = GetSnapPoints(owner);
        if (!IsValidIndex(packet.IndexA, snapPoints.Length))
        {
            Multiplayer.LogWarning($"Cannot apply {packet.Action}: snap point {packet.IndexA} was not found on gadget {packet.ItemNetId} ({snapPoints.Length} available)");
            return;
        }

        if (packet.Action == CustomizationAction.UnsnapItem)
        {
            if (!snapPoints[packet.IndexA].UnsnapItem(forced: true))
                Multiplayer.LogWarning($"Could not unsnap item {packet.OtherItemNetId} from gadget {packet.ItemNetId} point {packet.IndexA}");
            return;
        }

        if (!NetworkedItem.TryGet(packet.OtherItemNetId, out var snappedItem))
        {
            Multiplayer.LogWarning($"Cannot snap item {packet.OtherItemNetId}: networked item was not found");
            return;
        }

        var snapPoint = snapPoints[packet.IndexA];
        bool snapSucceeded;
        if (snapPoint is SnapPointGadgetSliding)
        {
            var anchor = snappedItem.Item.SnappableItem?.GetAnchor(snapPoint.SnapPointType);
            if (anchor == null)
            {
                Multiplayer.LogWarning($"Could not restore sliding snap for item {packet.OtherItemNetId}: anchor {snapPoint.SnapPointType} was not found");
                return;
            }

            // The sliding override performs local physics queries even for a forced
            // snap. Prefer the authoritative anchor offset. Older/incomplete state
            // can derive the same offset from the synchronized snapped item pose.
            if (packet.Flag)
            {
                anchor.localPosition = packet.Position;
            }
            else
            {
                var target = snapPoint.snapPointTarget != null ? snapPoint.snapPointTarget : snapPoint.transform;
                anchor.localPosition = snappedItem.transform.InverseTransformPoint(target.position);
                Multiplayer.LogWarning($"Sliding snap {packet.ItemNetId}:{packet.IndexA} for item {packet.OtherItemNetId} " +
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
            Multiplayer.LogWarning($"Could not snap item {packet.OtherItemNetId} to gadget {packet.ItemNetId} point {packet.IndexA}: " +
                $"pointType={snapPoint.SnapPointType}, allowedTypes={snappable?.AllowedSnapPointTypes}, occupied={snapPoint.SnappedItem != null}");
        }
        else
        {
            if (snappedItem.Item?.ItemRigidbody != null)
                snappedItem.Item.ItemRigidbody.isKinematic = true;
            snappedItem.MarkAsSynchronized();
            Multiplayer.LogDebug(() => $"Snapped item {packet.OtherItemNetId} to gadget {packet.ItemNetId} point {packet.IndexA}");
        }
    }

    private static CustomizationRefData TargetOf(CommonCustomizationPacket packet)
    {
        return new CustomizationRefData
        {
            IdentificationKey = packet.TargetKey,
        };
    }

    private static bool TryItemId(GadgetBase gadget, out ushort id)
    {
        id = 0;
        return gadget?.GadgetItem?.Item != null && NetworkedItem.TryGetNetId(gadget.GadgetItem.Item, out id);
    }

    private static bool IsValidIndex(int index, int count) => index >= 0 && index < count;

    private static void AddRelationship(
        CustomizationStateData state,
        CustomizationAction action,
        ushort firstItem,
        ushort secondItem,
        int firstIndex = 0,
        int secondIndex = 0,
        bool hasPosition = false,
        Vector3 position = default)
    {
        state.Relationships.Add(new CommonCustomizationPacket
        {
            Action = action,
            ItemNetId = firstItem,
            OtherItemNetId = secondItem,
            IndexA = firstIndex,
            IndexB = secondIndex,
            Flag = hasPosition,
            Position = position,
        });
    }

    private static void LogSnapshot(string operation, CustomizationStateData state)
    {
        Multiplayer.LogDebug(() => $"Customization snapshot {operation} targets={state.CustomizationKeys.Count} " +
            $"gadgets={state.Gadgets.Count} relationships={state.Relationships.Count} holes={state.Holes.Count}");
    }
}
