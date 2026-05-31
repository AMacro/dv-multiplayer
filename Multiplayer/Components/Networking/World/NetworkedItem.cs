using DV;
using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Interaction;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System;
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
    Removed,        //Unattached from mounting point
}

public class NetworkedItem : IdMonoBehaviour<ushort, NetworkedItem>
{
    #region Lookup Cache
    private static readonly Dictionary<ItemBase, NetworkedItem> itemBaseToNetworkedItem = [];

    public static List<NetworkedItem> GetAll()
    {
        return itemBaseToNetworkedItem.Values.Where(val => val.Item != null).ToList();
    }
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

    #region Server Variables
    public ServerPlayer BelongsTo { get; private set; }
    #endregion

    #region Common Variables
    public ItemBase Item { get; private set; }
    private GrabHandlerItem grabHandler;
    private SnappableItem snappableItem;
    private Component trackedItem;
    private readonly List<object> trackedValues = [];
    public bool UsefulItem { get; private set; }
    public Type TrackedItemType { get; private set; }
    public uint LastDirtyTick { get; private set; }
    private bool initialised;
    private bool registrationComplete = false;
    private readonly Queue<ItemUpdateData> pendingSnapshots = [];

    //Track dirty states
    private bool createdDirty = true;   //if set, we created this item dirty and have not sent an update
    private ItemState lastState;
    private bool stateDirty;
    private bool wasThrown;
    private bool wasRemoved;

    private Vector3 thrownPosition;
    private Quaternion thrownRotation;
    private Vector3 throwDirection;

    private bool processingAsHost = false;
    #endregion

    #region Client Variables
    public byte playerBelongsToId; // 0 means no owner
    public NetworkedPlayer playerBelongsTo;
    #endregion

    protected override bool IsIdServerAuthoritative => true;

    #region Unity Callbacks
    protected override void Awake()
    {
        base.Awake();

        if (NetworkLifecycle.Instance.IsHost())
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

    protected void LateUpdate()
    {
        var newState = GetItemState();

        if (lastState == newState && !HasDirtyValues())
            return;

        ItemUpdateData snapshot = GetSnapshot();

        if (!processingAsHost && snapshot != null)
            NetworkLifecycle.Instance.Client?.SendItemUpdatePacket(snapshot);
        else
            processingAsHost = false;
    }

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
            itemBaseToNetworkedItem.Remove(Item);
        }
        else
        {
            Multiplayer.LogWarning($"NetworkedItem.OnDestroy({name}, {NetId}) Item is null!");
        }

