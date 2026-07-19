using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Interaction;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public enum ItemState : byte
{
    Dropped,        //belongs to the world
    Thrown,         //was thrown by player
    InHand,         //held by player
    InInventory,    //in player's inventory
    Attached,       //attached to another object (e.g. EOT Lanterns)
    InContainer,    //stored inside another networked item
    InstalledGadget //the source item is represented by its linked GadgetBase
}

public class NetworkedItem : IdMonoBehaviour<ushort, NetworkedItem>
{
    public static Vector3 ToNetworkPosition(Vector3 worldPosition) => worldPosition - WorldMover.currentMove;

    public static Vector3 ToWorldPosition(Vector3 networkPosition) => networkPosition + WorldMover.currentMove;

    #region Lookup Cache
    private static readonly Dictionary<ItemBase, NetworkedItem> itemBaseToNetworkedItem = new(4096);

    public static Dictionary<ItemBase, NetworkedItem>.ValueCollection GetAll() => itemBaseToNetworkedItem.Values;
    
    public static bool Get(ushort netId, out NetworkedItem obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedItem> rawObj);
        obj = (NetworkedItem)rawObj;
        return b;
    }

    public static bool TryGet(ushort netId, out NetworkedItem obj)
    {
        bool b = TryGet(netId, out IdMonoBehaviour<ushort, NetworkedItem> rawObj);
        obj = (NetworkedItem)rawObj;
        return b;
    }

    public static bool GetItem(ushort netId, out ItemBase obj)
    {
        bool b = Get(netId, out NetworkedItem networkedItem);
        obj = b ? networkedItem.Item : null;
        return b;
    }

    public static bool TryGetNetworkedItem(ItemBase item, out NetworkedItem networkedItem)
    {
        return itemBaseToNetworkedItem.TryGetValue(item, out networkedItem);
    }

    public static bool TryGetNetId(ItemBase item, out ushort netID)
    {
        if (itemBaseToNetworkedItem.TryGetValue(item, out var networkedItem))
        {
            netID = networkedItem.NetId;
            return true;
        }

        netID = 0;
        return false;
    }
    #endregion

    private const float PositionThreshold = 0.1f;
    private const float RotationThreshold = 0.1f;

    public ItemBase Item { get; private set; }
    private GrabHandlerItem grabHandler;
    private SnappableItem snappableItem;
    private GadgetItem gadgetItem;
    private Component trackedItem;
    private List<object> trackedValues = new List<object>();
    private readonly Dictionary<string, object> unappliedTrackedValues = new();
    public bool UsefulItem { get; private set; } = false;
    public Type TrackedItemType { get; private set; }
    public uint LastDirtyTick { get; private set; }
    private bool initialised;
    private bool registrationComplete = false;
    private Queue<ItemUpdateData> pendingSnapshots = new Queue<ItemUpdateData>();

    //Track dirty states
    private bool createdDirty = true;   //if set, we created this item dirty and have not sent an update
    private ItemState lastState;
    private bool stateDirty;
    private bool wasThrown;

    private Vector3 thrownPosition;
    private Quaternion thrownRotation;
    private Vector3 throwDirection;

    //Handle ownership
    public sbyte OwnerId { get; private set; } = -1; // 0 means no owner

    //public void SetOwner(ushort playerId)
    //{
    //    if (OwnerId != playerId)
    //    {
    //        if (OwnerId != 0)
    //        {
    //            NetworkedItemManager.Instance.RemoveItemFromPlayerInventory(this);
    //        }
    //        OwnerId = playerId;
    //        if (playerId != 0)
    //        {
    //            NetworkedItemManager.Instance.AddItemToPlayerInventory(playerId, this);
    //        }
    //    }
    //}

    protected override bool IsIdServerAuthoritative => true;

    protected override void Awake()
    {
        base.Awake();
        //Multiplayer.LogDebug(() => $"NetworkedItem.Awake() {name}");
        NetworkedItemManager.Instance.CheckInstance(); //Ensure the NetworkedItemManager is initialised

        Register();
    }

    protected void Start()
    {
        if (!initialised)
            Register();

        // Mark registration as complete for items that don't need tracked values
        if (!registrationComplete && !UsefulItem)
            registrationComplete = true;
    }

    public T GetTrackedItem<T>() where T : Component
    {
        return UsefulItem ? trackedItem as T : null;
    }

    public void Initialize<T>(T item, ushort netId = 0, bool createDirty = true) where T : Component
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.Initialize<{typeof(T)}>(netId: {netId}, name: {name}, createDirty: {createdDirty})");

        if (netId != 0)
            NetId = netId;

        trackedItem = item;
        TrackedItemType = typeof(T);
        UsefulItem = true;

        createdDirty = createDirty;

        if (Item == null)
            Register();

    }

    private bool Register()
    {
        if (initialised)
            return false;

        try
        {

            if (!TryGetComponent(out ItemBase itemBase))
            {
                Multiplayer.LogError($"NetworkedItem.Register() Unable to find ItemBase for {name}");
                return false;
            }

            Item = itemBase;
            itemBaseToNetworkedItem[Item] = this;

            Item.Grabbed += OnGrabbed;
            Item.Ungrabbed += OnUngrabbed;
            Item.ItemInContainerStateChanged += OnContainerChanged;

            //Find special interaction components
            TryGetComponent<GrabHandlerItem>(out grabHandler);
            TryGetComponent<SnappableItem>(out snappableItem);
            TryGetComponent<GadgetItem>(out gadgetItem);

            lastState = GetItemState();
            stateDirty = false;

            initialised = true;
            return true;
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"NetworkedItem.Register() Unable to find ItemBase for {name}\r\n{ex.Message}");
            return false;
        }
    }

    private void OnUngrabbed(ControlImplBase obj)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.OnUngrabbed() NetID: {NetId}, {name}");
        stateDirty = true;
    }

    private void OnGrabbed(ControlImplBase obj)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.OnGrabbed() NetID: {NetId}, {name}");
        stateDirty = true;
    }

    private void OnContainerChanged(ItemBase item, AItemContainer newContainer, AItemContainer oldContainer, bool added)
    {
        stateDirty = true;
    }

    public void OnThrow(Vector3 direction)
    {
        //block a received throw from 
        if (wasThrown)
        {
            wasThrown = false;
            return;
        }

        throwDirection = direction;
        thrownPosition = ToNetworkPosition(Item.transform.position);
        thrownRotation = Item.transform.rotation;

        //Multiplayer.LogDebug(() => $"NetworkedItem.OnThrow() netId: {NetId}, Name: {name}, Raw Position: {Item.transform.position}, Position: {thrownPosition}, Rotation: {thrownRotation}, Direction: {throwDirection}");

        wasThrown = true;
        stateDirty = true;
    }


    #region Item Value Tracking
    public void RegisterTrackedValue<T>(string key, Func<T> valueGetter, Action<T> valueSetter, Func<T, T, bool> thresholdComparer = null, bool serverAuthoritative = false)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.RegisterTrackedValue(\"{key}\", {valueGetter != null}, {valueSetter != null}, {thresholdComparer != null}, {serverAuthoritative}) itemNetId {NetId}, item name: {name}");
        if (trackedValues.Any(value => ((dynamic)value).Key == key))
        {
            Multiplayer.LogWarning($"Duplicate tracked item key '{key}' ignored for {name} ({NetId}).");
            return;
        }

        var trackedValue = new TrackedValue<T>(key, valueGetter, valueSetter, thresholdComparer, serverAuthoritative);
        trackedValues.Add(trackedValue);

        if (unappliedTrackedValues.TryGetValue(key, out object pendingValue))
        {
            trackedValue.SetValueFromObject(pendingValue);
            unappliedTrackedValues.Remove(key);
        }
    }

    public void FinaliseTrackedValues()
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.FinaliseTrackedValues() itemNetId: {NetId}, item name: {name}");

        while (pendingSnapshots.Count > 0)
        {
            Multiplayer.LogDebug(() => $"NetworkedItem.FinaliseTrackedValues() itemNetId: {NetId}, item name: {name}. Dequeuing");
            ApplySnapshot(pendingSnapshots.Dequeue());
        }

        registrationComplete = true;

    }

    private bool HasDirtyValues()
    {
        //clients should only send values that are not server authoritative
        if (!NetworkLifecycle.Instance.IsHost())
            return trackedValues.Any(tv => ((dynamic)tv).IsDirty && !((dynamic)tv).ServerAuthoritative);
        else
            return trackedValues.Any(tv => ((dynamic)tv).IsDirty);
    }

    private Dictionary<string, object> GetDirtyStateData()
    {
        var dirtyData = new Dictionary<string, object>();
        foreach (var trackedValue in trackedValues)
        {
            bool canSend = NetworkLifecycle.Instance.IsHost() || !((dynamic)trackedValue).ServerAuthoritative;
            if (canSend && ((dynamic)trackedValue).IsDirty)
            {
                dirtyData[((dynamic)trackedValue).Key] = ((dynamic)trackedValue).GetValueAsObject();
            }
        }
        return dirtyData;
    }
    private Dictionary<string, object> GetAllStateData()
    {
        var data = new Dictionary<string, object>();
        foreach (var trackedValue in trackedValues)
        {
            data[((dynamic)trackedValue).Key] = ((dynamic)trackedValue).GetValueAsObject();
        }
        return data;
    }

    private void MarkValuesClean()
    {
        foreach (var trackedValue in trackedValues)
        {
            ((dynamic)trackedValue).MarkClean();
        }
    }

    #endregion

    public ItemUpdateData GetSnapshot()
    {
        ItemUpdateData snapshot;
        ItemUpdateData.ItemUpdateType updateType = ItemUpdateData.ItemUpdateType.None;

        if (Item == null && Register() == false)
            return null;

        bool hasDirtyVals = HasDirtyValues();
        ItemState currentState = GetItemState();
        bool stateChanged = lastState != currentState;

        if (!stateDirty && !stateChanged && !hasDirtyVals)
            return null;

        if (!createdDirty)
        {
            // stateDirty also covers changes within a state, such as moving between
            // container slots, which cannot be detected from the enum alone.
            if (stateDirty || stateChanged)
                updateType |= ItemUpdateData.ItemUpdateType.ItemState;

            if (hasDirtyVals)
            {
                Multiplayer.LogDebug(GetDirtyValuesDebugString);
                updateType |= ItemUpdateData.ItemUpdateType.ObjectState;
            }
        }
        else
        {
            updateType = ItemUpdateData.ItemUpdateType.Create;
        }

        //no changes this snapshot
        if (updateType == ItemUpdateData.ItemUpdateType.None)
            return null;

        lastState = currentState;
        LastDirtyTick = NetworkLifecycle.Instance.Tick;
        snapshot = CreateUpdateData(updateType);

        createdDirty = false;
        stateDirty = false;
        wasThrown = false;

        MarkValuesClean();

        return snapshot;
    }

    public void ReceiveSnapshot(ItemUpdateData snapshot)
    {
        if (snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.None)
            return;

        if (!registrationComplete)
        {
            Multiplayer.Log($"NetworkedItem.ReceiveSnapshot() netId: {snapshot?.ItemNetId}, ItemUpdateType: {snapshot?.UpdateType}. Queuing");
            pendingSnapshots.Enqueue(snapshot);
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(ItemUpdateData snapshot)
    {
        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ItemState) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.FullSync) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create))
        {
            Multiplayer.Log($"NetworkedItem.ApplySnapshot() netId: {snapshot?.ItemNetId}, ItemUpdateType: {snapshot?.UpdateType}, ItemState: {snapshot?.ItemState}, Active state: {gameObject.activeInHierarchy}");

            switch (snapshot.ItemState)
            {
                case ItemState.Dropped:
                case ItemState.Thrown:
                    HandleDroppedOrThrownState(snapshot);
                    break;

                case ItemState.InHand:
                case ItemState.InInventory:
                    HandleInventoryOrHandState(snapshot);
                    break;

                case ItemState.Attached:
                    HandleAttachedState(snapshot);
                    break;

                case ItemState.InContainer:
                    if (!HandleContainerState(snapshot))
                        StartCoroutine(RetryContainerState(snapshot));
                    break;

                case ItemState.InstalledGadget:
                    // Placement and gadget data are applied atomically by GadgetItemPatch.
                    break;

                default:
                    throw new Exception($"NetworkedItem.ApplySnapshot() Item state not implemented: {snapshot?.ItemState}");

            }
        }

        Multiplayer.Log($"NetworkedItem.ApplySnapshot() netID: {snapshot?.ItemNetId}, ItemUpdateType {snapshot?.UpdateType} About to process states");

        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ObjectState))
        {
            Multiplayer.Log($"NetworkedItem.ApplySnapshot() netID: {snapshot?.ItemNetId}, States: {snapshot?.States?.Count}");

            if (snapshot.States != null)
            {
                ApplyTrackedValues(snapshot.States);
            }
        }

        Multiplayer.Log($"NetworkedItem.ApplySnapshot() netID: {snapshot?.ItemNetId}, ItemUpdateType {snapshot?.UpdateType} states processed");

        //mark values as clean
        createdDirty = false;
        stateDirty = false;

        MarkValuesClean();
        return;
    }

    public ItemUpdateData CreateUpdateData(ItemUpdateData.ItemUpdateType updateType)
    {
        if (transform == null || Item == null || Item?.InventorySpecs == null || Item?.InventorySpecs?.ItemPrefabName == null)
        {
            Multiplayer.LogDebug(()=>$"NetworkedItem.CreateUpdateData({updateType}) NetId: {NetId}, name: {name}. Transform is null: {transform == null}, Item is null: {Item == null}, Inventory Specs: {Item?.InventorySpecs == null}, ItemPrefabName is null: {Item?.InventorySpecs?.ItemPrefabName == null}");
            return null;
        }

        Vector3 position;
        Quaternion rotation;
        Dictionary<string, object> states;
        ushort carId = 0;
        bool frontCoupler = true;
        ushort containerNetId = 0;
        int containerSlot = -1;

        // Creates/full syncs are generated directly for newly relevant clients and
        // must not depend on the state cached by a previous dirty-update pass.
        if (updateType.HasFlag(ItemUpdateData.ItemUpdateType.Create) ||
            updateType.HasFlag(ItemUpdateData.ItemUpdateType.FullSync))
        {
            lastState = GetItemState();
        }

        if (wasThrown)
        {
            position = thrownPosition;
            rotation = thrownRotation;
        }
        else
        {
            position = ToNetworkPosition(transform.position);
            rotation = transform.rotation;
        }

        if (updateType.HasFlag(ItemUpdateData.ItemUpdateType.Create) || updateType.HasFlag(ItemUpdateData.ItemUpdateType.FullSync))
        {
            states = GetAllStateData();
        }
        else
        {
            states = GetDirtyStateData();
        }

        if (lastState == ItemState.Attached)
        {
            ItemSnapPointCoupler itemSnapPointCoupler = snappableItem.SnappedTo as ItemSnapPointCoupler;

            if (itemSnapPointCoupler != null)
            {
                carId = itemSnapPointCoupler.Car.GetNetId();
                frontCoupler = itemSnapPointCoupler.IsFront;
            }
        }
        else if (lastState == ItemState.InContainer && Item.InContainer is ItemContainer container)
        {
            if (container.ItemBase != null)
                TryGetNetId(container.ItemBase, out containerNetId);

            containerSlot = Array.IndexOf(container.GetItemsArray(false), Item.gameObject);
        }

        var updateData = new ItemUpdateData
        {
            UpdateType = updateType,
            ItemNetId = NetId,
            PrefabName = Item.InventorySpecs.ItemPrefabName,
            ItemState = lastState,
            ItemPosition = position,
            ItemRotation = rotation,
            ThrowDirection = throwDirection,
            CarNetId = carId,
            AttachedFront = frontCoupler,
            ContainerNetId = containerNetId,
            ContainerSlot = containerSlot,
            States = states,
        };

        return updateData;
    }

    private ItemState GetItemState()
    {
        //Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, Parent: {Item.transform.parent} WorldMover: {WorldMover.OriginShiftParent}, wasThrown: {wasThrown}, isGrabbed: {Item.IsGrabbed()} Inventory.Contains(): {Inventory.Instance.Contains(this.gameObject, false)} Storage.Contains: {StorageController.Instance.StorageInventory.ContainsItem(Item)}");

        if (wasThrown)
        {
            Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, Parent: {Item.transform.parent} WorldMover: {WorldMover.OriginShiftParent}, wasThrown: {wasThrown}");
            return ItemState.Thrown;
        }

        if (Item.IsGrabbed())
            return ItemState.InHand;

        if (Inventory.Instance.Contains(this.gameObject, false))
            return ItemState.InInventory;

        if (Item.InContainer != null)
            return ItemState.InContainer;

        if (gadgetItem != null && gadgetItem.Gadget != null && gadgetItem.Gadget.IsLinked)
            return ItemState.InstalledGadget;

        if (snappableItem != null && snappableItem.IsSnapped)
        {
            Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, snapped! {this.transform.parent}");
            return ItemState.Attached;
        }

        if (Item.transform.parent == WorldMover.OriginShiftParent)
        {
            Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, Parent: {Item.transform.parent} WorldMover: {WorldMover.OriginShiftParent}, wasThrown: {wasThrown}");
            return ItemState.Dropped;
        }

        //do we need a condition to check if it's attached to something else (last attach vs current attach)?
        return ItemState.Dropped;

    }

    private void ApplyTrackedValues(Dictionary<string, object> newValues)
    {
        Multiplayer.LogDebug(() => $"NetworkedItem.ApplyTrackedValues() itemNetId: {NetId}, item name: {name}. Null checks");

        if (newValues == null || newValues.Count == 0)
            return;


        Multiplayer.LogDebug(() => $"NetworkedItem.ApplyTrackedValues() itemNetId: {NetId}, item name: {name}. Registration complete: {registrationComplete}");

        foreach (var newValue in newValues)
        {
            var trackedValue = trackedValues.Find(tv => ((dynamic)tv).Key == newValue.Key);
            if (trackedValue != null)
            {
                if (!NetworkLifecycle.Instance.IsHost() || !((dynamic)trackedValue).ServerAuthoritative)
                {
                    try
                    {
                        ((dynamic)trackedValue).SetValueFromObject(newValue.Value);
                        Multiplayer.LogDebug(() => $"NetworkedItem.ApplyTrackedValues() itemNetId: {NetId}, item name: {name}, Updated tracked value: {newValue.Key}, value: {newValue.Value} ");
                    }
                    catch (Exception ex)
                    {
                        Multiplayer.LogError($"NetworkedItem.ApplyTrackedValues() itemNetId: {NetId}, item name: {name}. Error updating tracked value {newValue.Key}: {ex.Message}");
                    }
                }
                else
                {
                    Multiplayer.LogWarning($"NetworkedItem.ApplyTrackedValues() itemNetId: {NetId}, item name: {name}. Skipped server-authoritative value update from client: {newValue.Key}");
                }
            }
            else
            {
                unappliedTrackedValues[newValue.Key] = newValue.Value;
                Multiplayer.LogDebug(() => $"Tracked value '{newValue.Key}' queued until its item adapter registers on {name} ({NetId}).");
            }
        }
    }

    #region Item State Update Handlers

    private void HandleDroppedOrThrownState(ItemUpdateData snapshot)
    {
        //resolve attachment
        if (Item.IsSnapped)
        {
            Item.SnappableItem.SnappedTo.UnsnapItem(false);
        }

        //resolve ownership
        if (NetworkLifecycle.Instance.IsHost())
            if (NetworkLifecycle.Instance.Server.TryGetServerPlayer(snapshot.Player, out ServerPlayer player) && player.OwnsItem(NetId))
                player.RemoveOwnedItem(NetId);

        // Use the game's world-placement transition so a remote item is removed
        // from any local inventory/container state and parented to the origin-shift
        // transform before it is made visible.
        StorageController.RemoveItemFromCurrentStorageAndAddToWorld(
            Item,
            ToWorldPosition(snapshot.ItemPosition),
            snapshot.ItemRotation);
        OwnerId = 0;

        //handle throwing of the item
        if (snapshot.ItemState == ItemState.Thrown)
        {
            Multiplayer.LogDebug(() => $"NetworkedItem.HandleDroppedOrThrownState() ItemNetId: {snapshot?.ItemNetId} Thrown. Position: {transform.position}, Direction: {snapshot?.ThrowDirection}");

            wasThrown = true;
            grabHandler?.Throw(snapshot.ThrowDirection);
        }
        else
        {
            Multiplayer.LogDebug(() => $"NetworkedItem.HandleDroppedOrThrownState() ItemNetId: {snapshot?.ItemNetId} Dropped. Position: {transform.position}");
        }
    }

    private void HandleAttachedState(ItemUpdateData snapshot)
    {
        //resovle ownership
        if (NetworkLifecycle.Instance.IsHost())
            if (NetworkLifecycle.Instance.Server.TryGetServerPlayer(snapshot.Player, out ServerPlayer player) && player.OwnsItem(NetId))
                player.RemoveOwnedItem(NetId);

        //handle attaching the item
        gameObject.SetActive(true);
        Multiplayer.LogDebug(() => $"NetworkedItem.HandleAttachedState() ItemNetId: {snapshot?.ItemNetId} attempting attachment to car {snapshot.CarNetId}, at the front {snapshot.AttachedFront}");

        if (!NetworkedTrainCar.TryGet(snapshot.CarNetId, out TrainCar trainCar))
        {
            Multiplayer.LogWarning($"NetworkedItem.HandleAttachedState() CarNetId: {snapshot?.CarNetId} not found for ItemNetId: {snapshot?.ItemNetId}");
            return;
        }

        //Try to find the coupler snap point for the car and correct end to snap to
        var snapPoint = trainCar?.physicsLod?.GetCouplerSnapPoints()
            .FirstOrDefault(sp => sp.IsFront == snapshot.AttachedFront);

        if (snapPoint == null)
        {
            Multiplayer.LogWarning($"NetworkedItem.HandleAttachedState() ItemNetId: {snapshot?.ItemNetId}. No valid snap point found for car {snapshot.CarNetId}");
            return;
        }

        //Attempt attachment to car
        Item.ItemRigidbody.isKinematic = false;
        if (!snapPoint.SnapItem(Item, false))
        {
            Multiplayer.LogWarning($"NetworkedItem.HandleAttachedState() Attachment failed for item {snapshot?.ItemNetId} to car {snapshot.CarNetId}");
        }
    }

    private void HandleInventoryOrHandState(ItemUpdateData snapshot)
    {
        if (Item.IsSnapped)
        {
            Item.SnappableItem.SnappedTo.UnsnapItem(false);
        }

        if (NetworkLifecycle.Instance.IsHost())
            if (NetworkLifecycle.Instance.Server.TryGetServerPlayer(snapshot.Player, out ServerPlayer player) && !player.OwnsItem(NetId))
                player.AddOwnedItem(NetId);

        //todo add to player model's hand
        this.gameObject.SetActive(false);
    }

    public Vector3 GetRelevancePosition()
    {
        if (gadgetItem != null && gadgetItem.Gadget != null && gadgetItem.Gadget.IsLinked)
            return gadgetItem.Gadget.transform.position;

        return transform.position;
    }

    private bool HandleContainerState(ItemUpdateData snapshot)
    {
        if (!TryGet(snapshot.ContainerNetId, out NetworkedItem containerItem) ||
            containerItem == null ||
            !containerItem.TryGetComponent(out ItemContainer container) ||
            snapshot.ContainerSlot < 0 || snapshot.ContainerSlot >= container.Capacity)
        {
            return false;
        }

        if (Item.InContainer == container && Array.IndexOf(container.GetItemsArray(false), Item.gameObject) == snapshot.ContainerSlot)
            return true;

        if (Item.InContainer != null)
            Item.InContainer.RemoveItem(Item.gameObject, false, false);

        GameObject occupyingItem = container[snapshot.ContainerSlot];
        if (occupyingItem != null && occupyingItem != Item.gameObject)
            container.RemoveItem(snapshot.ContainerSlot, false, true);

        return container.AddItem(Item.gameObject, snapshot.ContainerSlot);
    }

    private IEnumerator RetryContainerState(ItemUpdateData snapshot)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            if (HandleContainerState(snapshot))
                yield break;
        }

        Multiplayer.LogWarning($"Timed out attaching item {NetId} to container {snapshot.ContainerNetId} slot {snapshot.ContainerSlot}.");
    }
    #endregion

    protected override void OnDestroy()
    {
        if (UnloadWatcher.isQuitting || UnloadWatcher.isUnloading)
            return;

        if (NetworkLifecycle.Instance.IsHost())
        {
            var updateData = CreateUpdateData(ItemUpdateData.ItemUpdateType.Destroy);
            if (updateData != null)
                NetworkedItemManager.Instance.AddDirtyItemSnapshot(this, updateData);
        }

        if (Item != null)
        {
            Item.Grabbed -= OnGrabbed;
            Item.Ungrabbed -= OnUngrabbed;
            Item.ItemInContainerStateChanged -= OnContainerChanged;
            itemBaseToNetworkedItem.Remove(Item);
        }
        else
        {
            Multiplayer.LogWarning($"NetworkedItem.OnDestroy({name}, {NetId}) Item is null!");
        }

        base.OnDestroy();

    }

    public string GetDirtyValuesDebugString()
    {
        var dirtyValues = trackedValues.Where(tv => ((dynamic)tv).IsDirty).ToList();
        if (dirtyValues.Count == 0)
        {
            return "No dirty values";
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Dirty values for NetworkedItem: {name}, NetId: {NetId}:");
        foreach (var value in dirtyValues)
        {
            sb.AppendLine(((dynamic)value).GetDebugString());
        }
        return sb.ToString();
    }
}
