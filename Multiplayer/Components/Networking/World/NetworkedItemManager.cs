using System.Collections.Generic;
using System.Linq;
using DV.Utils;
using UnityEngine;
using JetBrains.Annotations;
using Multiplayer.Networking.Data;
using Multiplayer.Components.Networking.World;
using System;
using Multiplayer.Utils;
using DV;
using DV.Interaction;
using DV.InventorySystem;
using DV.Shops;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.Packets;
using System.Collections;
using Multiplayer.Components.SaveGame;
using Newtonsoft.Json.Linq;
using DV.Common;

namespace Multiplayer.Components.Networking.World;

public class NetworkedItemManager : SingletonBehaviour<NetworkedItemManager>
{
    /*
     * Server 
     */

    //Culling distance for items
    public const float MAX_DISTANCE_TO_ITEM = 100f;
    public const float MAX_DISTANCE_TO_ITEM_SQR = MAX_DISTANCE_TO_ITEM * MAX_DISTANCE_TO_ITEM;
    public const float NEARBY_REMOVAL_DELAY = 3f; // 3 seconds delay
    public const float REACH_DISTANCE_BUFFER = 0.5f;
    public float MAX_REACH_DISTANCE = 4f + REACH_DISTANCE_BUFFER;         //from the game, but we should try to look up the value

    //caches for item snapshots
    private List<ItemUpdateData> DestroyedItems = new(64);
    private readonly List<ItemUpdateData> AcceptedClientUpdates = new(64);

    /*
     * Client
     */

    //cache for client-sided items & spawns
    private Dictionary<string, List<NetworkedItem>> CachedItems = new(1024); //Client cached items
    private Dictionary<string, InventoryItemSpec> ItemPrefabs = new(1024);   //Item prefabs
    internal IReadOnlyDictionary<string, InventoryItemSpec> InventoryPrefabs => ItemPrefabs;
    internal bool ClientInitialised { get; set; }
    private readonly Dictionary<NetworkedItem, Queue<ItemUpdateData>> pendingGadgetCreates = [];


    /* 
     * Common
     */
    private Queue<Tuple<ItemUpdateData, ServerPlayer>> ReceivedSnapshots = new(64);

