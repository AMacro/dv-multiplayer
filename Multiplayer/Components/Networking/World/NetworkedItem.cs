using DV.CabControls;
using DV.CabControls.Spec;
using DV.InventorySystem;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedItem : IdMonoBehaviour<ushort, NetworkedItem>
{
    #region Lookup Cache
    private static readonly Dictionary<ItemBase, NetworkedItem> itemBaseToNetworkedItem = new();

    public static List<NetworkedItem> GetAll()
    {
        return itemBaseToNetworkedItem.Values.ToList();
    }
    public static bool Get(ushort netId, out NetworkedItem obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedItem> rawObj);
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
    #endregion

    private const float PositionThreshold = 0.01f;
    private const float RotationThreshold = 0.01f;

    public ItemBase Item { get; private set; }
    private Component trackedItem;
    private List<object> trackedValues = new List<object>();
    public bool UsefulItem { get; private set; } = false;
    public Type TrackedItemType { get; private set; }
    public bool BlockSync { get; set; } = false;
    public uint LastDirtyTick { get; private set; }

    //Track dirty states
    private bool CreatedDirty = true;   //if set, we created this item dirty and have not sent an update

    private bool ItemGrabbed = false;   //Current state of item grabbed
    private bool GrabbedDirty = false;  //Current state is dirty

    private bool ItemDropped = false;   //Current state of item dropped
    private bool DroppedDirty = false;  //Current state is dirty

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private ItemPositionData ItemPosition;
    private bool PositionDirty = false;

    //Handle ownership
    public ushort OwnerId { get; private set; } = 0; // 0 means no owner

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
        Multiplayer.LogDebug(() => $"NetworkedItem.Awake() {name}");
        NetworkedItemManager.Instance.CheckInstance(); //Ensure the NetworkedItemManager is initialised

        Register();
    }

    protected void Start()
    {
        if (!CreatedDirty)
            return;


        ItemGrabbed = Item.IsGrabbed();
        ItemDropped = Item.transform.parent == WorldMover.OriginShiftParent;

        //if (StorageController.Instance.IsInStorageWorld(Item) )
        //{
        //    ItemDropped = true;
        //}
    }

    public T GetTrackedItem<T>() where T : Component
    {
        return UsefulItem ? trackedItem as T : null;
    }

    public void Initialize<T>(T item, ushort netId = 0, bool createDirty = true) where T : Component
    {
        if(netId != 0)
            NetId = netId;

        trackedItem = item;
        TrackedItemType = typeof(T);
        UsefulItem = true;

        CreatedDirty = createDirty;

        if(Item == null)
            Register();

    }

    private bool Register()
    {
        try
        {

            if (!TryGetComponent(out ItemBase itemBase))
            {
                Multiplayer.LogError($"Unable to find ItemBase for {name}");
                return false;
            }

            Item = itemBase;
            itemBaseToNetworkedItem[Item] = this;

            Item.Grabbed += OnGrabbed;
            Item.Ungrabbed += OnUngrabbed;
            Item.ItemInventoryStateChanged += OnItemInventoryStateChanged;

            lastPosition = Item.transform.position - WorldMover.currentMove;
            lastRotation = Item.transform.rotation;

            return true;
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"Unable to find ItemBase for {name}\r\n{ex.Message}");
            return false; 
        }
    }

    private void OnUngrabbed(ControlImplBase obj)
    {
        Multiplayer.LogDebug(() => $"OnUngrabbed() {name}");
        GrabbedDirty = ItemGrabbed == true;
        ItemGrabbed = false;
        
    }

    private void OnGrabbed(ControlImplBase obj)
    {
        Multiplayer.LogDebug(() => $"OnGrabbed() {name}");
        GrabbedDirty = ItemGrabbed == false;
        ItemGrabbed = true;
    }

    private void OnItemInventoryStateChanged(ItemBase itemBase, InventoryActionType actionType, InventoryItemState itemState)
    {
        Multiplayer.LogDebug(() => $"OnItemInventoryStateChanged() {name}, InventoryActionType: {actionType}, InventoryItemState: {itemState}");
        if (actionType == InventoryActionType.Purge)
        {
            DroppedDirty = true;
            ItemDropped = true;
        }

    }

    #region Item Value Tracking
    public void RegisterTrackedValue<T>(string key, Func<T> valueGetter, Action<T> valueSetter)
    {
        trackedValues.Add(new TrackedValue<T>(key, valueGetter, valueSetter));
    }

    private bool HasDirtyValues()
    {
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

    private void MarkValuesClean()
    {
        foreach (var trackedValue in trackedValues)
        {
            ((dynamic)trackedValue).MarkClean();
        }
    }

    private void CheckPositionChange()
    {
        Vector3 currentPosition = transform.position - WorldMover.currentMove;
        Quaternion currentRotation = transform.rotation;

        bool positionChanged = Vector3.Distance(currentPosition, lastPosition) > PositionThreshold;
        bool rotationChanged = Quaternion.Angle(currentRotation, lastRotation) > RotationThreshold;

        //We don't care about position and rotation if the player is holding it, as it will move relative to the player
        if ((positionChanged || rotationChanged) && !ItemGrabbed)
        {
            ItemPosition = new ItemPositionData
            {
                Position = currentPosition,
                Rotation = currentRotation
            };
            lastPosition = currentPosition;
            lastRotation = currentRotation;
            PositionDirty = true;
        }
    }

    public ItemUpdateData GetSnapshot()
    {
        ItemUpdateData snapshot;
        ItemUpdateData.ItemUpdateType updateType = ItemUpdateData.ItemUpdateType.None;

        if (Item == null && Register() == false)
            return null;

        CheckPositionChange();

        if (!CreatedDirty)
        {
            if(PositionDirty)
                updateType |= ItemUpdateData.ItemUpdateType.Position;
            if(DroppedDirty)
                updateType |= ItemUpdateData.ItemUpdateType.ItemDropped;
            if(GrabbedDirty)
                updateType |= ItemUpdateData.ItemUpdateType.ItemEquipped;
            if (HasDirtyValues())
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

        LastDirtyTick = NetworkLifecycle.Instance.Tick;
        snapshot = CreateUpdateData(updateType);

        CreatedDirty = false;
        GrabbedDirty = false;
        DroppedDirty = false;
        PositionDirty = false;

        MarkValuesClean();

        return snapshot;
    }

    public void ReceiveSnapshot(ItemUpdateData snapshot)
    {
        if(snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.None)
            return;

        //Multiplayer.LogDebug(()=>$"NetworkedItem.ReceiveSnapshot() netID: {snapshot.ItemNetId}, {snapshot.UpdateType}");

        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ItemEquipped) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create))
        {
            //do something when a player equips/unequips an item
            Multiplayer.Log($"NetworkedItem.ReceiveSnapshot() netID: {snapshot.ItemNetId}, Equipped: {snapshot.Equipped}, Player ID: {snapshot.Player}");
            //OwnerId = snapshot.Player;
            //if(OwnerId != NetworkLifecycle.Instance.Client.selfPeer.RemoteId)
        }

        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.ItemDropped) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create))
        {
            //do something when a player drops/picks up an item
            Multiplayer.Log($"NetworkedItem.ReceiveSnapshot() netID: {snapshot.ItemNetId}, Dropped: {snapshot.Dropped}, Player ID: {snapshot.Player}");
            //Item.gameObject.SetActive(snapshot.Dropped);
        }

        if (snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Position) || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create))
        { 
            //update all values
            transform.position = snapshot.PositionData.Position + WorldMover.currentMove;
            transform.rotation = snapshot.PositionData.Rotation;
        }

        if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.ObjectState || snapshot.UpdateType.HasFlag(ItemUpdateData.ItemUpdateType.Create))
        {
            //Multiplayer.Log($"NetworkedItem.ReceiveSnapshot() netID: {snapshot.ItemNetId}, States: {snapshot?.States?.Count}");

            foreach (var state in snapshot.States)
            {
                var trackedValue = trackedValues.Find(tv => ((dynamic)tv).Key == state.Key);
                if (trackedValue != null)
                {
                    try
                    {
                        ((dynamic)trackedValue).SetValueFromObject(state.Value);
                        Multiplayer.LogDebug(() => $"Updated tracked value: {state.Key}");
                    }
                    catch (Exception ex)
                    {
                        Multiplayer.LogError($"Error updating tracked value {state.Key}: {ex.Message}");
                    }
                }
                else
                {
                    Multiplayer.LogWarning($"Tracked value not found: {state.Key}");
                }
            }
        }

        //mark values as clean
        CreatedDirty = false;
        GrabbedDirty = false;
        DroppedDirty = false;
        PositionDirty = false;

        MarkValuesClean();
        return;
    }
    #endregion

    public ItemUpdateData CreateUpdateData(ItemUpdateData.ItemUpdateType updateType)
    {
        Multiplayer.LogDebug(() => $"NetworkedItem.CreateUpdateData({updateType}) NetId: {NetId}, name: {name}");
   
        var updateData = new ItemUpdateData
        {
            UpdateType = updateType,
            ItemNetId = NetId,
            PrefabName = Item.name,
            PositionData = ItemPosition,
            Equipped = ItemGrabbed,
            Dropped = ItemDropped,
            States = GetDirtyStateData(),
        };

        return updateData;
    }


    protected override void OnDestroy()
    {
        if (UnloadWatcher.isQuitting || UnloadWatcher.isUnloading)
            return;

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkedItemManager.Instance.AddDirtyItemSnapshot(this, CreateUpdateData(ItemUpdateData.ItemUpdateType.Destroy));
        }
        /*
        else if(!BlockSync)
        {
            Multiplayer.LogWarning($"NetworkedItem.OnDestroy({name}, {NetId})");/*\r\n{new System.Diagnostics.StackTrace()}
        }
        else
        {
            Multiplayer.LogDebug(()=>$"NetworkedItem.OnDestroy({name}, {NetId})");/*\r\n{new System.Diagnostics.StackTrace()}
        }*/

        if (Item != null)
        {
            Item.Grabbed -= OnGrabbed;
            Item.Ungrabbed -= OnUngrabbed;
            Item.ItemInventoryStateChanged -= OnItemInventoryStateChanged;
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
        sb.AppendLine($"Dirty values for NetworkedItem {name}, NetId {NetId}:");
        foreach (var value in dirtyValues)
        {
            sb.AppendLine(((dynamic)value).GetDebugString());
        }
        return sb.ToString();
    }
}
