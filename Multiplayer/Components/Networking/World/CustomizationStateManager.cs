using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.InventorySystem;
using DV.Items.Snapping;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Packets.Common;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static class CustomizationStateManager
{
    public static CustomizationStateData CaptureCurrentState()
    {
        var state = new CustomizationStateData();

        foreach (var item in NetworkedItem.GetAll())
            CaptureItem(state, item);

        CaptureFreeHoles(state);
        LogSnapshot("captured", state);
        return state;
    }

    public static IEnumerator ApplyCurrentStateWhenReady(CustomizationStateData state)
    {
        LogSnapshot("applying", state);

        using (CustomizationSyncScope.Remote())
        {
            CreateRelatedItems(state.RelatedItems);
            CreateGadgetItems(state.Gadgets);
        }

        bool loggedWait = false;
        while (!AreSnapshotItemsReady(state, out string waitingFor))
        {
            if (!loggedWait)
            {
                Multiplayer.LogDebug(() => $"Waiting to apply customization snapshot until {waitingFor} is initialized");
                loggedWait = true;
            }
            yield return null;
        }

        if (loggedWait)
            Multiplayer.LogDebug(() => "Customization snapshot items initialized; continuing reconstruction");

        using (CustomizationSyncScope.Remote())
            PlaceGadgets(state.Gadgets);

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
            Multiplayer.LogWarning($"Customization snapshot timed out waiting for {snapWaitingFor}; attempting relationships with diagnostics");
        else if (loggedSnapWait)
            Multiplayer.LogDebug(() => $"Customization snap relationships ready after {snapInitializationFrames} frame(s)");

        using (CustomizationSyncScope.Remote())
        {
            ApplyRelationships(state.Relationships);
            ApplyFreeHoles(state.Holes);
            ApplyTrackedValues(state.Gadgets);
        }

        Multiplayer.LogDebug(() => "Customization snapshot application completed");
    }

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

        AddRelatedItem(state, toolItem, forceDropped: false);
        AddRelatedItem(state, spoolItem, forceDropped: true);
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
            PrefabName = item.Item.InventorySpecs.ItemPrefabName,
            Target = target,
            LocalPosition = gadget.transform.localPosition,
            LocalRotation = gadget.transform.localRotation,
            IsOnGlass = gadget.IsOnGlass,
            TrackedValues = fullState?.States ?? new Dictionary<string, object>(),
        });

        Multiplayer.LogDebug(() => $"Customization snapshot gadget={item.NetId} type={gadget.GetType().Name} " +
            $"target={target.Kind}:{target.TrainCarNetId} pos={gadget.transform.localPosition} " +
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

            if (NetworkedItem.TryGet(snappedId, out var networkedSnappedItem))
                AddRelatedItem(state, networkedSnappedItem, forceDropped: true);

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

    private static void CreateRelatedItems(IEnumerable<ItemUpdateData> relatedItems)
    {
        foreach (var relatedItem in relatedItems)
        {
            if (!NetworkedItem.TryGet(relatedItem.ItemNetId, out _))
                NetworkedItemManager.Instance.CreateCustomizationItem(relatedItem);
        }
    }

    private static void CreateGadgetItems(IEnumerable<GadgetPlacementData> placements)
    {
        foreach (var placement in placements)
            GetOrCreateGadgetItem(placement);
    }

    private static bool AreSnapshotItemsReady(CustomizationStateData state, out string waitingFor)
    {
        foreach (var relatedItem in state.RelatedItems)
        {
            if (!NetworkedItem.TryGet(relatedItem.ItemNetId, out var item) || !item.IsReadyForSnapshots)
            {
                waitingFor = $"related item {relatedItem.ItemNetId}";
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

    private static bool AreSnapRelationshipsReady(IEnumerable<GadgetRelationshipData> relationships, out string waitingFor)
    {
        foreach (var relationship in relationships)
        {
            if ((CustomizationAction)relationship.Action != CustomizationAction.SnapItem)
                continue;

            if (!TryGadget(relationship.ItemA, out var owner))
            {
                waitingFor = $"snap-point owner {relationship.ItemA} is initialized";
                return false;
            }

            var snapPoints = GetSnapPoints(owner);
            if (!IsValidIndex(relationship.IndexA, snapPoints.Length))
            {
                waitingFor = $"snap point {relationship.IndexA} on gadget {relationship.ItemA} is initialized";
                return false;
            }

            if (!NetworkedItem.TryGet(relationship.ItemB, out var snappedItem) ||
                snappedItem.Item?.SnappableItem == null)
            {
                waitingFor = $"snappable item {relationship.ItemB} is initialized";
                return false;
            }

            var snapPoint = snapPoints[relationship.IndexA];
            var snappable = snappedItem.Item.SnappableItem;
            if (((int)snappable.AllowedSnapPointTypes & (int)snapPoint.SnapPointType) == 0 ||
                snappable.GetAnchor(snapPoint.SnapPointType) == null)
            {
                waitingFor = $"snap anchor on item {relationship.ItemB} is initialized";
                return false;
            }
        }

        waitingFor = null;
        return true;
    }

    private static void PlaceGadgets(IEnumerable<GadgetPlacementData> placements)
    {
        foreach (var placement in placements)
        {
            if (!NetworkedItem.TryGet(placement.ItemNetId, out var item))
                continue;

            var gadgetItem = item?.GetTrackedItem<GadgetItem>();
            if (gadgetItem == null || !CustomizationRef.TryResolve(placement.Target, out var target))
                continue;

            var gadget = GadgetItem.Place(
                target,
                placement.LocalPosition,
                placement.LocalRotation,
                gadgetItem,
                colliderForPlacementData: null);

            if (gadget != null)
                gadget.IsOnGlass = placement.IsOnGlass;
        }
    }

    private static NetworkedItem GetOrCreateGadgetItem(GadgetPlacementData placement)
    {
        if (NetworkedItem.TryGet(placement.ItemNetId, out var item))
            return item;

        NetworkedItemManager.Instance.CreateCustomizationItem(new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.Create,
            ItemNetId = placement.ItemNetId,
            PrefabName = placement.PrefabName,
            ItemState = ItemState.Dropped,
            ItemPosition = Vector3.zero,
            ItemRotation = Quaternion.identity,
        });

        NetworkedItem.TryGet(placement.ItemNetId, out item);
        return item;
    }

    private static void ApplyRelationships(IEnumerable<GadgetRelationshipData> relationships)
    {
        foreach (var relationship in relationships)
        {
            ApplyAction(new CommonCustomizationPacket
            {
                Action = (CustomizationAction)relationship.Action,
                ItemNetId = relationship.ItemA,
                OtherItemNetId = relationship.ItemB,
                IndexA = relationship.IndexA,
                IndexB = relationship.IndexB,
                Flag = relationship.HasPosition,
                Position = relationship.Position,
            });
        }
    }

    private static void ApplyFreeHoles(IEnumerable<CustomizationHoleData> holes)
    {
        foreach (var hole in holes)
        {
            if (CustomizationRef.TryResolve(hole.Target, out var target))
                target.AddHole(hole.LocalPosition, hole.LocalNormal);
        }
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
                States = placement.TrackedValues,
            });
        }
    }

    private static void ApplyPlacement(CommonCustomizationPacket packet)
    {
        if (!NetworkedItem.TryGet(packet.ItemNetId, out var item) ||
            item.GetTrackedItem<GadgetItem>() is not GadgetItem gadgetItem ||
            !CustomizationRef.TryResolve(TargetOf(packet), out var target))
            return;

        var gadget = GadgetItem.Place(target, packet.Position, packet.Rotation, gadgetItem, colliderForPlacementData: null);
        if (gadget != null)
            gadget.IsOnGlass = packet.Flag;
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
            ApplySpoolReplacement(tool, packet);
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

    private static void ApplySpoolReplacement(GadgetSolderingTool tool, CommonCustomizationPacket packet)
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

            spentObject = Object.Instantiate(
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

        spentItem.MarkAsSynchronized();
        NetworkedItemManager.Instance.MarkKnownToCurrentPlayers(spentItem);
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
        NetworkedItemManager.Instance.MarkKnownToCurrentPlayers(spoolItem);
    }

    private static void FinalizeContainedSpool(NetworkedItem spoolItem)
    {
        spoolItem.MarkAsSynchronized();
        NetworkedItemManager.Instance.MarkKnownToCurrentPlayers(spoolItem);
    }

    private static void ApplyDuctTapeReplacement(CommonCustomizationPacket packet)
    {
        NetworkedItem.TryGet(packet.ItemNetId, out var oldItem);
        var oldTape = oldItem?.GetTrackedItem<DV.Customization.Gadgets.Implementations.DuctTape>();
        Vector3 position = packet.Flag ? packet.Position + WorldMover.currentMove : oldItem?.transform.position ?? Vector3.zero;
        Quaternion rotation = packet.Flag ? packet.Rotation : oldItem?.transform.rotation ?? Quaternion.identity;

        // The originating client already has the native replacement, but it still
        // has NetId 0 until the server echoes the authoritative replacement ID.
        var replacementItem = NetworkedItem.GetAll().FirstOrDefault(candidate =>
            candidate != null && candidate.NetId == 0 &&
            candidate.GetComponent<DV.Customization.Gadgets.Implementations.DuctTapeEmpty>() != null &&
            Vector3.SqrMagnitude(candidate.transform.position - position) < 4f);
        bool existingLocalReplacement = replacementItem != null;

        if (replacementItem == null)
        {
            if (oldTape?.emptyTapeItemPrefab == null)
            {
                Multiplayer.LogWarning($"Cannot replace consumed duct tape {packet.ItemNetId}: empty-tape prefab unavailable");
                return;
            }

            var replacementObject = Object.Instantiate(oldTape.emptyTapeItemPrefab, position, rotation);
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

        if (!existingLocalReplacement)
        {
            replacementItem.transform.SetPositionAndRotation(position, rotation);
            // A newly instantiated inactive item never reaches Start(), so finish
            // registration now. Otherwise every subsequent drop/hand snapshot is
            // queued indefinitely on the host.
            replacementItem.FinaliseTrackedValues();
            replacementItem.gameObject.SetActive(false);
        }
        replacementItem.MarkAsSynchronized();
        NetworkedItemManager.Instance.MarkKnownToCurrentPlayers(replacementItem);
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
            Kind = (CustomizationTargetKind)packet.TargetKind,
            TrainCarNetId = packet.TargetTrainCarNetId,
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
        state.Relationships.Add(new GadgetRelationshipData
        {
            Action = (byte)action,
            ItemA = firstItem,
            ItemB = secondItem,
            IndexA = firstIndex,
            IndexB = secondIndex,
            HasPosition = hasPosition,
            Position = position,
        });
    }

    private static void AddRelatedItem(CustomizationStateData state, NetworkedItem item, bool forceDropped)
    {
        if (item == null || state.RelatedItems.Any(existing => existing.ItemNetId == item.NetId))
            return;

        var snapshot = item.CreateCurrentUpdateData(ItemUpdateData.ItemUpdateType.Create);
        if (snapshot == null)
            return;

        if (forceDropped)
        {
            snapshot.ItemState = ItemState.Dropped;
            snapshot.ItemPosition = item.transform.position - WorldMover.currentMove;
            snapshot.ItemRotation = item.transform.rotation;
        }

        state.RelatedItems.Add(snapshot);
    }

    private static void LogSnapshot(string operation, CustomizationStateData state)
    {
        Multiplayer.LogDebug(() => $"Customization snapshot {operation} related={state.RelatedItems.Count} " +
            $"gadgets={state.Gadgets.Count} relationships={state.Relationships.Count} holes={state.Holes.Count}");
    }
}
