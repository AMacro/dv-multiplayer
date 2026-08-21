using LiteNetLib.Utils;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Numerics;

namespace Multiplayer.Networking.Data.Items;

public struct PlayerItemSaveData
{
    // Flags for data in the packet
    [Flags]
    private enum DataFlags : byte
    {
        None = 0,
        Position = 1,       // position data is not a zero vector
        Rotation = 2,       // rotation data is not a zero quaternion 
        CarGuid = 4,        // car GUID is not null
        ContainerId = 8,    // Container Id is not null/empty
        State = 16,         // State object is not null/empty
    }

    public ushort NetId { get; set; }
    public string ItemPrefabName { get; set; }
    public float ItemPositionX { get; set; }
    public float ItemPositionY { get; set; }
    public float ItemPositionZ { get; set; }
    public float ItemRotationX { get; set; }
    public float ItemRotationY { get; set; }
    public float ItemRotationZ { get; set; }
    public float ItemRotationW { get; set; }
    public bool BelongsToPlayer { get; set; }
    public bool IsGrabbed { get; set; }
    public string CarGuid { get; set; }
    public string ContainerId { get; set; }
    public JObject State { get; set; }
    public int InventorySlotIndex { get; set; }
    public int ContainerSlotIndex { get; set; }
    public bool InLockedSlot { get; set; }
    public bool IsDropped { get; set; }

    private Vector3 _position;
    [JsonIgnore]
    public Vector3 Position
    {
        get
        {
            if (_position == null || _position == default)
                _position = new Vector3(ItemPositionX, ItemPositionY, ItemPositionZ);

            return _position;
        }
    }

    private Quaternion _rotation;
    [JsonIgnore]
    public Quaternion Rotation
    {
        get
        {
            if (_rotation == null || _rotation == default)
                _rotation = new Quaternion(ItemRotationX, ItemRotationY, ItemRotationZ, ItemRotationW);

            return _rotation;
        }
    }

    public static void Serialize(NetDataWriter writer, PlayerItemSaveData data)
    {
        Guid carGuid = Guid.Empty;

        writer.Put(data.NetId);
        writer.Put(data.ItemPrefabName);

        // Determine which data is present
        bool hasPosition = data.ItemPositionX != 0 || data.ItemPositionY != 0 || data.ItemPositionZ != 0;
        bool hasRotation = data.ItemRotationX != 0 || data.ItemRotationY != 0 || data.ItemRotationZ != 0 || data.ItemRotationW != 0;
        bool hasCarGuid = !string.IsNullOrEmpty(data.CarGuid) && Guid.TryParse(data.CarGuid, out carGuid);
        bool hasContainerId = !string.IsNullOrEmpty(data.ContainerId);
        bool hasState = data.State != null;

        // Pack flags
        DataFlags flags = DataFlags.None;
        if (hasPosition) flags |= DataFlags.Position;
        if (hasRotation) flags |= DataFlags.Rotation;
        if (hasCarGuid) flags |= DataFlags.CarGuid;
        if (hasContainerId) flags |= DataFlags.ContainerId;
        if (hasState) flags |= DataFlags.State;

        writer.Put((byte)flags);

        // Write conditional data
        if (hasPosition)
        {
            writer.Put(data.ItemPositionX);
            writer.Put(data.ItemPositionY);
            writer.Put(data.ItemPositionZ);
        }

        if (hasRotation)
        {
            writer.Put(data.ItemRotationX);
            writer.Put(data.ItemRotationY);
            writer.Put(data.ItemRotationZ);
            writer.Put(data.ItemRotationW);
        }

        writer.Put(data.BelongsToPlayer);
        writer.Put(data.IsGrabbed);

        if (hasCarGuid)
            writer.Put(carGuid);

        if (hasContainerId)
            writer.Put(data.ContainerId);

        if (hasState)
            writer.Put(data.State.ToString());

        writer.Put(data.InventorySlotIndex);
        writer.Put(data.ContainerSlotIndex);
        writer.Put(data.InLockedSlot);
        writer.Put(data.IsDropped);
    }

    public static PlayerItemSaveData Deserialize(NetDataReader reader)
    {
        ushort itemNetId;
        string itemPrefabName;
        float posX = 0, posY = 0, posZ = 0;
        float rotX = 0, rotY = 0, rotZ = 0, rotW = 0;
        bool belongsToPlayer;
        bool isGrabbed;
        string carGuid = null;
        string containerId = null;
        JObject state = null;
        int inventorySlotIndex;
        int containerSlotIndex;
        bool inLockedSlot;
        bool isDropped;

        itemNetId = reader.GetUShort();
        itemPrefabName = reader.GetString();

        // Read flags
        DataFlags flags = (DataFlags)reader.GetByte();

        // Read conditional data
        if (flags.HasFlag(DataFlags.Position))
        {
            posX = reader.GetFloat();
            posY = reader.GetFloat();
            posZ = reader.GetFloat();
        }

        if (flags.HasFlag(DataFlags.Rotation))
        {
            rotX = reader.GetFloat();
            rotY = reader.GetFloat();
            rotZ = reader.GetFloat();
            rotW = reader.GetFloat();
        }

        belongsToPlayer = reader.GetBool();
        isGrabbed = reader.GetBool();

        if (flags.HasFlag(DataFlags.CarGuid))
            carGuid = reader.GetGuid().ToString();

        if (flags.HasFlag(DataFlags.ContainerId))
            containerId = reader.GetString();

        if (flags.HasFlag(DataFlags.State))
            state = JObject.Parse(reader.GetString());

        inventorySlotIndex = reader.GetInt();
        containerSlotIndex = reader.GetInt();
        inLockedSlot = reader.GetBool();
        isDropped = reader.GetBool();

        return new PlayerItemSaveData
        {
            NetId = itemNetId,
            ItemPrefabName = itemPrefabName,
            ItemPositionX = posX,
            ItemPositionY = posY,
            ItemPositionZ = posZ,
            ItemRotationX = rotX,
            ItemRotationY = rotY,
            ItemRotationZ = rotZ,
            ItemRotationW = rotW,
            BelongsToPlayer = belongsToPlayer,
            IsGrabbed = isGrabbed,
            CarGuid = carGuid,
            ContainerId = containerId,
            State = state,
            InventorySlotIndex = inventorySlotIndex,
            ContainerSlotIndex = containerSlotIndex,
            InLockedSlot = inLockedSlot,
            IsDropped = isDropped,
        };
    }
}
