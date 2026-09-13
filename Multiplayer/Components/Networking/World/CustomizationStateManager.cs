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

public static partial class CustomizationStateManager
{
    private static readonly Dictionary<ushort, NetworkedItem> pendingLocalDuctTapeReplacements = [];

    public static void RegisterPendingDuctTapeReplacement(ushort consumedItemNetId, NetworkedItem replacement)
    {
        if (consumedItemNetId != 0 && replacement != null && replacement.NetId == 0)
            pendingLocalDuctTapeReplacements[consumedItemNetId] = replacement;
    }

    public static void ClearPendingLocalState() => pendingLocalDuctTapeReplacements.Clear();

    internal static NetworkedItem GetLocalReplacement(ICustomizationActionPacket packet)
    {
        if (packet is ReplaceDuctTapePacket tape)
        {
            pendingLocalDuctTapeReplacements.TryGetValue(tape.ConsumedItemNetId, out var replacement);
            return replacement;
        }
        if (packet is ReplaceSpoolPacket spool && NetworkedItem.TryGet(spool.ToolItemNetId, out var toolItem))
        {
            var items = toolItem.GetTrackedItem<GadgetSolderingTool>()?.magazine?.items;
            var spentObject = items != null && items.Length > 0 ? items[0] : null;
            if (spentObject != null && NetworkedItem.TryGetNetworkedItem(spentObject.GetComponent<DV.CabControls.ItemBase>(), out var replacement))
                return replacement;
        }
        return null;
    }

    internal static bool BindLocalReplacement(ICustomizationActionPacket packet, NetworkedItem replacement)
    {
        ushort netId = packet switch
        {
            ReplaceSpoolPacket spool => spool.ReplacementSpoolItemNetId,
            ReplaceDuctTapePacket tape => tape.ReplacementItemNetId,
            _ => 0,
        };
        if (replacement == null || netId == 0)
            return false;

        // The native local mutation already happened. Bind its exact object even
        // during snapshot application, without replacing a newer reel in the tool.
        if (NetworkedItem.TryGet(netId, out var duplicate) && duplicate != replacement)
            NetworkedItemManager.Instance.SendToCache(duplicate);
        replacement.NetId = netId;
        replacement.SetOwner(NetworkLifecycle.Instance.Client.PlayerId);
        replacement.MarkAsSynchronized();
        if (packet is ReplaceDuctTapePacket ductTape)
            pendingLocalDuctTapeReplacements.Remove(ductTape.ConsumedItemNetId);
        return true;
    }

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
                if (action.SpoolItemNetId == 0)
                    return NetworkedItem.TryGet(action.ToolItemNetId, out var emptyToolItem) &&
                        emptyToolItem.GetTrackedItem<GadgetSolderingTool>()?.magazine is { } emptyMagazine &&
                        emptyMagazine.items?.FirstOrDefault() == null;
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
            .Concat(state.MagazineStates)
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