    protected override void Awake()
    {
        base.Awake();
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        NetworkLifecycle.Instance.Server.PlayerDisconnected += PlayerDisconnected;

        try
        {
            MAX_REACH_DISTANCE = GrabberRaycasterDV.RAYCAST_MAX_DIST + REACH_DISTANCE_BUFFER;
        }
        catch (Exception ex)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"NatworkedItemManager.Awake() Failed to find GrabberRaycasterDV\r\n{ex.Message}");
        }
    }

    private InventorySynchronization inventorySynchronization;
    private InventorySynchronization InventorySynchronization => inventorySynchronization ??= new(this);

    private void PlayerDisconnected(ServerPlayer player) => InventorySynchronization.PlayerDisconnected(player);

    public bool TryCaptureCarriedInventory(ServerPlayer player, out IReadOnlyList<PlayerItemSaveData> items) =>
        InventorySynchronization.TryCaptureCarriedInventory(player, out items);

    public IEnumerator RecoverStoredInventory(SavedPlayerInventory stored, Action<bool, int, int, string> completed) =>
        InventorySynchronization.RecoverStoredInventory(stored, completed);

    protected void Start()
    {
        NetworkLifecycle.Instance.OnTick += Common_OnTick;

        BuildPrefabLookup();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= Common_OnTick;
        if (NetworkLifecycle.Instance.Server != null)
            NetworkLifecycle.Instance.Server.PlayerDisconnected -= PlayerDisconnected;
    }

    public void AddDirtyItemSnapshot(NetworkedItem netItem, ItemUpdateData snapshot)
    {
        DestroyedItems.Add(snapshot);

        ForgetItem(netItem);
    }

    public void ForgetItem(NetworkedItem netItem)
    {

        foreach(var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if(player.KnownItems.ContainsKey(netItem))
                player.KnownItems.Remove(netItem);

            if(player.NearbyItems.ContainsKey(netItem))
                player.NearbyItems.Remove(netItem);
        }
    }

    public void ReceiveSnapshots(List<ItemUpdateData> snapshots, ServerPlayer sender)
    {
        if (snapshots == null)
            return;

        foreach (var snapshot in snapshots)
        {
            ReceivedSnapshots.Enqueue(new (snapshot, sender));
        }

        //Multiplayer.LogDebug(() => $"NetworkItemManager.ReceiveSnapshots() count: {ReceivedSnapshots.Count}, from: ");
    }

    #region Common

    private void Common_OnTick(uint tick)
    {
        ProcessReceived();
        RestoreContainerMemberships();

        if (NetworkLifecycle.Instance.IsHost())
        {
            UpdatePlayerItemLists();
            ProcessChanged(tick);
        }
        else
        {
            ProcessClientChanges(tick);
        }
    }

    private void ProcessReceived()
    {
        while (ReceivedSnapshots.Count > 0)
        {
            var snapshotInfo = ReceivedSnapshots.Dequeue();
            ItemUpdateData snapshot = snapshotInfo.Item1;
            try
            {
                //Multiplayer.LogDebug(() => $"ProcessReceived: {snapshot.UpdateType}");

                if (snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.None)
                {
                    Multiplayer.LogError($"NetworkedItemManager.ProcessReceived() Invalid Update Type: {snapshot?.UpdateType}, ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
                    continue;
                }

                if (NetworkLifecycle.Instance.IsHost())
                {
                    ProcessReceivedAsHost(snapshot, snapshotInfo.Item2);
                }
                else
                {
                    ProcessReceivedAsClient(snapshot);
                }
            }
            catch (Exception ex)
            {
                Multiplayer.LogError($"NetworkedItemManager.ProcessReceived() Error! {ex.Message}\r\n{ex.StackTrace}");
            }
        }
    }

    #endregion

    #region Server

    private void UpdatePlayerItemLists()
    {
        float currentTime = Time.time;

        var allItems = NetworkedItem.GetAll();

        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            foreach (var item in allItems)
            {
                if (item == null || item.IsPendingReconciliation)
                    continue;

                if (IsLocalShopItem(item))
                    continue;

                float sqrDistance = (player.WorldPosition - item.WorldPosition).sqrMagnitude;

                if (sqrDistance <= MAX_DISTANCE_TO_ITEM_SQR)
                {
                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"UpdatePlayerItemLists() Adding for player: {player?.Username}, Nearby Item: {item?.NetId}, {item?.name}");
                    player.NearbyItems[item] = currentTime;
                }
            }

            // Remove items that are no longer nearby
            for (int i = 0; i < player.NearbyItems.Count; i++)
            {
                var kvp = player.NearbyItems.ElementAt(i);

                if (currentTime - kvp.Value > NEARBY_REMOVAL_DELAY)
                {
                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"UpdatePlayerItemLists() Removing for player: {player?.Username}, Nearby Item: {kvp.Key?.NetId}, {kvp.Key?.name}");
                    player.NearbyItems.Remove(kvp.Key);
                }
            }
        }
    }

    private void ProcessChanged(uint tick)
    {
        List<ItemUpdateData> dirtyItems = new List<ItemUpdateData>();
        float timeStamp = Time.time;

        foreach (var item in NetworkedItem.GetAll())
        {
            if (item == null || item.NetId == 0 || IsLocalShopItem(item))
                continue;

            ItemUpdateData snapshot = item.GetSnapshot();
            if (snapshot != null)
                dirtyItems.Add(snapshot);
        }

        // Each packet is a delta: later packets need not contain earlier state or
        // tracked-value changes. Replay all accepted deltas before any subsequent
        // native changes observed on the host.
        dirtyItems.InsertRange(0, AcceptedClientUpdates);

        //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) DirtyItems: {dirtyItems.Count}");

        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            List<ItemUpdateData> playerUpdates = new List<ItemUpdateData>();

            // Process nearby items
            foreach (var nearbyItem in player.NearbyItems.Keys)
            {
                if (!player.KnownItems.ContainsKey(nearbyItem))
                {
                    // This is a new item for the player
                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) New item for: {player.Username}, itemNetID{nearbyItem.NetId}");

                    ItemUpdateData snapshot = nearbyItem.CreateUpdateData(ItemUpdateData.ItemUpdateType.Create);
                    player.KnownItems[nearbyItem] = tick;

                    //prevent propagation of creates for special items
                    if(!DoNotCreateItem(nearbyItem.GetType()))
                        playerUpdates.Add(snapshot);
                }
                else
                {
                    // Check if this item is in the dirty items list
                    var itemUpdates = dirtyItems.Where(di => di.ItemNetId == nearbyItem.NetId).ToList();

                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Item exists for: {player.Username}, {dirtyUpdate != null}");

                    if (itemUpdates.Count == 0)
                    {
                        //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Item exists for: {player.Username}, LastDirtyTick: {player.KnownItems[nearbyItem] < nearbyItem.LastDirtyTick}");
                        if (player.KnownItems[nearbyItem] < nearbyItem.LastDirtyTick)
                        {
                            itemUpdates.Add(nearbyItem.CreateUpdateData(
                                ItemUpdateData.ItemUpdateType.FullSync |
                                ItemUpdateData.ItemUpdateType.Ownership));
                        }
                    }

                    foreach (var dirtyUpdate in itemUpdates)
                    {
                        // The originating client already applied its own inventory/hand
                        // transition locally. Echoing it back would hide the real item
                        // currently attached to that client's hand.
                        if (dirtyUpdate.Player != 0 && dirtyUpdate.Player == player.PlayerId)
                        {
                            player.KnownItems[nearbyItem] = tick;
                            continue;
                        }

                        Multiplayer.LogDebug(() => $"ProcessChanged({tick}) Update Type: {dirtyUpdate.UpdateType}, Item State: {dirtyUpdate.ItemState}");
                        playerUpdates.Add(dirtyUpdate);
                        player.KnownItems[nearbyItem] = tick;
                    }
                }
            }

            //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Adding {DestroyedItems.Count()} DestroyedItems for: {player.Username}");

            playerUpdates.AddRange(DestroyedItems);

            if (playerUpdates.Count > 0)
            {
                //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Sending {playerUpdates.Count()} to player: {player.Username}");
                NetworkLifecycle.Instance.Server.SendItemsChangePacket(playerUpdates, player);
            }
        }

        DestroyedItems.Clear();
        AcceptedClientUpdates.Clear();
    }

    private void ProcessReceivedAsHost(ItemUpdateData snapshot, ServerPlayer player)
    {
        if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create)
        {
            NetworkLifecycle.Instance.Server.LogError($"NetworkedItemManager.ProcessReceivedAsHost() Host received Create snapshot! ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
            return;
        }

        if (NetworkedItem.TryGet(snapshot.ItemNetId, out NetworkedItem netItem))
        {
            // A remote hand/inventory proxy exists outside the observing client's
            // real Inventory and can therefore look dropped at the hidden parking
            // coordinates. Only the current owner may transition a held item to a
            // dropped/thrown state. This still permits ordinary world-item pickup
            // and the owning player's genuine drop.
            bool changesItemState = snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ItemState);
            bool dropsHeldItem = NetworkedItem.IsCarriedState(netItem.CurrentState) &&
                snapshot.ItemState is ItemState.Dropped or ItemState.Thrown;
            bool belongsToAnotherPlayer = netItem.OwnerPlayerId != 0 &&
                netItem.OwnerPlayerId != player.PlayerId;
            if (changesItemState && dropsHeldItem && belongsToAnotherPlayer)
            {
                NetworkLifecycle.Instance.Server.LogWarning(
                    $"NetworkedItemManager.ProcessReceivedAsHost() Ignoring non-owner drop for item " +
                    $"{snapshot.ItemNetId}: sender={player.PlayerId}, owner={netItem.OwnerPlayerId}, " +
                    $"state={snapshot.ItemState}");
                return;
            }

            // Clients are authoritative for their item interactions. The host may not
            // have the sender's area loaded, so its physical item position is not a
            // reliable basis for ownership or reach validation.
            snapshot.Player = player.PlayerId;
            snapshot.OwnerPlayerId = NetworkedItem.IsCarriedState(snapshot.ItemState)
                ? player.PlayerId
                : netItem.OwnerPlayerId;
            NetworkLifecycle.Instance.Server.LogWarning($"NetworkedItemManager.ProcessReceivedAsHost() ItemNetId: {snapshot.ItemNetId}, snapshot type: {snapshot.UpdateType}");
            netItem.ReceiveSnapshot(snapshot);
            netItem.MarkRemoteUpdateAccepted();
            AcceptedClientUpdates.Add(snapshot);
        }
        else
        {
            NetworkLifecycle.Instance.Server.LogError($"NetworkedItemManager.ProcessReceivedAsHost() NetworkedItem not found! Update Type: {snapshot.UpdateType}, ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
        }
    }

    #endregion

    #region Client

    private void ProcessClientChanges(uint tick)
    {
        List<ItemUpdateData> changedItems = new List<ItemUpdateData>();

        if(!ClientInitialised)
            return;

        foreach (var item in NetworkedItem.GetAll())
        {
            if (item == null || item.NetId == 0 || IsLocalShopItem(item))
                continue;

            ItemUpdateData snapshot = item.GetSnapshot();
            if (snapshot != null)
            {
                changedItems.Add(snapshot);
            }
        }

        if (changedItems.Count > 0)
        {
            NetworkLifecycle.Instance.Client.SendItemsChangePacket(changedItems);
        }
    }

    private void ProcessReceivedAsClient(ItemUpdateData snapshot)
    {
        NetworkedItem.TryGet(snapshot.ItemNetId, out NetworkedItem netItem);

        NetworkLifecycle.Instance.Client.LogDebug(() => $"NetworkedItemManager.ProcessReceivedAsClient() Update Type: {snapshot?.UpdateType}, ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
        if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create)
        {
            //if the item already exists we need to remove it
            if (netItem != null)
                SendToCache(netItem);

            CreateItem(snapshot);
        }
        else if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Destroy)
        {
            if (netItem != null)
                SendToCache(netItem);
        }
        else if (netItem != null)
        {
            if (pendingGadgetCreates.TryGetValue(netItem, out var pending))
                pending.Enqueue(snapshot);
            else
                netItem.ReceiveSnapshot(snapshot);
        }
        else
        {
            NetworkLifecycle.Instance.Client.LogError($"NetworkedItemManager.ProcessReceivedAsClient() NetworkedItem not found on client! Update Type: {snapshot.UpdateType}, ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
        }
    }
    #endregion

    #region Item Cache And Management
    internal void CreateItem(ItemUpdateData snapshot)
    {
        if(snapshot == null || snapshot.ItemNetId == 0)
        {
            Multiplayer.LogError($"NetworkedItemManager.CreateItem() Invalid snapshot! ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
            return;
        }

        // Streamed locations instantiate their scene-authored items on every peer.
        // Those items can appear after the one-time CacheWorldItems pass. Reuse the
        // matching unassigned client item instead of adding a network-created copy
        // on top of it.
        NetworkedItem newItem = FindMatchingLocalWorldItem(snapshot) ?? GetFromCache(snapshot.PrefabName);

        if(newItem == null)
        {
            //GameObject prefabObj = Resources.Load(snapshot.PrefabName) as GameObject;
            
            if (!ItemPrefabs.TryGetValue(snapshot.PrefabName, out InventoryItemSpec spec))
            {
                Multiplayer.LogError($"NetworkedItemManager.CreateItem() Unable to load prefab for ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
                return;
            }

            //create a new item
            GameObject gameObject = Instantiate(spec.gameObject, snapshot.ItemPosition + WorldMover.currentMove, snapshot.ItemRotation);

            //Make sure we have a NetworkedItem
            newItem = gameObject.GetOrAddComponent<NetworkedItem>();
        }

        newItem.gameObject.SetActive(true);
        newItem.NetId = snapshot.ItemNetId;

        // Installed and carried snapshots deactivate the physical item object.
        // Let every new GadgetItem finish Start before applying its initial state,
        // including remote inventory proxies later used in placement actions.
        if (newItem.GetTrackedItem<DV.Customization.Gadgets.GadgetItem>() is { Item: null })
        {
            var pending = new Queue<ItemUpdateData>();
            pending.Enqueue(snapshot);
            pendingGadgetCreates[newItem] = pending;
            newItem.IsPendingInitialization = true;
            CoroutineManager.Instance.StartCoroutine(ApplyGadgetCreateAfterStart(newItem, snapshot, pending));
            return;
        }

        newItem.ReceiveSnapshot(snapshot);
    }

    private static NetworkedItem FindMatchingLocalWorldItem(ItemUpdateData snapshot)
    {
        const float maxMatchDistance = 1f;
        float maxMatchDistanceSqr = maxMatchDistance * maxMatchDistance;
        Vector3 authoritativePosition = snapshot.ItemPosition + WorldMover.currentMove;
        NetworkedItem closest = null;
        float closestDistanceSqr = maxMatchDistanceSqr;

        foreach (var candidate in NetworkedItem.GetAll())
        {
            if (candidate?.Item?.InventorySpecs == null || candidate.NetId != 0 ||
                IsLocalShopItem(candidate) || candidate.Item.IsEssential() || candidate.Item.IsGrabbed() ||
                !string.Equals(candidate.Item.InventorySpecs.ItemPrefabName, snapshot.PrefabName,
                    StringComparison.Ordinal))
                continue;

            var inventory = Inventory.Instance;
            var storage = StorageController.Instance;
            if (inventory != null && inventory.Contains(candidate.gameObject, true) ||
                storage?.StorageInventory != null && storage.StorageInventory.ContainsItem(candidate.Item))
                continue;

            float distanceSqr = (candidate.transform.position - authoritativePosition).sqrMagnitude;
            if (distanceSqr > closestDistanceSqr)
                continue;

            closest = candidate;
            closestDistanceSqr = distanceSqr;
        }

        if (closest != null)
            Multiplayer.LogDebug(() =>
                $"Adopting local world item {closest.name} for authoritative NetId {snapshot.ItemNetId}");

        return closest;
    }

    internal void CacheLateLocalWorldDuplicate(NetworkedItem localItem)
    {
        const float duplicatePositionTolerance = 0.05f;

        if (!ClientInitialised || NetworkLifecycle.Instance.IsHost() ||
            localItem?.Item?.InventorySpecs == null || localItem.NetId != 0 ||
            IsLocalShopItem(localItem) || localItem.Item.IsEssential() || localItem.Item.IsGrabbed())
            return;

        var inventory = Inventory.Instance;
        var storage = StorageController.Instance;
        if (inventory != null && inventory.Contains(localItem.gameObject, true) ||
            storage?.StorageInventory != null && storage.StorageInventory.ContainsItem(localItem.Item))
            return;

        string prefabName = localItem.Item.InventorySpecs.ItemPrefabName;
        float toleranceSqr = duplicatePositionTolerance * duplicatePositionTolerance;
        bool hasAuthoritativeCounterpart = NetworkedItem.GetAll().Any(candidate =>
            candidate != null && candidate != localItem && candidate.NetId != 0 &&
            candidate.Item?.InventorySpecs != null &&
            string.Equals(candidate.Item.InventorySpecs.ItemPrefabName, prefabName, StringComparison.Ordinal) &&
            (candidate.transform.position - localItem.transform.position).sqrMagnitude <= toleranceSqr);

        if (!hasAuthoritativeCounterpart)
            return;

        Multiplayer.LogDebug(() =>
            $"Caching late local duplicate {localItem.name}; authoritative counterpart already exists");
        SendToCache(localItem);
    }

    private IEnumerator ApplyGadgetCreateAfterStart(NetworkedItem item, ItemUpdateData snapshot,
        Queue<ItemUpdateData> pending)
    {
        const int maxInitializationFrames = 120;
        int frames = 0;
        var gadgetItem = item?.GetTrackedItem<DV.Customization.Gadgets.GadgetItem>();

        while (item != null && item.NetId == snapshot.ItemNetId &&
               pendingGadgetCreates.TryGetValue(item, out var current) && ReferenceEquals(current, pending) &&
               gadgetItem?.Item == null &&
               frames++ < maxInitializationFrames)
            yield return null;

        if (item == null || item.NetId != snapshot.ItemNetId ||
            !pendingGadgetCreates.TryGetValue(item, out var active) || !ReferenceEquals(active, pending))
            yield break;

        if (gadgetItem?.Item == null)
        {
            Multiplayer.LogError($"Gadget {snapshot.ItemNetId} ({snapshot.PrefabName}) did not initialize; create snapshot was not applied");
            SendToCache(item);
            yield break;
        }

        // Start has now run. Apply Create first and every subsequent delta in
        // arrival order, before allowing snapshots or customization actions.
        try
        {
            while (pending.Count > 0)
                item.ReceiveSnapshot(pending.Dequeue());
        }
        finally
        {
            pendingGadgetCreates.Remove(item);
            item.IsPendingInitialization = false;
        }
    }

    public void SendInitialItems(ServerPlayer player, IEnumerable<ushort> itemNetIds,
        ISet<ushort> forceDropped = null)
    {
        if (player == null || itemNetIds == null || !NetworkLifecycle.Instance.IsHost())
            return;

        var creates = new List<ItemUpdateData>();
        uint tick = NetworkLifecycle.Instance.Tick;
        foreach (ushort itemNetId in itemNetIds.Distinct())
        {
            if (!NetworkedItem.TryGet(itemNetId, out var item) || player.KnownItems.ContainsKey(item))
                continue;

            var snapshot = item.CreateCurrentUpdateData(ItemUpdateData.ItemUpdateType.Create);
            if (snapshot == null)
                continue;

            if (forceDropped?.Contains(itemNetId) == true)
            {
                snapshot.ItemState = ItemState.Dropped;
                snapshot.ItemPosition = item.transform.position - WorldMover.currentMove;
                snapshot.ItemRotation = item.transform.rotation;
            }

            creates.Add(snapshot);
            player.KnownItems[item] = tick;
        }

        if (creates.Count > 0)
            NetworkLifecycle.Instance.Server.SendItemsChangePacket(creates, player);
    }

    private void BuildPrefabLookup()
    {
        NetworkLifecycle.Instance.Client.LogDebug(() => $"BuildPrefabLookup()");

        foreach (var item in Globals.G.Items.items)
        {
            if (!ItemPrefabs.ContainsKey(item.ItemPrefabName))
            {
                ItemPrefabs[item.itemPrefabName] = item;
            }
        }
    }
    public void CacheWorldItems()
    {
        if (NetworkLifecycle.Instance.IsHost())
            return;

        // Remove all spawned world items and place them into a cache for later use
        foreach (var item in NetworkedItem.GetAll())
        {
            try
            {
                if (item.Item != null && !IsLocalShopItem(item) && !item.Item.IsEssential() &&
                    !item.Item.IsGrabbed() &&
                    !StorageController.Instance.StorageInventory.ContainsItem(item.Item) &&
                    !StorageController.Instance.StorageItemContainers.ContainsItem(item.Item))
                {
                    SendToCache(item);
                }
                //else
                //{
                //    NetworkLifecycle.Instance.Client.LogDebug(() => $"CacheWorldItems() Not caching: {item.Item.InventorySpecs.previewPrefab} is in Inventory: {StorageController.Instance.StorageInventory.ContainsItem(item.Item)}");
                //}
            }
            catch (Exception ex)
            {
                NetworkLifecycle.Instance.Client.LogError($"Error Caching Spawned Item: {ex.Message}");
            }
        }

        ClientInitialised = false;
    }

    internal IReadOnlyList<ServerboundItemReconciliationPacket> CreateItemReconciliationRequests() =>
        InventorySynchronization.CreateItemReconciliationRequests();

    internal bool ApplyItemReconciliation(ClientboundItemReconciliationPacket packet) =>
        InventorySynchronization.ApplyItemReconciliation(packet);

    internal void ContinueAfterItemReconciliationTimeout() =>
        InventorySynchronization.ContinueAfterItemReconciliationTimeout();

    internal void EnqueueClientItemReconciliation(ItemReconciliationRequest[] requests, bool isFinalBatch,
        ServerPlayer player, Action<ClientboundItemReconciliationPacket> completed) =>
        InventorySynchronization.EnqueueClientItemReconciliation(requests, isFinalBatch, player, completed);

    internal static void RestoreContainerMemberships()
    {
        foreach (var item in NetworkedItem.GetAll().ToArray())
        {
            if (item == null)
                continue;
            try
            {
                item.RestoreContainerMembership();
            }
            catch (Exception exception)
            {
                Multiplayer.LogError($"Could not restore container membership for item {item.NetId}: {exception}");
            }
        }
    }

    private NetworkedItem GetFromCache(string prefabName)
    {
        if (CachedItems.TryGetValue(prefabName, out var items) && items.Count > 0)
        {

            var cachedItem = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            return cachedItem;
        }

        return null;
    }

    internal void SendToCache(NetworkedItem netItem)
    {
        // Cancel the old creation generation even if this object is immediately
        // reused for another Create with the same network ID.
        pendingGadgetCreates.Remove(netItem);
        netItem.IsPendingInitialization = false;
        netItem.DetachFromContainer();
        string prefabName = netItem?.Item?.InventorySpecs?.itemPrefabName;

        //NetworkLifecycle.Instance.Client.LogDebug(() => $"Caching Spawned Item: {prefabName ?? ""}");

        netItem.gameObject.SetActive(false);
        if (SingletonBehaviour<StorageController>.Instance.StorageWorld.ContainsItem(netItem.Item))
        {
            SingletonBehaviour<StorageController>.Instance.RemoveItemFromWorldStorage(netItem.Item);
        }

        if (SingletonBehaviour<StorageController>.Instance.StorageInventory.ContainsItem(netItem.Item))
        {
            SingletonBehaviour<StorageController>.Instance.RemoveItemFromStorageItemList(netItem.Item);
        }

        if (SingletonBehaviour<StorageController>.Instance.StorageLostAndFound.ContainsItem(netItem.Item))
        {
            SingletonBehaviour<StorageController>.Instance.RemoveItemFromStorageItemList(netItem.Item);
        }

        if (SingletonBehaviour<StorageController>.Instance.StorageItemContainers.ContainsItem(netItem.Item))
        {
            SingletonBehaviour<StorageController>.Instance.RemoveItemFromStorageItemList(netItem.Item);
        }

        netItem.Item.InventorySpecs.BelongsToPlayer = false;
        netItem.NetId = 0;
        
        if (!CachedItems.ContainsKey(prefabName))
        {
            CachedItems[prefabName] = new List<NetworkedItem>();
        }
        CachedItems[prefabName].Add(netItem);
    }

    #endregion

    public bool DoNotCreateItem(Type itemType)
    {
        if (
            itemType == typeof(JobOverview) ||
            itemType == typeof(JobBooklet) ||
            itemType == typeof(JobReport) ||
            itemType == typeof(JobExpiredReport) ||
            itemType == typeof(JobMissingLicenseReport)
           )
        {
            return true;
        }

            return false;
    }

    internal static bool IsLocalShopItem(NetworkedItem item) =>
        item != null && item.TryGetComponent<ShopScanner>(out _);

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedItemManager)}]";
    }
}
