using DV.CabControls;
using DV.InventorySystem;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

/// <summary>
///     Multiplayer is co-op, so there is a single shared inventory: every player carries the
///     same set of items. Each player holds their own physical copy of each item, so slot
///     placement, which item is in hand, and per-item state (battery charge, coal load, ...)
///     are local. Only the presence of an item is shared: if anyone drops or uses up an item,
///     it leaves everyone's inventory.
///
///     Items lying in the world stay under <see cref="NetworkedItem"/>; this manager owns the
///     handover in both directions (world item -> shared entry on pickup, shared entry -> world
///     item on drop).
/// </summary>
public class SharedInventoryManager : SingletonBehaviour<SharedInventoryManager>
{
    #region Server
    private readonly IdPool<ushort> entryIdPool = new();
    private readonly Dictionary<ushort, string> entries = new(64);

    // Serialized ItemSaveData per entry, so an item keeps its state (battery charge, coal
    // load, wick, ...) as it moves between the world and players' inventories
    private readonly Dictionary<ushort, string> entryStates = new(64);

    public IReadOnlyDictionary<ushort, string> Entries => entries;

    /// <summary>Reads an item's own save state, if it has any.</summary>
    public static string CaptureItemState(GameObject itemGO)
    {
        if (itemGO == null || !itemGO.TryGetComponent(out ItemSaveData saveData))
            return null;

        try
        {
            return saveData.SaveItemData()?.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (System.Exception ex)
        {
            Multiplayer.LogWarning($"Failed to capture item state: {ex.Message}");
            return null;
        }
    }

    /// <summary>Restores an item's own save state onto a freshly created copy.</summary>
    public static void ApplyItemState(GameObject itemGO, string state)
    {
        if (string.IsNullOrEmpty(state) || itemGO == null || !itemGO.TryGetComponent(out ItemSaveData saveData))
            return;

        try
        {
            saveData.LoadItemData(Newtonsoft.Json.Linq.JObject.Parse(state));
        }
        catch (System.Exception ex)
        {
            Multiplayer.LogWarning($"Failed to apply item state: {ex.Message}");
        }
    }

    /// <summary>
    ///     Adds an item to the shared inventory and gives every player a copy. The host is a
    ///     player too, so it applies the change to its own inventory directly rather than
    ///     through the network (it is excluded from its own broadcasts).
    /// </summary>
    public ushort Server_AddEntry(string prefabName, string state = null, ushort originItemNetId = 0)
    {
        ushort entryId = entryIdPool.NextId;
        entries[entryId] = prefabName;
        entryStates[entryId] = state;

        NetworkLifecycle.Instance.Server.SendSharedInventoryChange(entryId, prefabName, true, state, originItemNetId);
        Multiplayer.Log($"Shared inventory + [{entryId}] {prefabName}");

        Client_AddEntry(entryId, prefabName, state, originItemNetId);

        return entryId;
    }

    /// <summary>Removes an item from the shared inventory and from every player.</summary>
    public bool Server_RemoveEntry(ushort entryId)
    {
        if (!entries.TryGetValue(entryId, out string prefabName))
            return false;

        entries.Remove(entryId);
        entryStates.Remove(entryId);
        entryIdPool.ReleaseId(entryId);

        NetworkLifecycle.Instance.Server.SendSharedInventoryChange(entryId, prefabName, false, null);
        Multiplayer.Log($"Shared inventory - [{entryId}] {prefabName}");

        // The host carries a copy as well
        Client_RemoveEntry(entryId);

        return true;
    }

    /// <summary>Converts a world item into a shared inventory entry (someone picked it up).</summary>
    public void Server_StoreWorldItem(ushort itemNetId, ServerPlayer player)
    {
        if (!NetworkedItem.TryGet(itemNetId, out NetworkedItem netItem) || netItem.Item?.InventorySpecs == null)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"{player?.Username} stored item {itemNetId}, but it does not exist");
            return;
        }

        if (IsExcludedFromSharedInventory(netItem.Item))
        {
            NetworkLifecycle.Instance.Server.LogWarning($"Refusing to share \"{netItem.Item.InventorySpecs.ItemPrefabName}\" ({player?.Username}): it is synchronised by its own system");
            return;
        }

