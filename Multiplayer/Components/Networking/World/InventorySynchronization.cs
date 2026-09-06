using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Common;
using DV.InventorySystem;
using DV.Shops;
using DV.Utils;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Packets;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

// Session inventory workflows extend item replication without owning its tick or transport.
internal sealed class InventorySynchronization
{
    private readonly Dictionary<int, NetworkedItem> pendingItemReconciliation = [];
    private readonly Queue<IEnumerator> reconciliationBatches = new();
    private bool drainingReconciliationBatches;
    private readonly NetworkedItemManager manager;

    internal InventorySynchronization(NetworkedItemManager manager) => this.manager = manager;

    internal void PlayerDisconnected(ServerPlayer disconnectedPlayer)
    {
        if (disconnectedPlayer == null || !NetworkLifecycle.Instance.IsHost())
            return;

        byte hostPlayerId = NetworkLifecycle.Instance.Server?.SelfId ?? 0;
        ServerPlayer hostPlayer = null;
        if (hostPlayerId != 0 && disconnectedPlayer.PlayerId != hostPlayerId)
            NetworkLifecycle.Instance.Server.TryGetServerPlayer(hostPlayerId, out hostPlayer);
        ShopPurchaseCoordinator.TransferPendingOwnership(disconnectedPlayer, hostPlayer);

        if (hostPlayerId == 0 || disconnectedPlayer.PlayerId == hostPlayerId)
            return;

        HashSet<NetworkedItem> carriedItems = GetCarriedInventoryItems(disconnectedPlayer);
        IReadOnlyList<PlayerItemSaveData> savedItems = [];
        bool snapshotComplete = disconnectedPlayer.InventoryReconciliationComplete &&
            TryCreateSaveData(carriedItems, out savedItems);
        if (snapshotComplete)
        {
            NetworkedSaveGameManager.Instance.StorePlayerInventory(disconnectedPlayer, savedItems);
        }
        else
        {
            // Position and identity are independently authoritative. A partial
            // reconciliation must preserve the previous inventory snapshot, but
            // it must not also discard the player's latest position.
            NetworkedSaveGameManager.Instance.StorePlayerMetadata(disconnectedPlayer);
        }

        int storedItems = snapshotComplete ? carriedItems.Count : 0;
        int discardedPartialItems = snapshotComplete ? 0 : carriedItems.Count;
        int preservedItems = 0;
        int transferredItems = 0;
        foreach (var item in NetworkedItem.GetAll().ToArray())
        {
            if (item == null || (item.OwnerPlayerId != disconnectedPlayer.PlayerId && !carriedItems.Contains(item)))
                continue;

            if (carriedItems.Contains(item))
            {
                ShopPurchaseCoordinator.SuppressStorageTransitionRestock(item.gameObject);
                UnityEngine.Object.Destroy(item.gameObject);
                continue;
            }

            preservedItems++;
            item.SetOwner(hostPlayerId, true);
            transferredItems++;
        }

        Multiplayer.LogDebug(() =>
            $"Disconnect inventory storage for player {disconnectedPlayer.PlayerId}: " +
            $"storedItems={storedItems}, discardedPartialItems={discardedPartialItems}, " +
            $"preservedItems={preservedItems}, transferredItems={transferredItems}, " +
            $"hostPlayer={hostPlayerId}");
        ShopPurchaseCoordinator.RequestStockRecount();
    }

    public bool TryCaptureCarriedInventory(ServerPlayer player, out IReadOnlyList<PlayerItemSaveData> items) =>
        TryCreateSaveData(GetCarriedInventoryItems(player), out items);

