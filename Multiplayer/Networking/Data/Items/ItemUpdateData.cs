using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data.Items;

public class ItemUpdateData
{
    [Flags]
    public enum ItemUpdateType : byte
    {
        None = 0,
        Create = 1,
        Destroy = 2,
        ItemState = 4,
        ItemPosition = 8,
        ObjectState = 16,
        FullSync = ItemState | ItemPosition | ObjectState,
    }

    public enum Hand : byte
    {
        nonVR = 0,
        Left = 1,
        Right = 2
    }

    public ItemUpdateType UpdateType { get; set; }
    public ushort ItemNetId { get; set; }
    public string PrefabName { get; set; }
    public ItemState ItemState { get; set; }
    public Vector3 ItemPosition { get; set; }
    public Quaternion ItemRotation { get; set; }
    public Vector3 ThrowDirection { get; set; }
    public byte PlayerId { get; set; }
    public ushort CarNetId { get; set; }
    public bool AttachedFront  { get; set; }
    public Dictionary<string, object> States { get; set; }
    public Hand PlayerHand { get; set; }

    public static void Serialize(NetDataWriter writer, ItemUpdateData data)
    {
        writer.Put((byte)data.UpdateType);
        writer.Put(data.ItemNetId);

        if (data.UpdateType == ItemUpdateType.Destroy)
            return;

        writer.Put((byte)data.ItemState);

        if (data.UpdateType.HasFlag(ItemUpdateType.Create))
            writer.Put(data.PrefabName);

        if (data.UpdateType.HasFlag(ItemUpdateType.Create) || data.UpdateType.HasFlag(ItemUpdateType.ItemState))
        {
            if (data.ItemState == ItemState.Dropped || data.ItemState == ItemState.Thrown) // || data.UpdateType.HasFlag(ItemUpdateType.ItemPosition)
            {
                Vector3Serializer.Serialize(writer, data.ItemPosition);
                QuaternionSerializer.Serialize(writer, data.ItemRotation);

                if (data.ItemState == ItemState.Thrown)
                    Vector3Serializer.Serialize(writer, data.ThrowDirection);
            }
            else if (data.ItemState == ItemState.InInventory || data.ItemState == ItemState.InHand)
            {
                writer.Put(data.PlayerId);
            }
            else if (data.ItemState == ItemState.Attached)
            {
                writer.Put(data.CarNetId);
                writer.Put(data.AttachedFront);
            }
        }

        if (data.UpdateType.HasFlag(ItemUpdateType.Create) || data.UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            if (data.States == null)
                writer.Put(0);
            else
            {
                writer.Put(data.States.Count);
                foreach (var state in data.States)
                {
                    writer.Put(state.Key);
                    data.SerializeTrackedValue(writer, state.Value);
                }
            }
        }
    }

    public static ItemUpdateData Deserialize(NetDataReader reader)
    {
        ItemUpdateData data = new();

        data.UpdateType = (ItemUpdateType)reader.GetByte();
        data.ItemNetId = reader.GetUShort();

        if (data.UpdateType == ItemUpdateType.Destroy)
            return data;
        data.ItemState = (ItemState)reader.GetByte();

        if (data.UpdateType.HasFlag(ItemUpdateType.Create))
            data.PrefabName = reader.GetString();
        if (data.UpdateType.HasFlag(ItemUpdateType.Create) || data.UpdateType.HasFlag(ItemUpdateType.ItemState))
        {
            if (data.ItemState == ItemState.Dropped || data.ItemState == ItemState.Thrown) // || data.UpdateType.HasFlag(ItemUpdateType.ItemPosition)
            {
                data.ItemPosition = Vector3Serializer.Deserialize(reader);
                data.ItemRotation = QuaternionSerializer.Deserialize(reader);

                if (data.ItemState == ItemState.Thrown)
                {
                    Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown before: {data.ThrowDirection}");
                    data.ThrowDirection = Vector3Serializer.Deserialize(reader);
                    Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown after: {data.ThrowDirection}");
                }
            }
            else if (data.ItemState == ItemState.InInventory || data.ItemState == ItemState.InHand)
            {
                data.PlayerId = reader.GetByte();
            }
            else if (data.ItemState == ItemState.Attached)
            {
                data.CarNetId = reader.GetUShort();
                data.AttachedFront = reader.GetBool();
            }
        }

        if (data.UpdateType.HasFlag(ItemUpdateType.Create) || data.UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            int stateCount = reader.GetInt();
            if (stateCount > 0)
            {
                data.States = new Dictionary<string, object>();
                for (int i = 0; i < stateCount; i++)
                {
                    string key = reader.GetString();
                    object value = data.DeserializeTrackedValue(reader);
                    data.States[key] = value;
                }
            }
        }

        return data;
    }

    private void SerializeTrackedValue(NetDataWriter writer, object value)
    {
        if (value is bool boolValue)
        {
            writer.Put((byte)0);
            writer.Put(boolValue);
        }
        else if (value is int intValue)
        {
            writer.Put((byte)1);
            writer.Put(intValue);
        }
        else if (value is uint uintValue)
        {
            writer.Put((byte)2);
            writer.Put(uintValue);
        }
        else if (value is float floatValue)
        {
            writer.Put((byte)3);
            writer.Put(floatValue);
        }
        else if (value is string stringValue)
        {
            writer.Put((byte)4);
            writer.Put(stringValue);
        }
        else
        {
            throw new NotSupportedException($"ItemUpdateData.SerializeTrackedValue({ItemNetId}, {PrefabName??""}) Unsupported type for serialization: {value.GetType()}");
        }
    }

    private object DeserializeTrackedValue(NetDataReader reader)
    {
        byte typeCode = reader.GetByte();
        switch (typeCode)
        {
            case 0: return reader.GetBool();
            case 1: return reader.GetInt();
            case 2: return reader.GetUInt();
            case 3: return reader.GetFloat();
            case 4: return reader.GetString();

            default:
                throw new NotSupportedException($"ItemUpdateData.DeserializeTrackedValue({ItemNetId}, {PrefabName ?? ""}) Unsupported type code for deserialization: {typeCode}");
        }
    }
}
