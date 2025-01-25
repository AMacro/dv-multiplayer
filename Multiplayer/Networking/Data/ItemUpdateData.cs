using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data;

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

    public ItemUpdateType UpdateType { get; set; }
    public ushort ItemNetId { get; set; }
    public string PrefabName { get; set; }
    public ItemState ItemState { get; set; }
    public Vector3 ItemPosition { get; set; }
    public Quaternion ItemRotation { get; set; }
    public Vector3 ThrowDirection { get; set; }
    public byte Player { get; set; }
    public ushort CarNetId { get; set; }
    public bool AttachedFront  { get; set; }
    public Dictionary<string, object> States { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)UpdateType);
        writer.Put(ItemNetId);

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        writer.Put((byte)ItemState);

        if (UpdateType.HasFlag(ItemUpdateType.Create))
            writer.Put(PrefabName);

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ItemState))
        {
            if (ItemState == ItemState.Dropped || ItemState == ItemState.Thrown) // || UpdateType.HasFlag(ItemUpdateType.ItemPosition)
            {
                Vector3Serializer.Serialize(writer, ItemPosition);
                QuaternionSerializer.Serialize(writer, ItemRotation);

                if (ItemState == ItemState.Thrown)
                    Vector3Serializer.Serialize(writer, ThrowDirection);
            }
            else if (ItemState == ItemState.InInventory || ItemState == ItemState.InHand)
            {
                writer.Put(Player);
            }
            else if (ItemState == ItemState.Attached)
            {
                writer.Put(CarNetId);
                writer.Put(AttachedFront);
            }
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            if (States == null)
                writer.Put(0);
            else
            {
                writer.Put(States.Count);
                foreach (var state in States)
                {
                    writer.Put(state.Key);
                    SerializeTrackedValue(writer, state.Value);
                }
            }
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        UpdateType = (ItemUpdateType)reader.GetByte();
        ItemNetId = reader.GetUShort();

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        ItemState = (ItemState)reader.GetByte();

        if (UpdateType.HasFlag(ItemUpdateType.Create))
            PrefabName = reader.GetString();

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ItemState))
        {
            if (ItemState == ItemState.Dropped || ItemState == ItemState.Thrown) // || UpdateType.HasFlag(ItemUpdateType.ItemPosition)
            {
                ItemPosition = Vector3Serializer.Deserialize(reader);
                ItemRotation = QuaternionSerializer.Deserialize(reader);

                if (ItemState == ItemState.Thrown)
                {
                    Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown before: {ThrowDirection}");
                    ThrowDirection = Vector3Serializer.Deserialize(reader);
                    Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown after: {ThrowDirection}");
                }
            }
            else if (ItemState == ItemState.InInventory || ItemState == ItemState.InHand)
            {
                Player = reader.GetByte();
            }
            else if (ItemState == ItemState.Attached)
            {
                CarNetId = reader.GetUShort();
                AttachedFront = reader.GetBool();
            }
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            int stateCount = reader.GetInt();
            if (stateCount > 0)
            {
                States = new Dictionary<string, object>();
                for (int i = 0; i < stateCount; i++)
                {
                    string key = reader.GetString();
                    object value = DeserializeTrackedValue(reader);
                    States[key] = value;
                }
            }
        }
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
