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
    private List<ItemUpdateData> DestroyedItems = new List<ItemUpdateData>();

    //Item ownership
    //private Dictionary<ushort, PlayerInventory> playerInventories = new Dictionary<ushort, PlayerInventory>();
    //private Dictionary<NetworkedItem, ushort> itemToPlayerMap = new Dictionary<NetworkedItem, ushort>();


    /*
     * Client
     */

    //cache for client-sided items & spawns
    private Dictionary<string, List<NetworkedItem>> CachedItems = new Dictionary<string, List<NetworkedItem>>(); //Client cached items
    private Dictionary<string, InventoryItemSpec> ItemPrefabs = new Dictionary<string, InventoryItemSpec>();     //Item prefabs
    private bool ClientInitialised = false;


    /* 
     * Common
     */
    private Queue<Tuple<ItemUpdateData, ServerPlayer>> ReceivedSnapshots = new Queue<Tuple<ItemUpdateData, ServerPlayer>>();
    

    protected override void Awake()
    {
        base.Awake();
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        //B99 temporary patch NetworkLifecycle.Instance.Server.PlayerDisconnect += PlayerDisconnected;

        try
        {
            MAX_REACH_DISTANCE = GrabberRaycasterDV.RAYCAST_MAX_DIST + REACH_DISTANCE_BUFFER;
        }
        catch (Exception ex)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"NatworkedItemManager.Awake() Failed to find GrabberRaycasterDV\r\n{ex.Message}");
        }
    }

    private void PlayerDisconnected(uint netID)
    {
        throw new NotImplementedException();
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
    }

    public void AddDirtyItemSnapshot(NetworkedItem netItem, ItemUpdateData snapshot)
    {
        DestroyedItems.Add(snapshot);

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

        Multiplayer.LogDebug(() => $"NetworkItemManager.ReceiveSnapshots() count: {ReceivedSnapshots.Count}, from: ");
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

        List<NetworkedItem> allItems = NetworkedItem.GetAll();

        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (!player.IsLoaded)
                continue;

            foreach (var item in allItems)
            {
                if (item == null)
                {
                    NetworkLifecycle.Instance.Server.LogDebug(() => $"UpdatePlayerItemLists() Null item found in allItems!");
                    continue;
                }

                float sqrDistance = (player.WorldPosition - item.transform.position).sqrMagnitude;

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
            ItemUpdateData snapshot = item.GetSnapshot();
            if (snapshot != null)
                dirtyItems.Add(snapshot);
        }

        //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) DirtyItems: {dirtyItems.Count}");

        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (!player.IsLoaded)
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
                            dirtyUpdate = nearbyItem.CreateUpdateData(ItemUpdateData.ItemUpdateType.FullSync);
                        }
                    }

                    if (dirtyUpdate != null)
                    {
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
            if (ValidatePlayerAction(snapshot, player)) //Ensure the player can do this
            {
                NetworkLifecycle.Instance.Server.LogWarning($"NetworkedItemManager.ProcessReceivedAsHost() ItemNetId: {snapshot.ItemNetId}, snapshot type: {snapshot.UpdateType}");
                netItem.ReceiveSnapshot(snapshot);
            }
            else
            {
                NetworkLifecycle.Instance.Server.LogWarning($"NetworkedItemManager.ProcessReceivedAsHost() Player action validation failed for ItemNetId: {snapshot.ItemNetId}");
            }
        }
        else
        {
            NetworkLifecycle.Instance.Server.LogError($"NetworkedItemManager.ProcessReceivedAsHost() NetworkedItem not found! Update Type: {snapshot.UpdateType}, ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
        }
    }

    private bool ValidatePlayerAction(ItemUpdateData snapshot, ServerPlayer player)
    {
        return true;
        // Must have valid item
        if (!NetworkedItem.TryGet(snapshot.ItemNetId, out NetworkedItem networkedItem))
            return false;

        Multiplayer.LogDebug(() => $"ValidatePlayerAction() ItemId: {snapshot.ItemNetId}, name: {networkedItem.name} Update Type: {snapshot.UpdateType}, Item State: {snapshot.ItemState}, Player: {player.Username}");

        switch (snapshot.ItemState)
        {
            case ItemState.InHand:
            case ItemState.InInventory:
                // Check if someone else owns it
                GetItemOwner(snapshot.ItemNetId, out ServerPlayer currentOwner);
                Multiplayer.LogDebug(() => $"ValidatePlayerAction() ItemId: {snapshot.ItemNetId}, name: {networkedItem.name} Update Type: {snapshot.UpdateType}, Item State: {snapshot.ItemState}, Player: {player?.Username}, Current Owner: {currentOwner?.Username}");

                if (currentOwner != null && currentOwner != player)
                    return false;

                // Check pickup distance
                float distance = Vector3.Distance(player.WorldPosition, networkedItem.transform.position);
                if (distance > MAX_REACH_DISTANCE)
                    return false;

                Multiplayer.LogDebug(() => $"ValidatePlayerAction() ItemId: {snapshot.ItemNetId}, name: {networkedItem.name} Update Type: {snapshot.UpdateType}, Item State: {snapshot.ItemState}, Player: {player.Username}, Distance check: {distance}");
                break;

            case ItemState.Dropped:
            case ItemState.Thrown:
            case ItemState.Attached: //needs additional checks for distance to coupler
                // Only owner can drop/throw
                if (!player.OwnsItem(snapshot.ItemNetId))
                    return false;
                break;
        }

        return true;
    }

    private bool GetItemOwner(ushort itemNetId, out ServerPlayer owner)
    {
        owner = NetworkLifecycle.Instance.Server.ServerPlayers.FirstOrDefault(p => p.OwnsItem(itemNetId));
        return owner != null;
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

        newItem.ReceiveSnapshot(snapshot);
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
        //B99 temporary patch
        return;
        if (NetworkLifecycle.Instance.IsHost())
            return;

        // Remove all spawned world items and place them into a cache for later use
        var items = NetworkedItem.GetAll().ToList();
        foreach (var item in items)
        {
            try
            {
                if (item.Item != null && !item.Item.IsEssential() && !item.Item.IsGrabbed() && !StorageController.Instance.StorageInventory.ContainsItem(item.Item))
                {
                    SendToCache(item);
                }
                else
                {
                    NetworkLifecycle.Instance.Client.LogDebug(() => $"CacheWorldItems() Not caching: {item.Item.InventorySpecs.previewPrefab} is in Inventory: {StorageController.Instance.StorageInventory.ContainsItem(item.Item)}");
                }
            }
            catch (Exception ex)
            {
                NetworkLifecycle.Instance.Client.LogDebug(() => $"Error Caching Spawned Item: {ex.Message}");
            }
        }

        ClientInitialised = true;
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

        NetworkLifecycle.Instance.Client.LogDebug(() => $"Caching Spawned Item: {prefabName ?? ""}");

        netItem.gameObject.SetActive(false);
        RespawnOnDrop respawn = netItem.Item.GetComponent<RespawnOnDrop>();

        Destroy(respawn);

        //NetworkLifecycle.Instance.Client.LogDebug(() => $"Caching Spawned Item: {prefabName ?? ""}: checkWhileDisabled {respawn.checkWhileDisabled}, ignoreDistanceFromSpawnPosition {respawn.ignoreDistanceFromSpawnPosition}, respawnOnDropThroughFloor {respawn.respawnOnDropThroughFloor}");

        //respawn.checkWhileDisabled = false;
        //respawn.ignoreDistanceFromSpawnPosition = true;
        //respawn.respawnOnDropThroughFloor = false;

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

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedItemManager)}]";
    }
}
