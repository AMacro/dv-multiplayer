
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public class ItemData
{
    public ushort netId;
    public StorageItemData StorageData;

    public ItemData() { }

    public ItemData(ushort netId, StorageItemData storageData)
    {
        this.netId = netId;
        StorageData = storageData;
    }

    public static ItemData From(NetworkedItem netItem)
    {
        return new ItemData(netItem.NetId, netItem.StorageData);
    }

    public static ItemData[] From(NetworkedItem[] netItem)
    {
        List<ItemData> data = new List<ItemData>();

        foreach (NetworkedItem item in netItem)
        {
            data.Add(From(item));
        }

        return data.ToArray();
    }
    public static void Serialize(NetDataWriter writer, ItemData item)
    {
        //Multiplayer.Log($"NetworkedItem.Serialize()");

        writer.Put(item.netId);
        //Multiplayer.Log($"NetworkedItem.Serialize() NetId");

        writer.Put(item.StorageData.itemPrefabName);

        //Multiplayer.Log($"NetworkedItem.Serialize() Prefab Name");
        writer.Put(item.StorageData.itemPositionX);
        writer.Put(item.StorageData.itemPositionY);
        writer.Put(item.StorageData.itemPositionZ);
        writer.Put(item.StorageData.itemRotationX);
        writer.Put(item.StorageData.itemRotationY);
        writer.Put(item.StorageData.itemRotationZ);
        writer.Put(item.StorageData.itemRotationW);

        //Multiplayer.Log($"NetworkedItem.Serialize() Coords");
        writer.Put(item.StorageData.belongsToPlayer);
        writer.Put(item.StorageData.isGrabbed);
        writer.Put(item.StorageData.inventorySlotIndex);
        writer.Put(item.StorageData.inLockedSlot);
        writer.Put(item.StorageData.isDropped);
        //Multiplayer.Log($"NetworkedItem.Serialize() States");

        writer.Put(item.StorageData.carGuid);

        //Multiplayer.Log($"NetworkedItem.Serialize() GuID");
        writer.Put(item.StorageData?.state?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty);
        //Multiplayer.Log($"NetworkedItem.Serialize() JSon");
    }

    public static ItemData Deserialize(NetDataReader reader)
    {
        //Multiplayer.Log($"NetworkedItem.Deserialize()");
        ItemData netItem = new ItemData();

        netItem.netId = reader.GetUShort();
        //Multiplayer.Log($"NetworkedItem.Deserialize() NetId");

        string itemPrefabName = reader.GetString();
        //Multiplayer.Log($"NetworkedItem.Deserialize() Prefab Name");

        Vector3 itemPosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        Quaternion itemRotation = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        //Multiplayer.Log($"NetworkedItem.Deserialize() Coords");

        bool belongsToPlayer = reader.GetBool();
        bool isGrabbed = reader.GetBool();
        int inventorySlotIndex = reader.GetInt();
        bool inLockedSlot = reader.GetBool();
        bool isDropped = reader.GetBool();
        //Multiplayer.Log($"NetworkedItem.Deserialize() States");

        string carGuid = reader.GetString();
        //Multiplayer.Log($"NetworkedItem.Deserialize() GuID");

        string state = reader.GetString();
        //Multiplayer.Log($"NetworkedItem.Deserialize() JSon");

        JObject jstate = null;
        if (state != null && state != string.Empty)
            jstate = new JObject(state);

        netItem.StorageData = new StorageItemData(itemPrefabName, itemPosition, itemRotation, belongsToPlayer, isGrabbed, carGuid, jstate, inventorySlotIndex, inLockedSlot, isDropped);
        return netItem;
    }
}
