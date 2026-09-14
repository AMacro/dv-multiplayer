using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.InventorySystem;
using Multiplayer.Networking.Packets.Common.Customization;
using System;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static partial class CustomizationStateManager
{
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
        // A newer snapshot/Create may already have supplied the canonical reel.
        // Replaying its earlier replacement must not eject a subsequently loaded reel.
        if (packet.ReplacementSpoolItemNetId != 0 && NetworkedItem.TryGet(packet.ReplacementSpoolItemNetId, out _))
            return;
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
            toolItem.GetTrackedItem<GadgetSolderingTool>() is { magazine: not null } tool)
        {
            if (packet.SpoolItemNetId == 0)
            {
                var current = tool.magazine.items?.FirstOrDefault();
                if (current != null)
                {
                    if (!tool.magazine.RemoveItem(0, true, true))
                        throw new InvalidOperationException($"Could not clear magazine on tool {toolItem.NetId}");
                    NetworkedItem.TryGetNetworkedItem(current.GetComponent<DV.CabControls.ItemBase>(), out var removed);
                    // Preserve the physical reel; an empty baseline does not
                    // authorize destroying an item that may still exist elsewhere.
                    FinalizeDroppedSpool(removed, current);
                }
                tool.currentSpool = null;
            }
            else if (NetworkedItem.TryGet(packet.SpoolItemNetId, out var spoolItem))
                LoadSpool(toolItem, tool, spoolItem);
            else
                return;
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
        Guid? replacementUniqueId = replacementOwner == currentItem?.OwnerPlayerId
            ? currentItem.OwnerUniqueId
            : replacementOwner == toolItem?.OwnerPlayerId ? toolItem.OwnerUniqueId : null;
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

        spentItem.InheritOwner(replacementOwner, replacementUniqueId);
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
        Guid? replacementUniqueId = replacementOwner == oldItem?.OwnerPlayerId
            ? oldItem.OwnerUniqueId : null;
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

        replacementItem.InheritOwner(replacementOwner, replacementUniqueId);
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
}