        string prefabName = netItem.Item.InventorySpecs.ItemPrefabName;
        string state = CaptureItemState(netItem.gameObject);
        GameObject worldObject = netItem.gameObject;

        // Announce the world item's removal first, while it still has a valid id, so the
        // message cannot be mistaken for anything else. It is delivered after the addition
        // below, by which point whoever adopted the object has cleared its id and will
        // correctly ignore it.
        ItemUpdateData removal = netItem.CreateUpdateData(ItemUpdateData.ItemUpdateType.Destroy);
        if (removal != null)
            NetworkedItemManager.Instance.AddDirtyItemSnapshot(netItem, removal);

        // Give every player a copy. The one already holding this object keeps it, so the
        // item does not get pulled out of their hands the moment they pick it up.
        Server_AddEntry(prefabName, state, itemNetId);

        // Nobody here adopted it, so the world copy is ours to remove
        if (netItem != null && netItem.NetId != 0)
            Destroy(worldObject);
    }

    /// <summary>Removes a shared entry; if it was dropped, spawn it back into the world.</summary>
    public void Server_RemoveFromInventory(ushort entryId, bool dropped, Vector3 position, Quaternion rotation, ServerPlayer player, string state = null)
    {
        if (!entries.TryGetValue(entryId, out string prefabName))
        {
            Multiplayer.LogWarning($"{player?.Username} removed shared item [{entryId}], but no such entry exists (dropped: {dropped})");
            return;
        }

        Multiplayer.Log($"{player?.Username} removed shared item [{entryId}] {prefabName} (dropped: {dropped})");

        // Prefer the state the dropping player reported; fall back to the last known one
        if (string.IsNullOrEmpty(state))
            entryStates.TryGetValue(entryId, out state);

        Server_RemoveEntry(entryId);

        if (dropped)
            NetworkedItemManager.Instance.Server_SpawnRequestedItem(prefabName, position, rotation, player, state);
    }

    public void Server_SendFullInventory(ServerPlayer player)
    {
        ushort[] ids = entries.Keys.ToArray();
        NetworkLifecycle.Instance.Server.SendSharedInventory(
            ids,
            ids.Select(id => entries[id]).ToArray(),
            ids.Select(id => entryStates.TryGetValue(id, out string s) ? s ?? string.Empty : string.Empty).ToArray(),
            player);
    }

    /// <summary>
    ///     Seeds the shared inventory from the host's own inventory when a session starts, so
    ///     joiners receive what the host is already carrying.
    /// </summary>
    public void Server_SeedFromHostInventory()
    {
        if (entries.Count > 0 || Inventory.Instance == null)
            return;

        foreach (GameObject itemGO in Inventory.Instance.GetItemsArray(false))
        {
            if (itemGO == null)
                continue;

            ItemBase item = itemGO.GetComponent<ItemBase>();
            if (item?.InventorySpecs == null || IsExcludedFromSharedInventory(item))
                continue;

            ushort entryId = entryIdPool.NextId;
            entries[entryId] = item.InventorySpecs.ItemPrefabName;
            entryStates[entryId] = CaptureItemState(itemGO);

            // Registering also strips the network id: inventory copies are per-player
            Client_RegisterLocalCopy(entryId, itemGO);
        }

        Multiplayer.Log($"Seeded shared inventory with {entries.Count} items from the host");
    }
    #endregion

    #region Client
    // entryId -> this player's physical copy of that item
    private readonly Dictionary<ushort, GameObject> localCopies = new(64);

    // Same copies as a set, so the per-tick scan can test membership without walking the
    // dictionary once per inventory slot
    private readonly HashSet<GameObject> localCopyObjects = new(64);

    // Reused by the scan so it does not allocate every tick
    private readonly List<ushort> staleEntries = [];

    // Entries whose copy exists but has not been stowed yet. A new copy needs a couple of
    // frames to initialise before it can go into a slot, and during that window it has no
    // slot - which the scan below would otherwise read as the player having dropped it.
    private readonly HashSet<ushort> awaitingStow = [];

    // Upper bound on hands; GetEquippedItemAtSlot returns null beyond the real count
    private const int MAX_EQUIP_SLOTS = 4;

    // Inventory changes are a human-speed event; polling every network tick is wasted work
    private const float POLL_INTERVAL = 0.25f;
    private float nextPoll;

    // Set while applying a server-driven change, so our own inventory polling does not
    // report it straight back to the server as a local action
    private bool applyingRemoteChange;

    // World items we have already asked the server to share, so a refusal (or a slow
    // answer) does not make us re-send the request on every tick
    private readonly HashSet<ushort> pendingStoreRequests = new();


    public void Client_ApplyFullInventory(ushort[] entryIds, string[] prefabNames, string[] states)
    {
        if (entryIds == null || prefabNames == null || entryIds.Length != prefabNames.Length)
            return;

        for (int i = 0; i < entryIds.Length; i++)
            Client_AddEntry(entryIds[i], prefabNames[i], states != null && i < states.Length ? states[i] : null, 0);
    }

    public void Client_AddEntry(ushort entryId, string prefabName, string state, ushort originItemNetId)
    {
        if (localCopies.ContainsKey(entryId))
            return;

        applyingRemoteChange = true;
        try
        {
            // If this player is the one carrying the item this entry came from, keep that
            // exact object. Clearing its network id also makes it ignore the world item's
            // removal that follows.
            GameObject itemGO = null;
            if (originItemNetId != 0
                && NetworkedItem.TryGet(originItemNetId, out NetworkedItem origin)
                && origin.Item != null
                && IsCarriedByLocalPlayer(origin.gameObject))
            {
                itemGO = origin.gameObject;
                origin.NetId = 0;
                pendingStoreRequests.Remove(originItemNetId);
            }

            // The joining player already loaded this inventory through the normal save path,
            // so adopt a matching item that is not yet claimed rather than duplicating it
            itemGO ??= FindUnclaimedInventoryItem(prefabName);

            bool freshCopy = false;

            if (itemGO == null)
            {
                GameObject prefab = NetworkedItemManager.Instance.GetItemPrefab(prefabName);
                if (prefab == null)
                {
                    Multiplayer.LogError($"SharedInventoryManager.Client_AddEntry() Unknown prefab \"{prefabName}\" for entry {entryId}");
                    return;
                }

                // Spawn next to the player, never at the world origin: that is a real place
                // kilometres away, where the game's distance culling deactivates the object.
                // An inactive object never runs Start(), so the item's own scripts never wire
                // themselves up and it ends up present but unusable (a lighter that won't light).
                Vector3 spawnPosition = PlayerManager.PlayerTransform != null
                    ? PlayerManager.PlayerTransform.position
                    : WorldMover.currentMove;

                itemGO = Instantiate(prefab, spawnPosition, Quaternion.identity);
                itemGO.SetActive(true);

                InventoryItemSpec spec = itemGO.GetComponent<InventoryItemSpec>();
                if (spec != null)
                    spec.BelongsToPlayer = true;

                freshCopy = true;
            }

            Client_RegisterLocalCopy(entryId, itemGO);

            if (freshCopy)
            {
                // A just-instantiated item has run Awake but not Start, so its scripts have
                // not finished wiring up: applying save data or stowing it now leaves it
                // half-initialised and unusable. Let it initialise first.
                awaitingStow.Add(entryId);
                StartCoroutine(FinishAddingCopy(entryId, itemGO, state));
            }
            else
            {
                ApplyItemState(itemGO, state);
                StoreInInventory(itemGO);
            }
        }
        finally
        {
            applyingRemoteChange = false;
        }
    }

    public void Client_RemoveEntry(ushort entryId)
    {
        if (!localCopies.TryGetValue(entryId, out GameObject itemGO))
            return;

        localCopies.Remove(entryId);
        if (itemGO != null)
            localCopyObjects.Remove(itemGO);

        applyingRemoteChange = true;
        try
        {
            DiscardLocalCopy(itemGO);
        }
        finally
        {
            applyingRemoteChange = false;
        }
    }

    /// <summary>
    ///     Removes a local copy from the inventory and every storage list before destroying
    ///     it. Skipping the storage cleanup leaves the game holding a reference to a
    ///     destroyed object, which shows up as an item that cannot be interacted with.
    /// </summary>
    private static void DiscardLocalCopy(GameObject itemGO)
    {
        if (itemGO == null)
            return;

        if (itemGO.TryGetComponent(out ItemBase item))
        {
            if (item.IsGrabbed())
                item.ForceEndInteraction();

            StorageController.Instance.RemoveItemFromStorageItemList(item);
        }

        // Destroying an item that is still equipped leaves the hand holding a dead
        // reference, which shows up as a ghost item until something else replaces it
        int equipSlot = Inventory.Instance.GetEquipSlotForItem(itemGO);
        if (equipSlot >= 0)
            Inventory.Instance.UnequipItem(false, equipSlot);

        Inventory.Instance.PurgeFromInventory(itemGO);
        Destroy(itemGO);
    }

    /// <summary>
    ///     Compares this player's inventory against the shared entries and reports differences.
    ///     Polling is used rather than the inventory events because it also catches items that
    ///     were consumed (their object is simply gone).
    /// </summary>
    public void Client_PollLocalChanges()
    {
        if (applyingRemoteChange || Inventory.Instance == null || Time.time < nextPoll)
            return;

        nextPoll = Time.time + POLL_INTERVAL;

        bool isHost = NetworkLifecycle.Instance.IsHost();

        // Find items that are no longer stowed. Collected first, because acting on them
        // modifies localCopies.
        staleEntries.Clear();
        foreach (var kvp in localCopies)
        {
            GameObject itemGO = kvp.Value;

            // Still being set up: it has no slot yet, which is not a drop
            if (awaitingStow.Contains(kvp.Key))
                continue;

            // Destroyed means used up
            if (itemGO == null)
            {
                staleEntries.Add(kvp.Key);
                continue;
            }

            // Still in a real slot. A dropped item keeps its slot *reserved* so it can
            // return there, so the reservation has to be told apart from real storage.
            int slot = Inventory.Instance.IndexOf(itemGO);
            if (slot >= 0 && !Inventory.Instance.GetSlotDroppedState(slot))
                continue;

            // Held items leave their slot but are still owned. Which item is in hand is
            // per-player, so it must never leave the shared inventory.
            if (IsHeldByLocalPlayer(itemGO))
                continue;

            staleEntries.Add(kvp.Key);
        }

        foreach (ushort entryId in staleEntries)
        {
            if (!localCopies.TryGetValue(entryId, out GameObject itemGO))
                continue;

            // Used up: it just leaves the shared inventory
            if (itemGO == null)
            {
                ForgetLocalCopy(entryId);

                if (isHost)
                    Server_RemoveEntry(entryId);
                else
                    NetworkLifecycle.Instance.Client.SendInventoryRemove(entryId, false, Vector3.zero, Quaternion.identity, null);

                continue;
            }

            // Dropped into the world
            Vector3 position = itemGO.transform.position - WorldMover.currentMove;
            Quaternion rotation = itemGO.transform.rotation;

            ForgetLocalCopy(entryId);

            if (isHost)
            {
                // The host's own object becomes the authoritative world item, rather than
                // being destroyed and respawned
                Server_RemoveEntry(entryId);
                NetworkedItemManager.Instance.Server_AdoptDroppedItem(itemGO);
            }
            else
            {
                Multiplayer.Log($"Dropped shared item [{entryId}] \"{itemGO.name}\"; asking the server to place it");
                NetworkLifecycle.Instance.Client.SendInventoryRemove(entryId, true, position, rotation, CaptureItemState(itemGO));

                // The server places the authoritative world item, so this copy goes. It must
                // be unregistered from the inventory and storages first, or the game keeps
                // referencing a destroyed object (a "ghost").
                DiscardLocalCopy(itemGO);
            }
        }

        // Items that entered this player's possession from the world.
        // Excluding dropped items is essential: an item lying in the world keeps its slot
        // reserved and is still listed by GetItemsArray(true), so counting it as carried
        // would read every drop as an instant pickup.
        foreach (GameObject itemGO in Inventory.Instance.GetItemsArray(false))
            TryShareCarriedItem(itemGO, isHost);

        // Picking an item off the ground puts it straight into the player's hands rather
        // than into a slot, and held items are not part of the slot array. Without this the
        // item would stay unshared until the player happened to stow it.
        for (int equipSlot = 0; equipSlot < MAX_EQUIP_SLOTS; equipSlot++)
            TryShareCarriedItem(Inventory.Instance.GetEquippedItemAtSlot(equipSlot), isHost);
    }

    /// <summary>
    ///     Adds a world item the player is now carrying to the shared inventory.
    /// </summary>
    private void TryShareCarriedItem(GameObject itemGO, bool isHost)
    {
        if (itemGO == null || localCopyObjects.Contains(itemGO))
            return;

        ItemBase carriedItem = itemGO.GetComponent<ItemBase>();

        if (!NetworkedItem.TryGetNetworkedItem(carriedItem, out NetworkedItem netItem) || netItem.NetId == 0)
            return;

        // Job papers, money and licences are synchronised by their own systems
        if (IsExcludedFromSharedInventory(carriedItem))
            return;

        // Only ask once per item; the server may take a few ticks to answer, and some items
        // are never shareable and would otherwise be requested forever
        if (!pendingStoreRequests.Add(netItem.NetId))
            return;

        if (isHost)
        {
            NetworkLifecycle.Instance.Server.TryGetServerPlayer(NetworkLifecycle.Instance.Client.PlayerId, out ServerPlayer hostPlayer);
            Server_StoreWorldItem(netItem.NetId, hostPlayer);
        }
        else
        {
            NetworkLifecycle.Instance.Client.SendInventoryStore(netItem.NetId);
        }
    }

    private IEnumerator FinishAddingCopy(ushort entryId, GameObject itemGO, string state)
    {
        // Awake has run, but Start has not, and Start only runs while the object is active.
        // Wait for it to actually initialise before loading state into it or stowing it away.
        yield return null;
        yield return null;

        if (itemGO == null)
        {
            awaitingStow.Remove(entryId);
            yield break;
        }

        applyingRemoteChange = true;
        try
        {
            ApplyItemState(itemGO, state);
            StoreInInventory(itemGO);
        }
        finally
        {
            applyingRemoteChange = false;
            awaitingStow.Remove(entryId);
        }

        // A copy is created next to the player so it can initialise. If it did not make it
        // into the inventory it would simply lie there, visible and grabbable by anyone
        // walking past, so make that loud rather than silent.
        if (itemGO != null && !Inventory.Instance.Contains(itemGO, true))
            Multiplayer.LogWarning($"Shared item \"{itemGO.name}\" could not be stowed and is lying at the player's feet");
    }

    /// <summary>
    ///     Stows an item, preferring the backpack so shared items do not clutter the hotbar.
    /// </summary>
    private static void StoreInInventory(GameObject itemGO)
    {
        // Already carried, or in the player's hands - leave it where it is rather than
        // yanking a just-picked-up item into the backpack
        if (Inventory.Instance.Contains(itemGO, true) || IsHeldByLocalPlayer(itemGO))
            return;

        int slot = -1;

        // If this player keeps a locked slot for this kind of item, put it back there - the
        // whole point of locking a slot is that the item always returns to the same place,
        // even when it took a detour through another player's hands.
        int reservedSlot = Inventory.Instance.FindReservedSlotForDroppedItem(itemGO);
        if (reservedSlot >= 0 && Inventory.Instance.IsSlotEmpty(reservedSlot))
            slot = Inventory.Instance.AddItemToInventory(itemGO, reservedSlot);

        // Otherwise prefer the backpack so shared items do not clutter the hotbar
        if (slot < 0)
        {
            int backpackSlot = Inventory.Instance.GetFirstFreeBackpackSlot();
            slot = backpackSlot >= 0
                ? Inventory.Instance.AddItemToInventory(itemGO, backpackSlot)
                : Inventory.Instance.AddItemToInventory(itemGO);
        }

        if (slot < 0)
        {
            // Should not happen while inventories stay in lockstep; leave it at the
            // player's feet rather than losing the item outright
            Multiplayer.LogWarning($"No free slot for shared item \"{itemGO.name}\"; leaving it at the player's feet");
            itemGO.transform.position = PlayerManager.PlayerTransform.position;
            itemGO.SetActive(true);
        }
    }

    /// <summary>
    ///     Items that must never be pulled into the shared inventory. Each of these already
    ///     has its own authoritative synchronisation, and each carries state that cannot be
    ///     rebuilt from a prefab - sharing them destroys the original and hands everyone a
    ///     blank copy.
    /// </summary>
    public static bool IsExcludedFromSharedInventory(ItemBase item)
    {
        if (item == null)
            return true;

        // Job papers belong to the job system: the job they refer to lives on the original
        // object, so a fresh copy is a blank sheet that cannot start or complete anything
        if (item.GetComponent<JobOverview>() != null
            || item.GetComponent<JobBooklet>() != null
            || item.GetComponent<JobReport>() != null
            || item.GetComponent<JobExpiredReport>() != null
            || item.GetComponent<JobMissingLicenseReport>() != null)
            return true;

        // Physical money is backed by the shared wallet. Copying a banknote to every player
        // would let the same payment be banked more than once. The wallet itself is a normal
        // carried item, so only spend-and-destroy money is excluded.
        if (item.TryGetComponent(out IMoney money) && money.ShouldDestroyOnUse)
            return true;

        // Licences are consumed to unlock something that is already synchronised separately
        string prefabName = item.InventorySpecs?.ItemPrefabName;
        if (prefabName != null && prefabName.StartsWith("License", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>Stops tracking an entry's copy, without touching the object itself.</summary>
    private void ForgetLocalCopy(ushort entryId)
    {
        if (localCopies.TryGetValue(entryId, out GameObject itemGO) && itemGO != null)
            localCopyObjects.Remove(itemGO);

        localCopies.Remove(entryId);
    }

    /// <summary>Whether this player has the item, in a slot or in their hands.</summary>
    private static bool IsCarriedByLocalPlayer(GameObject itemGO)
    {
        return Inventory.Instance.Contains(itemGO, true) || IsHeldByLocalPlayer(itemGO);
    }

    private static bool IsHeldByLocalPlayer(GameObject itemGO)
    {
        if (Inventory.Instance.GetEquipSlotForItem(itemGO) >= 0)
            return true;

        return itemGO.TryGetComponent(out ItemBase item) && item.IsGrabbed();
    }

    private GameObject FindUnclaimedInventoryItem(string prefabName)
    {
        if (Inventory.Instance == null)
            return null;

        foreach (GameObject itemGO in Inventory.Instance.GetItemsArray(true))
        {
            if (itemGO == null || localCopyObjects.Contains(itemGO))
                continue;

            ItemBase item = itemGO.GetComponent<ItemBase>();
            if (item?.InventorySpecs?.ItemPrefabName != prefabName)
                continue;

            // Never adopt something that is still a world item: it is about to be destroyed
            // as part of the pickup, which would take the inventory copy with it
            if (NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem netItem) && netItem.NetId != 0)
                continue;

            return itemGO;
        }

        return null;
    }

    /// <summary>
    ///     Registers a local copy for an entry. Inventory copies are per-player, so they must
    ///     never carry a network id: on the host every new object is given one automatically,
    ///     which would turn the copy back into a replicated world item and, once it was picked
    ///     up again, spawn an endless add/remove loop between the two entries.
    /// </summary>
    public void Client_RegisterLocalCopy(ushort entryId, GameObject itemGO)
    {
        if (localCopies.TryGetValue(entryId, out GameObject previous) && previous != null)
            localCopyObjects.Remove(previous);

        localCopies[entryId] = itemGO;

        if (itemGO != null)
            localCopyObjects.Add(itemGO);

        if (itemGO != null
            && itemGO.TryGetComponent(out ItemBase item)
            && NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem netItem)
            && netItem.NetId != 0)
        {
            netItem.NetId = 0;
        }
    }
    #endregion

    /// <summary>
    ///     Drops all session state. Without this a reconnect inherits the previous session's
    ///     entries and copies, which no longer refer to anything valid.
    /// </summary>
    public void Reset()
    {
        entries.Clear();
        entryStates.Clear();
        entryIdPool.Reset();

        localCopies.Clear();
        localCopyObjects.Clear();
        awaitingStow.Clear();
        pendingStoreRequests.Clear();
        staleEntries.Clear();

        applyingRemoteChange = false;
        nextPoll = 0f;

        StopAllCoroutines();
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(SharedInventoryManager)}]";
    }
}