        base.OnDestroy();
    }
    #endregion

    #region Server
    public void Server_ReceiveItemUpdate(ItemUpdateData snapshot, ServerPlayer senderPlayer)
    {
        //TODO: rollback if validation fails
        if (!ValidateUpdate(snapshot, senderPlayer))
            return;

        Multiplayer.LogDebug(() => $"NetworkedItem.Server_ReceiveItemUpdate() NetId: {snapshot?.ItemNetId}, ItemState: {snapshot?.ItemState}, Player: {senderPlayer.DisplayName}");
        switch (snapshot.ItemState)
        {
            case ItemState.InHand:
            case ItemState.InInventory:
                BelongsTo = senderPlayer;
                break;
            case ItemState.Dropped:
            case ItemState.Thrown:
            case ItemState.Removed:
            case ItemState.Attached:
                BelongsTo = null;
                break;
        }

        // Ensure the playerId is set to the sender's playerId, rather than trusting the client value
        snapshot.PlayerId = senderPlayer.PlayerId;

        // Allow host to process packet if not from a local client
        if (!NetworkLifecycle.Instance.IsClientRunning || (NetworkLifecycle.Instance.IsClientRunning && senderPlayer.PlayerId != NetworkLifecycle.Instance.Server.SelfId))
        {
            processingAsHost = true;
            ReceiveSnapshot(snapshot);
        }

        //NetworkedItemManager.Instance.AddDirtyItemSnapshot(this, snapshot);
        NetworkLifecycle.Instance.Server.SendItemUpdatePacket(snapshot, excludePlayer: senderPlayer);
    }

    private bool ValidateUpdate(ItemUpdateData snapshot, ServerPlayer senderPlayer)
    {
        // Clients can not spawn or destroy items
        if (snapshot.UpdateType.HasAnyFlag(ItemUpdateData.ItemUpdateType.Create | ItemUpdateData.ItemUpdateType.Destroy))
            return false;

        if (snapshot.ItemState == ItemState.InHand)
        {
            if (BelongsTo != null && BelongsTo != senderPlayer)
            {
                Multiplayer.LogWarning($"NetworkedItem.ValidateUpdate() Player {senderPlayer?.DisplayName} attempted to grab item {NetId}, but it is held by {BelongsTo.DisplayName}");
                return false;
            }
        }

        if (snapshot.ItemState.HasAnyFlag(ItemState.InInventory | ItemState.Dropped | ItemState.Thrown))
        {
            if (BelongsTo != null && BelongsTo != senderPlayer)
            {
                Multiplayer.LogWarning($"NetworkedItem.ValidateUpdate() Player {senderPlayer?.DisplayName} attempted to update item {NetId} with state {snapshot.ItemState}, but it is held by {BelongsTo?.DisplayName}");

                return false;
            }
        }

        return true;
    }
    #endregion

    #region Client
    private void OnUngrabbed(ControlImplBase obj)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.OnUngrabbed() NetID: {NetId}, {name}");
        stateDirty = true;
    }

    private void OnGrabbed(ControlImplBase obj)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.OnGrabbed() NetID: {NetId}, {name}");

        playerBelongsToId = NetworkLifecycle.Instance.Client.PlayerId;
        stateDirty = true;
    }

    public void OnThrow(Vector3 direction)
    {
        playerBelongsToId = 0;

        //block a received throw from 
        if (wasThrown)
        {
            wasThrown = false;
            return;
        }

        throwDirection = direction;
        thrownPosition = Item.transform.position - WorldMover.currentMove;
        thrownRotation = Item.transform.rotation;

        //Multiplayer.LogDebug(() => $"NetworkedItem.OnThrow() netId: {NetId}, Name: {name}, Raw Position: {Item.transform.position}, Position: {thrownPosition}, Rotation: {thrownRotation}, Direction: {throwDirection}");

        wasThrown = true;
        stateDirty = true;
    }

    public void OnRemove()
    {
        playerBelongsToId = 0;
        playerBelongsTo = null;
        wasRemoved = true;
        stateDirty = true;
    }
    #endregion

    #region Common
    #region Initialisation

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

            //Find special interaction components
            TryGetComponent<GrabHandlerItem>(out grabHandler);
            TryGetComponent<SnappableItem>(out snappableItem);

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
    #endregion

    #region Item Value Tracking
    public void RegisterTrackedValue<T>(string key, Func<T> valueGetter, Action<T> valueSetter, Func<T, T, bool> thresholdComparer = null, bool serverAuthoritative = false)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.RegisterTrackedValue(\"{key}\", {valueGetter != null}, {valueSetter != null}, {thresholdComparer != null}, {serverAuthoritative}) itemNetId {NetId}, item name: {name}");
        trackedValues.Add(new TrackedValue<T>(key, valueGetter, valueSetter, thresholdComparer, serverAuthoritative));
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
            if (((dynamic)trackedValue).IsDirty)
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

        bool hasDirtyVals = HasDirtyValues();

        if (Item == null && Register() == false)
            return null;

        if (!stateDirty && !hasDirtyVals)
            return null;

        ItemState currentState = GetItemState();

        if (!createdDirty)
        {
            if (lastState != currentState)
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
        wasRemoved = false;

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
        Multiplayer.LogDebug(() => $"NetworkedItem.ApplySnapshot([netId: {snapshot?.ItemNetId}, ItemUpdateType: {snapshot?.UpdateType}, ItemState: {snapshot?.ItemState}, PlayerId: {snapshot?.PlayerId}, Active state: {gameObject.activeInHierarchy}])");

        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ItemState) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.FullSync) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create))
        {
            PrepareForStateChange(snapshot.PlayerId);

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
                case ItemState.Removed:
                    Item.GetComponent<GadgetBase>()?.Remove(true);
                    break;

                default:
                    throw new Exception($"NetworkedItem.ApplySnapshot() Item state not implemented: {snapshot?.ItemState}");
            }
        }

        Multiplayer.LogDebug(() => $"NetworkedItem.ApplySnapshot() netID: {snapshot?.ItemNetId}, ItemUpdateType {snapshot?.UpdateType} About to process states");

        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ObjectState))
        {
            Multiplayer.LogDebug(() => $"NetworkedItem.ApplySnapshot() netID: {snapshot?.ItemNetId}, States: {snapshot?.States?.Count}");
            if (trackedItem != null && snapshot.States != null)
            {
                ApplyTrackedValues(snapshot.States);
            }
        }

        Multiplayer.LogDebug(() => $"NetworkedItem.ApplySnapshot() netID: {snapshot?.ItemNetId}, ItemUpdateType {snapshot?.UpdateType} states processed");

        //mark values as clean
        createdDirty = false;
        stateDirty = false;

        MarkValuesClean();
    }

    public ItemUpdateData CreateUpdateData(ItemUpdateData.ItemUpdateType updateType)
    {
        if (transform == null || Item == null || Item?.InventorySpecs == null || Item?.InventorySpecs?.ItemPrefabName == null)
        {
            Multiplayer.LogDebug(() => $"NetworkedItem.CreateUpdateData({updateType}) NetId: {NetId}, name: {name}. Transform is null: {transform == null}, Item is null: {Item == null}, Inventory Specs: {Item?.InventorySpecs == null}, ItemPrefabName is null: {Item?.InventorySpecs?.ItemPrefabName == null}");
            return null;
        }

        Vector3 position;
        Quaternion rotation;
        Dictionary<string, object> states;
        ushort carId = 0;
        bool frontCoupler = true;

        if (wasThrown)
        {
            position = thrownPosition;
            rotation = thrownRotation;
        }
        else
        {
            position = transform.position - WorldMover.currentMove;
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
            States = states,
            PlayerId = playerBelongsToId,
        };

        return updateData;
    }

    private ItemState GetItemState()
    {
        //Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, Parent: {Item.transform.parent} WorldMover: {WorldMover.OriginShiftParent}, wasThrown: {wasThrown}, isGrabbed: {Item.IsGrabbed()} Inventory.Contains(): {Inventory.Instance.Contains(this.gameObject, false)} Storage.Contains: {StorageController.Instance.StorageInventory.ContainsItem(Item)}");


        if (Item.transform.parent == WorldMover.OriginShiftParent && !wasThrown)
        {
            //Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, Parent: {Item.transform.parent} WorldMover: {WorldMover.OriginShiftParent}, wasThrown: {wasThrown}");
            return ItemState.Dropped;
        }

        if (wasThrown)
        {
            //Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, Parent: {Item.transform.parent} WorldMover: {WorldMover.OriginShiftParent}, wasThrown: {wasThrown}");
            return ItemState.Thrown;
        }

        if (Item.IsGrabbed())
            return ItemState.InHand;

        if (Inventory.Instance.Contains(this.gameObject, false))
            return ItemState.InInventory;

        if (snappableItem != null && snappableItem.IsSnapped)
        {
            //Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, snapped! {this.transform.parent}");
            return ItemState.Attached;
        }

        if (wasRemoved)
        {
            //Multiplayer.LogDebug(() => $"GetItemState() NetId: {NetId}, {name}, was removed");
            return ItemState.Removed;
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
                Multiplayer.LogWarning($"Tracked value not found: {newValue.Key}\r\n {String.Join(", ", trackedValues.Select(val => ((dynamic)val).Key))}");
            }
        }
    }

    #region Item State Update Handlers

    private void PrepareForStateChange(byte playerId)
    {
        // Cleanup from previous state/desyncs
        if (Item.IsSnapped)
            Item.SnappableItem.SnappedTo.UnsnapItem(false); //Todo: should this be forced?

        if (playerBelongsTo != null && playerBelongsTo.PlayerId != playerId)
        {
            //TODO: fix for VR where item could be in either hand
            if (playerBelongsTo.RightHandItemGO == this.gameObject)
                playerBelongsTo.DropItem();
        }

        // find new player reference
        playerBelongsToId = playerId;
        playerBelongsTo = null;

        if (playerId != 0 && NetworkLifecycle.Instance.IsClientRunning)
            if (!NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerId, out playerBelongsTo))
                Multiplayer.LogWarning($"Unable to find player {playerId} for item {NetId}");
    }

    private void HandleDroppedOrThrownState(ItemUpdateData snapshot)
    {
        Multiplayer.LogDebug(() => $"NetworkedItem.HandleDroppedOrThrownState([netId: {snapshot?.ItemNetId}, ItemUpdateType: {snapshot?.UpdateType}, ItemState: {snapshot?.ItemState}, PlayerId: {snapshot?.PlayerId}, Active state: {gameObject.activeInHierarchy}])");

        //activate and relocate item
        gameObject.SetActive(true);
        transform.position = snapshot.ItemPosition + WorldMover.currentMove;
        transform.rotation = snapshot.ItemRotation;

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
        Multiplayer.LogDebug(() => $"NetworkedItem.HandleInventoryOrHandState() ItemNetId: {snapshot?.ItemNetId} State: {snapshot?.ItemState}. Player: {snapshot?.PlayerId}, Position: {snapshot?.ItemPosition}");


        if (snapshot.ItemState == ItemState.InHand)
        {
            if (playerBelongsTo != null)
            {
                Multiplayer.LogDebug(() => $"NetworkedItem.HandleInventoryOrHandState() Giving to player {playerBelongsTo.DisplayName}");
                var anchorOffsets = grabHandler?.GetAnchorOffsets();
                Vector3? pos = null;
                Quaternion? rot = null;
                if (anchorOffsets.HasValue)
                {
                    pos = anchorOffsets.Value.anchorPositionOffset;
                    rot = anchorOffsets.Value.anchorRotationOffset;
                }

                playerBelongsTo.HoldItem(this.gameObject, pos, rot);
                this.gameObject.SetActive(true);
            }
            else
            {
                Multiplayer.LogWarning($"Could not find player to hold item, disabling");
                this.gameObject.SetActive(false);
            }
        }
        else
        {
            if (playerBelongsTo != null)
                playerBelongsTo.AddItemToInventory(this.gameObject);
            else
                this.gameObject.SetActive(false);
        }
    }
    #endregion

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
    #endregion
}