    private static HashSet<NetworkedItem> GetCarriedInventoryItems(ServerPlayer player)
    {
        var allItems = NetworkedItem.GetAll()
            .Where(item => item != null && item.NetId != 0)
            .ToArray();
        foreach (var item in allItems)
            item.RefreshPersistenceState();
        var selected = allItems
            .Where(item => item.OwnerPlayerId == player.PlayerId &&
                item.CurrentState is ItemState.InHand or ItemState.InInventory)
            .ToHashSet();

        bool added;
        do
        {
            var selectedContainerIds = selected
                .Select(item => item.Item?.GetComponent<AItemContainer>()?.ContainerId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet(StringComparer.Ordinal);
            added = false;
            // Contents travel with their container even if another player put
            // them there. Ownership of the root decides whose inventory is saved.
            foreach (var item in allItems)
            {
                if (selected.Contains(item) || item.CurrentState != ItemState.InContainer ||
                    string.IsNullOrEmpty(item.ContainerId) || !selectedContainerIds.Contains(item.ContainerId))
                    continue;
                selected.Add(item);
                added = true;
            }
        } while (added);

        return selected;
    }

    private static bool TryCreateSaveData(IEnumerable<NetworkedItem> items,
        out IReadOnlyList<PlayerItemSaveData> saveData)
    {
        var result = new List<PlayerItemSaveData>();
        foreach (var item in items)
        {
            if (item?.Item?.InventorySpecs == null)
            {
                Multiplayer.LogWarning("Could not save a carried item because its inventory specification is unavailable.");
                saveData = [];
                return false;
            }

            JObject state = null;
            try
            {
                state = item.GetComponent<ItemSaveData>()?.SaveItemData()?.DeepClone() as JObject;
            }
            catch (Exception exception)
            {
                // Some inactive remote proxies (notably Flashlight) cannot run
                // every native save listener. Preserve the item and its layout
                // with default custom state instead of discarding the complete
                // inventory snapshot.
                Multiplayer.LogWarning($"Could not save custom item state for {item.NetId} ({item.name}); " +
                    $"using default state: {exception.Message}");
                state = [];
            }

            // Preserve native stock bookkeeping even if a different save listener
            // on an inactive proxy failed (for example the flashlight listener).
            state ??= [];
            item.GetComponent<ShopRestocker>()?.OnItemSaveDataRequested(state);

            result.Add(new PlayerItemSaveData
            {
                NetId = 0,
                ItemPrefabName = item.Item.InventorySpecs.ItemPrefabName,
                BelongsToPlayer = item.Item.InventorySpecs.BelongsToPlayer,
                IsGrabbed = item.CurrentState == ItemState.InHand,
                State = state ?? [],
                InventorySlotIndex = item.InventorySlotIndex,
                ContainerSlotIndex = item.ContainerSlotIndex,
                ContainerId = item.ContainerId,
                InLockedSlot = item.InLockedSlot,
                IsDropped = item.IsDroppedInInventory,
            });
        }
        saveData = result;
        return true;
    }

    public IEnumerator RecoverStoredInventory(SavedPlayerInventory stored,
        Action<bool, int, int, string> completed)
    {
        if (stored == null || !NetworkLifecycle.Instance.IsHost())
        {
            completed?.Invoke(false, 0, 0, "Invalid stored inventory.");
            yield break;
        }

        var recoverableRecords = new List<PlayerItemSaveData>();
        var excludedContainerIds = new HashSet<string>(StringComparer.Ordinal);
        int excludedEssentialCount = 0;
        foreach (var record in stored.Items)
        {
            if (string.IsNullOrEmpty(record.ItemPrefabName) || !manager.InventoryPrefabs.ContainsKey(record.ItemPrefabName))
            {
                completed?.Invoke(false, 0, 0,
                    $"Missing item prefab '{record.ItemPrefabName ?? "<null>"}'. Nothing was recovered.");
                yield break;
            }

            if (manager.InventoryPrefabs[record.ItemPrefabName].IsEssential)
            {
                excludedEssentialCount++;
                string containerId = record.State?["ContainerId"]?.Value<string>();
                if (!string.IsNullOrEmpty(containerId))
                    excludedContainerIds.Add(containerId);
                continue;
            }

            recoverableRecords.Add(record);
        }

        // If an essential item is itself a container, promote any non-essential
        // direct contents to Lost and Found instead of discarding them with it.
        for (int i = 0; i < recoverableRecords.Count; i++)
        {
            PlayerItemSaveData record = recoverableRecords[i];
            if (!string.IsNullOrEmpty(record.ContainerId) && excludedContainerIds.Contains(record.ContainerId))
            {
                record.ContainerId = null;
                record.ContainerSlotIndex = -1;
                recoverableRecords[i] = record;
            }
        }

        var created = new List<(PlayerItemSaveData Record, NetworkedItem Item)>();
        string failure = null;
        try
        {
            foreach (var record in recoverableRecords)
            {
                GameObject gameObject = UnityEngine.Object.Instantiate(manager.InventoryPrefabs[record.ItemPrefabName].gameObject,
                    Vector3.zero, Quaternion.identity);
                var networkedItem = gameObject.GetOrAddComponent<NetworkedItem>();
                networkedItem.DeferInitialCreate();
                created.Add((record, networkedItem));
            }
        }
        catch (Exception exception)
        {
            failure = $"Could not instantiate recovered inventory: {exception.Message}";
        }

        if (failure == null)
        {
            // Let Start register ItemSaveData listeners, then restore container IDs and item state.
            yield return null;
            foreach (var entry in created)
            {
                try
                {
                    entry.Item.gameObject.SetActive(false);
                    var saveData = entry.Item.GetComponent<ItemSaveData>();
                    saveData?.LoadItemData(entry.Record.State ?? []);
                    saveData?.PostLoadItemData();
                }
                catch (Exception exception)
                {
                    failure = $"Could not restore {entry.Record.ItemPrefabName}: {exception.Message}";
                    break;
                }
            }
        }

        if (failure == null)
        {
            foreach (var entry in created.Where(value => !string.IsNullOrEmpty(value.Record.ContainerId)))
            {
                AItemContainer container = Inventory.Instance.ItemContainerRegistry.GetContainer(entry.Record.ContainerId);
                int slot = entry.Record.ContainerSlotIndex;
                if (container == null || slot < 0 || slot >= container.Capacity ||
                    container[slot] != null || !container.AddItem(entry.Item.gameObject, slot))
                {
                    failure = $"Could not restore {entry.Record.ItemPrefabName} to container " +
                        $"'{entry.Record.ContainerId}' slot {slot}.";
                    break;
                }
            }
        }

        if (failure != null)
        {
            foreach (var entry in created)
            {
                if (entry.Item == null)
                    continue;
                entry.Item.SuppressDestroySync();
                ShopPurchaseCoordinator.SuppressStorageTransitionRestock(entry.Item.gameObject);
                Inventory.Instance.DestroyItem(entry.Item.gameObject);
            }
            completed?.Invoke(false, 0, 0, failure + " Nothing was recovered.");
            yield break;
        }

        byte hostPlayerId = NetworkLifecycle.Instance.Server?.SelfId ?? 0;
        int rootCount = 0;
        int containerCount = created.Count(entry => entry.Item.GetComponent<AItemContainer>() != null);
        foreach (var entry in created)
        {
            entry.Item.SetOwner(hostPlayerId);
            if (!string.IsNullOrEmpty(entry.Record.ContainerId))
                continue;

            StorageController.Instance.AddItemToLostAndFound(entry.Item.Item, false);
            rootCount++;
        }

        foreach (var entry in created)
            entry.Item.GetComponent<ItemSaveData>()?.PostContainerLoadData();

        foreach (var entry in created)
            entry.Item.PublishDeferredCreate();

        NetworkedSaveGameManager.Instance.ResetPlayerInventory(stored.Guid);
        SaveGameManager.Instance.Save(SaveType.Auto, null, true);
        Multiplayer.LogDebug(() => $"Recovered offline inventory for {stored.Guid}: " +
            $"items={created.Count}, containers={containerCount}, lostAndFoundRoots={rootCount}, " +
            $"excludedEssentials={excludedEssentialCount}, resetToFirstTime=true");
        completed?.Invoke(true, created.Count, containerCount, null);
    }

    internal IReadOnlyList<ServerboundItemReconciliationPacket> CreateItemReconciliationRequests()
    {
        pendingItemReconciliation.Clear();
        var packets = new List<ServerboundItemReconciliationPacket>();
        var packet = new ServerboundItemReconciliationPacket();
        int localId = 1;

        foreach (var item in NetworkedItem.GetAll())
        {
            if (packet.Items.Count >= ItemReconciliationProtocol.MaxItems)
            {
                packets.Add(packet);
                packet = new ServerboundItemReconciliationPacket();
            }

            if (!IsReconciliationCandidate(item))
                continue;

            var snapshot = item.CreateCurrentUpdateData(ItemUpdateData.ItemUpdateType.Create);
            if (snapshot == null)
                continue;

            snapshot.ItemNetId = 0;
            pendingItemReconciliation[localId] = item;
            packet.Items.Add(new ItemReconciliationRequest(
                localId,
                item.Item.InventorySpecs.BelongsToPlayer,
                snapshot));
            localId++;
        }

        packet.IsFinalBatch = true;
        packets.Add(packet);
        Multiplayer.LogDebug(() => $"Prepared {localId - 1} local items in {packets.Count} batch(es) for authoritative reconciliation");
        return packets;
    }

    internal bool ApplyItemReconciliation(ClientboundItemReconciliationPacket packet)
    {
        foreach (var result in packet?.Items ?? [])
        {
            if (!pendingItemReconciliation.TryGetValue(result.LocalId, out var localItem))
            {
                Multiplayer.LogWarning($"Received item reconciliation for unknown local item {result.LocalId}");
                continue;
            }

            pendingItemReconciliation.Remove(result.LocalId);
            if (result.NetId == 0)
            {
                Multiplayer.LogWarning($"Host rejected reconciliation for local item {result.LocalId} ({localItem?.name})");
                continue;
            }

            // Loading is allowed to continue after the bounded reconciliation
            // wait. The local item can therefore be consumed or destroyed before
            // a delayed reliable response arrives; let the host-created canonical
            // item take over instead of dereferencing Unity's destroyed wrapper.
            if (localItem == null)
            {
                Multiplayer.LogWarning($"Local reconciliation item {result.LocalId} was destroyed before " +
                    $"authoritative NetId {result.NetId} arrived");
                if (result.Snapshot != null)
                    manager.CreateItem(result.Snapshot);
                continue;
            }

            if (NetworkedItem.TryGet(result.NetId, out var existingItem) && existingItem != localItem)
                manager.SendToCache(existingItem);

            localItem.NetId = result.NetId;
            localItem.SetOwner(NetworkLifecycle.Instance.Client?.PlayerId ?? 0);
            localItem.MarkAsSynchronized();
            Multiplayer.LogDebug(() => $"Reconciled local item {result.LocalId} ({localItem.name}) as NetId {result.NetId}");
        }

        if (packet?.IsFinalBatch != true)
            return false;

        foreach (var unresolved in pendingItemReconciliation)
            Multiplayer.LogWarning($"Host did not return reconciliation data for local item {unresolved.Key} ({unresolved.Value?.name})");
        pendingItemReconciliation.Clear();
        manager.ClientInitialised = true;
        return true;
    }

    internal void ContinueAfterItemReconciliationTimeout()
    {
        // Allow ordinary item updates to resume, but retain the local-ID mapping so
        // a delayed reliable response can still assign authoritative IDs.
        manager.ClientInitialised = true;
    }

    internal void EnqueueClientItemReconciliation(ItemReconciliationRequest[] requests, bool isFinalBatch,
        ServerPlayer player, Action<ClientboundItemReconciliationPacket> completed)
    {
        reconciliationBatches.Enqueue(ReconcileClientItems(requests, isFinalBatch, player, completed));
        if (!drainingReconciliationBatches)
            CoroutineManager.Instance.StartCoroutine(DrainReconciliationBatches());
    }

    private IEnumerator DrainReconciliationBatches()
    {
        drainingReconciliationBatches = true;
        try
        {
            // In particular, an empty final batch must not acknowledge completion
            // before preceding batches have initialized their native objects.
            while (reconciliationBatches.Count > 0)
                yield return reconciliationBatches.Dequeue();
        }
        finally
        {
            drainingReconciliationBatches = false;
        }
    }

    private IEnumerator ReconcileClientItems(
        ItemReconciliationRequest[] requests, bool isFinalBatch,
        ServerPlayer player, Action<ClientboundItemReconciliationPacket> completed)
    {
        var response = new ClientboundItemReconciliationPacket
        {
            IsFinalBatch = isFinalBatch,
        };
        if (!NetworkLifecycle.Instance.IsHost() || requests == null || player == null)
            yield break;
        if (!NetworkLifecycle.Instance.Server.TryGetServerPlayer(player.PlayerId, out var initialPlayer) ||
            initialPlayer != player)
            yield break;

        var created = new List<(ItemReconciliationRequest Request, NetworkedItem Item)>();
        foreach (var request in requests)
        {
            var snapshot = request.Snapshot;
            GameObject gameObject = null;
            try
            {
                if (snapshot != null && snapshot.ItemNetId == 0 &&
                    !string.IsNullOrEmpty(snapshot.PrefabName) &&
                    manager.InventoryPrefabs.TryGetValue(snapshot.PrefabName, out var spec) &&
                    IsAllowedReconciliationState(snapshot.ItemState, spec))
                {
                    gameObject = UnityEngine.Object.Instantiate(
                        spec.gameObject,
                        snapshot.ItemPosition + WorldMover.currentMove,
                        snapshot.ItemRotation);
                    var networkedItem = gameObject.GetOrAddComponent<NetworkedItem>();
                    if (networkedItem.NetId != 0)
                    {
                        networkedItem.SetOwner(player.PlayerId);
                        networkedItem.IsPendingReconciliation = true;
                        player.ReconciledInventoryItems.Add(networkedItem);
                        networkedItem.DeferInitialCreate();
                        created.Add((request, networkedItem));
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Multiplayer.LogError($"Failed to reconcile local item {request.LocalId} ({snapshot?.PrefabName}) for {player.Username}: {ex}");
            }

            if (gameObject != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(gameObject);
                }
                catch (Exception ex)
                {
                    Multiplayer.LogWarning($"Failed to clean up rejected reconciliation item {request.LocalId}: {ex.Message}");
                }
            }

            response.Items.Add(new ItemReconciliationResult(request.LocalId, 0, null));
            player.InventoryReconciliationFailed = true;
        }

        // GadgetItem assigns Item in Start; applying a carried snapshot in the
        // instantiation frame disables its GameObject before Start can ever run.
        // Initialize the whole batch together, including container registries.
        if (created.Count > 0)
            yield return null;

        if (!NetworkLifecycle.Instance.IsHost() ||
            !NetworkLifecycle.Instance.Server.TryGetServerPlayer(player.PlayerId, out var connectedPlayer) ||
            connectedPlayer != player)
        {
            foreach (var entry in created)
                if (entry.Item != null)
                {
                    ShopPurchaseCoordinator.SuppressStorageTransitionRestock(entry.Item.gameObject);
                    UnityEngine.Object.Destroy(entry.Item.gameObject);
                }
            yield break;
        }

        foreach (var entry in created)
        {
            var request = entry.Request;
            var item = entry.Item;
            try
            {
                if (item == null || !item.IsReadyForSnapshots ||
                    item.GetTrackedItem<DV.Customization.Gadgets.GadgetItem>() is { Item: null })
                    throw new InvalidOperationException("Native item initialization did not complete");

                var snapshot = request.Snapshot;
                snapshot.BelongsToPlayer = request.BelongsToPlayer;
                snapshot.ItemNetId = item.NetId;
                snapshot.Player = player.PlayerId;
                snapshot.OwnerPlayerId = player.PlayerId;
                item.ReceiveSnapshot(snapshot);
                item.IsPendingReconciliation = false;
                player.KnownItems[item] = NetworkLifecycle.Instance.Tick;
                player.NearbyItems[item] = Time.time;
                response.Items.Add(new ItemReconciliationResult(request.LocalId, item.NetId,
                    item.CreateCurrentUpdateData(ItemUpdateData.ItemUpdateType.Create)));
            }
            catch (Exception exception)
            {
                Multiplayer.LogError($"Failed to initialize reconciled item {request.LocalId} for {player.Username}: {exception}");
                if (item != null)
                {
                    ShopPurchaseCoordinator.SuppressStorageTransitionRestock(item.gameObject);
                    UnityEngine.Object.Destroy(item.gameObject);
                }
                response.Items.Add(new ItemReconciliationResult(request.LocalId, 0, null));
                player.InventoryReconciliationFailed = true;
            }
        }

        Multiplayer.LogDebug(() => $"Reconciled {response.Items.Count(item => item.NetId != 0)}/{requests.Length} items for {player.Username}");
        NetworkedItemManager.RestoreContainerMemberships();
        if (isFinalBatch)
        {
            player.InventoryReconciliationComplete = !player.InventoryReconciliationFailed;
            if (player.InventoryReconciliationComplete)
                player.ReconciledInventoryItems.Clear();
            ShopPurchaseCoordinator.RequestStockRecount();
        }
        completed(response);
    }

    private static bool IsAllowedReconciliationState(ItemState state, InventoryItemSpec spec) =>
        NetworkedItem.IsCarriedState(state) ||
        state == ItemState.Dropped && spec.IsEssential;

    private static bool IsReconciliationCandidate(NetworkedItem item)
    {
        if (item?.Item == null || item.NetId != 0 || NetworkedItemManager.IsLocalShopItem(item))
            return false;

        var inventory = Inventory.Instance;
        var storage = StorageController.Instance;
        return item.Item.IsEssential() || item.Item.IsGrabbed() ||
            inventory != null && inventory.Contains(item.gameObject, true) ||
            storage?.StorageInventory != null && storage.StorageInventory.ContainsItem(item.Item) ||
            storage?.StorageItemContainers != null && storage.StorageItemContainers.ContainsItem(item.Item);
    }

}
