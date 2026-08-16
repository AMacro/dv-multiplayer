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
    private Dictionary<ushort, ItemUpdateData> AcceptedClientUpdates = new(64);

    /*
     * Client
     */

    //cache for client-sided items & spawns
    private Dictionary<string, List<NetworkedItem>> CachedItems = new(1024); //Client cached items
    private Dictionary<string, InventoryItemSpec> ItemPrefabs = new(1024);   //Item prefabs
    private bool ClientInitialised = false;
    private readonly Dictionary<int, NetworkedItem> pendingItemReconciliation = [];


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

    private void PlayerDisconnected(ServerPlayer disconnectedPlayer)
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

        int transferredItems = 0;
        foreach (var item in NetworkedItem.GetAll())
        {
            if (item == null || item.OwnerPlayerId != disconnectedPlayer.PlayerId)
                continue;

            item.SetOwner(hostPlayerId, true);
            transferredItems++;
        }

        Multiplayer.LogDebug(() =>
            $"Transferred {transferredItems} items from disconnected player " +
            $"{disconnectedPlayer.PlayerId} to host player {hostPlayerId}");
    }

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
                if (item == null)
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

        // Applying a client snapshot marks the host's tracked values clean. Preserve the
        // accepted wire representation so it can still be forwarded to the other clients.
        foreach (var acceptedUpdate in AcceptedClientUpdates.Values)
        {
            int existingIndex = dirtyItems.FindIndex(item => item.ItemNetId == acceptedUpdate.ItemNetId);
            if (existingIndex >= 0)
                dirtyItems[existingIndex] = acceptedUpdate;
            else
                dirtyItems.Add(acceptedUpdate);
        }

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
                    var dirtyUpdate = dirtyItems.FirstOrDefault(di => di.ItemNetId == nearbyItem.NetId);

                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Item exists for: {player.Username}, {dirtyUpdate != null}");

                    if (dirtyUpdate == null)
                    {
                        //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Item exists for: {player.Username}, LastDirtyTick: {player.KnownItems[nearbyItem] < nearbyItem.LastDirtyTick}");
                        if (player.KnownItems[nearbyItem] < nearbyItem.LastDirtyTick)
                        {
                            dirtyUpdate = nearbyItem.CreateUpdateData(
                                ItemUpdateData.ItemUpdateType.FullSync |
                                ItemUpdateData.ItemUpdateType.Ownership);
                        }
                    }

                    if (dirtyUpdate != null)
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
            // Clients are authoritative for their item interactions. The host may not
            // have the sender's area loaded, so its physical item position is not a
            // reliable basis for ownership or reach validation.
            snapshot.Player = player.PlayerId;
            snapshot.OwnerPlayerId = snapshot.ItemState is ItemState.InHand or ItemState.InInventory
                ? player.PlayerId
                : netItem.OwnerPlayerId;
            NetworkLifecycle.Instance.Server.LogWarning($"NetworkedItemManager.ProcessReceivedAsHost() ItemNetId: {snapshot.ItemNetId}, snapshot type: {snapshot.UpdateType}");
            netItem.ReceiveSnapshot(snapshot);
            netItem.MarkRemoteUpdateAccepted();
            AcceptedClientUpdates[snapshot.ItemNetId] = snapshot;
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
            netItem.ReceiveSnapshot(snapshot);
        }
        else
        {
            NetworkLifecycle.Instance.Client.LogError($"NetworkedItemManager.ProcessReceivedAsClient() NetworkedItem not found on client! Update Type: {snapshot.UpdateType}, ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
        }
    }
    #endregion

    #region Item Cache And Management
    private void CreateItem(ItemUpdateData snapshot)
    {
        if(snapshot == null || snapshot.ItemNetId == 0)
        {
            Multiplayer.LogError($"NetworkedItemManager.CreateItem() Invalid snapshot! ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
            return;
        }

        NetworkedItem newItem = GetFromCache(snapshot.PrefabName);

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

        // Applying InstalledGadget immediately deactivates the physical item object.
        // A freshly instantiated/cached GadgetItem has only run Awake at this point,
        // so doing that here prevents Unity from ever calling GadgetItem.Start and
        // leaves GadgetItem.Item null during customization reconstruction.
        if (snapshot.ItemState == ItemState.InstalledGadget &&
            newItem.GetTrackedItem<DV.Customization.Gadgets.GadgetItem>() is { Item: null })
        {
            CoroutineManager.Instance.StartCoroutine(ApplyInstalledGadgetCreateAfterStart(newItem, snapshot));
            return;
        }

        newItem.ReceiveSnapshot(snapshot);
    }

    private static IEnumerator ApplyInstalledGadgetCreateAfterStart(NetworkedItem item, ItemUpdateData snapshot)
    {
        const int maxInitializationFrames = 120;
        int frames = 0;
        var gadgetItem = item?.GetTrackedItem<DV.Customization.Gadgets.GadgetItem>();

        while (item != null && item.NetId == snapshot.ItemNetId && gadgetItem?.Item == null &&
               frames++ < maxInitializationFrames)
            yield return null;

        if (item == null || item.NetId != snapshot.ItemNetId)
            yield break;

        if (gadgetItem?.Item == null)
        {
            Multiplayer.LogError($"Installed gadget {snapshot.ItemNetId} ({snapshot.PrefabName}) did not initialize; create snapshot was not applied");
            yield break;
        }

        item.ReceiveSnapshot(snapshot);
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
                if (item.Item != null && !IsLocalShopItem(item) && !item.Item.IsEssential() && !item.Item.IsGrabbed() && !StorageController.Instance.StorageInventory.ContainsItem(item.Item))
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
                    CreateItem(result.Snapshot);
                continue;
            }

            if (NetworkedItem.TryGet(result.NetId, out var existingItem) && existingItem != localItem)
                SendToCache(existingItem);

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
        ClientInitialised = true;
        return true;
    }

    internal void ContinueAfterItemReconciliationTimeout()
    {
        // Allow ordinary item updates to resume, but retain the local-ID mapping so
        // a delayed reliable response can still assign authoritative IDs.
        ClientInitialised = true;
    }

    internal ClientboundItemReconciliationPacket ReconcileClientItems(
        ServerboundItemReconciliationPacket packet,
        ServerPlayer player)
    {
        var response = new ClientboundItemReconciliationPacket
        {
            IsFinalBatch = packet?.IsFinalBatch == true,
        };
        if (!NetworkLifecycle.Instance.IsHost() || packet?.Items == null || player == null)
            return response;

        foreach (var request in packet.Items)
        {
            ushort netId = 0;
            ItemUpdateData authoritativeSnapshot = null;
            var snapshot = request.Snapshot;
            GameObject gameObject = null;

            try
            {
                if (snapshot != null && snapshot.ItemNetId == 0 &&
                    !string.IsNullOrEmpty(snapshot.PrefabName) &&
                    ItemPrefabs.TryGetValue(snapshot.PrefabName, out var spec) &&
                    IsAllowedReconciliationState(snapshot.ItemState, spec))
                {
                    gameObject = Instantiate(
                        spec.gameObject,
                        snapshot.ItemPosition + WorldMover.currentMove,
                        snapshot.ItemRotation);
                    var networkedItem = gameObject.GetOrAddComponent<NetworkedItem>();
                    netId = networkedItem.NetId;

                    if (netId != 0)
                    {
                        networkedItem.Item.InventorySpecs.BelongsToPlayer = request.BelongsToPlayer;
                        snapshot.ItemNetId = netId;
                        snapshot.Player = player.PlayerId;
                        snapshot.OwnerPlayerId = player.PlayerId;
                        networkedItem.ReceiveSnapshot(snapshot);

                        player.KnownItems[networkedItem] = NetworkLifecycle.Instance.Tick;
                        player.NearbyItems[networkedItem] = Time.time;
                        authoritativeSnapshot = networkedItem.CreateCurrentUpdateData(
                            ItemUpdateData.ItemUpdateType.Create);
                    }
                }
            }
            catch (Exception ex)
            {
                Multiplayer.LogError($"Failed to reconcile local item {request.LocalId} ({snapshot?.PrefabName}) for {player.Username}: {ex}");
                netId = 0;
            }

            if (netId == 0 && gameObject != null)
            {
                try
                {
                    Destroy(gameObject);
                }
                catch (Exception ex)
                {
                    Multiplayer.LogWarning($"Failed to clean up rejected reconciliation item {request.LocalId}: {ex.Message}");
                }
            }

            response.Items.Add(new ItemReconciliationResult(request.LocalId, netId, authoritativeSnapshot));
        }

        Multiplayer.LogDebug(() => $"Reconciled {response.Items.Count(item => item.NetId != 0)}/{packet.Items.Count} items for {player.Username}");
        return response;
    }

    private static bool IsAllowedReconciliationState(ItemState state, InventoryItemSpec spec) =>
        state is ItemState.InHand or ItemState.InInventory ||
        state == ItemState.Dropped && spec.IsEssential;

    private static bool IsReconciliationCandidate(NetworkedItem item)
    {
        if (item?.Item == null || item.NetId != 0 || IsLocalShopItem(item))
            return false;

        var inventory = Inventory.Instance;
        var storage = StorageController.Instance;
        return item.Item.IsEssential() || item.Item.IsGrabbed() ||
            inventory != null && inventory.Contains(item.gameObject, true) ||
            storage?.StorageInventory != null && storage.StorageInventory.ContainsItem(item.Item);
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

    private void SendToCache(NetworkedItem netItem)
    {
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

    private static bool IsLocalShopItem(NetworkedItem item) =>
        item != null && item.TryGetComponent<ShopScanner>(out _);

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedItemManager)}]";
    }
}
