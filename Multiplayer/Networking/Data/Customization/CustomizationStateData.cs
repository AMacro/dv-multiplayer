using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using Multiplayer.Networking.Data.Items;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data.Customization;

public enum CustomizationTargetKind : byte
{
    TrainCar,
    World,
    Storage,
    PlayerHouse,
    PaintStation,
}

public struct CustomizationRefData
{
    public CustomizationTargetKind Kind;
    public ushort TrainCarNetId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Kind);
        writer.Put(TrainCarNetId);
    }

    public static CustomizationRefData Deserialize(NetDataReader reader)
    {
        return new CustomizationRefData
        {
            Kind = (CustomizationTargetKind)reader.GetByte(),
            TrainCarNetId = reader.GetUShort(),
        };
    }
}

public sealed class GadgetPlacementData
{
    public ushort ItemNetId;
    public string PrefabName;
    public CustomizationRefData Target;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public bool IsOnGlass;
    public Dictionary<string, object> TrackedValues = new();

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(ItemNetId);
        writer.Put(PrefabName ?? string.Empty);
        Target.Serialize(writer);
        Vector3Serializer.Serialize(writer, LocalPosition);
        QuaternionSerializer.Serialize(writer, LocalRotation);
        writer.Put(IsOnGlass);
        TrackedStateSerializer.Serialize(writer, TrackedValues);
    }

    public static GadgetPlacementData Deserialize(NetDataReader reader)
    {
        return new GadgetPlacementData
        {
            ItemNetId = reader.GetUShort(),
            PrefabName = reader.GetString(),
            Target = CustomizationRefData.Deserialize(reader),
            LocalPosition = Vector3Serializer.Deserialize(reader),
            LocalRotation = QuaternionSerializer.Deserialize(reader),
            IsOnGlass = reader.GetBool(),
            TrackedValues = TrackedStateSerializer.Deserialize(reader),
        };
    }
}

public sealed class CustomizationHoleData
{
    public CustomizationRefData Target;
    public Vector3 LocalPosition;
    public Vector3 LocalNormal;

    public void Serialize(NetDataWriter writer)
    {
        Target.Serialize(writer);
        Vector3Serializer.Serialize(writer, LocalPosition);
        Vector3Serializer.Serialize(writer, LocalNormal);
    }

    public static CustomizationHoleData Deserialize(NetDataReader reader)
    {
        return new CustomizationHoleData
        {
            Target = CustomizationRefData.Deserialize(reader),
            LocalPosition = Vector3Serializer.Deserialize(reader),
            LocalNormal = Vector3Serializer.Deserialize(reader),
        };
    }
}

public sealed class CustomizationStateData
{
    public List<ItemUpdateData> RelatedItems = new();
    public List<GadgetPlacementData> Gadgets = new();
    public List<GadgetRelationshipData> Relationships = new();
    public List<CustomizationHoleData> Holes = new();

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(RelatedItems.Count);
        foreach (var item in RelatedItems)
            item.Serialize(writer);

        writer.Put(Gadgets.Count);
        foreach (var gadget in Gadgets)
            gadget.Serialize(writer);

        writer.Put(Relationships.Count);
        foreach (var relationship in Relationships)
            relationship.Serialize(writer);

        writer.Put(Holes.Count);
        foreach (var hole in Holes)
            hole.Serialize(writer);
    }

    public static CustomizationStateData Deserialize(NetDataReader reader)
    {
        var result = new CustomizationStateData();

        int relatedItemCount = reader.GetInt();
        for (int i = 0; i < relatedItemCount; i++)
        {
            var item = new ItemUpdateData();
            item.Deserialize(reader);
            result.RelatedItems.Add(item);
        }

        int gadgetCount = reader.GetInt();
        for (int i = 0; i < gadgetCount; i++)
            result.Gadgets.Add(GadgetPlacementData.Deserialize(reader));

        int relationshipCount = reader.GetInt();
        for (int i = 0; i < relationshipCount; i++)
            result.Relationships.Add(GadgetRelationshipData.Deserialize(reader));

        int holeCount = reader.GetInt();
        for (int i = 0; i < holeCount; i++)
            result.Holes.Add(CustomizationHoleData.Deserialize(reader));

        return result;
    }
}

public sealed class GadgetRelationshipData
{
    public byte Action;
    public ushort ItemA;
    public ushort ItemB;
    public int IndexA;
    public int IndexB;
    public bool HasPosition;
    public Vector3 Position;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Action);
        writer.Put(ItemA);
        writer.Put(ItemB);
        writer.Put(IndexA);
        writer.Put(IndexB);
        writer.Put(HasPosition);
        if (HasPosition)
            Vector3Serializer.Serialize(writer, Position);
    }

    public static GadgetRelationshipData Deserialize(NetDataReader reader)
    {
        var relationship = new GadgetRelationshipData
        {
            Action = reader.GetByte(),
            ItemA = reader.GetUShort(),
            ItemB = reader.GetUShort(),
            IndexA = reader.GetInt(),
            IndexB = reader.GetInt(),
            HasPosition = reader.GetBool(),
        };
        if (relationship.HasPosition)
            relationship.Position = Vector3Serializer.Deserialize(reader);
        return relationship;
    }
}

internal static class TrackedStateSerializer
{
    public static void Serialize(NetDataWriter writer, Dictionary<string, object> values)
    {
        writer.Put(values?.Count ?? 0);
        if (values == null)
            return;

        foreach (var value in values)
        {
            writer.Put(value.Key);
            SerializeValue(writer, value.Value);
        }
    }

    public static Dictionary<string, object> Deserialize(NetDataReader reader)
    {
        int count = reader.GetInt();
        var result = new Dictionary<string, object>(count);
        for (int i = 0; i < count; i++)
            result[reader.GetString()] = DeserializeValue(reader);
        return result;
    }

    private static void SerializeValue(NetDataWriter writer, object value)
    {
        switch (value)
        {
            case bool boolValue:
                writer.Put((byte)0);
                writer.Put(boolValue);
                break;
            case int intValue:
                writer.Put((byte)1);
                writer.Put(intValue);
                break;
            case uint uintValue:
                writer.Put((byte)2);
                writer.Put(uintValue);
                break;
            case float floatValue:
                writer.Put((byte)3);
                writer.Put(floatValue);
                break;
            case string stringValue:
                writer.Put((byte)4);
                writer.Put(stringValue);
                break;
            default:
                throw new System.NotSupportedException($"Unsupported customization tracked value type: {value?.GetType()}");
        }
    }

    private static object DeserializeValue(NetDataReader reader)
    {
        return reader.GetByte() switch
        {
            0 => reader.GetBool(),
            1 => reader.GetInt(),
            2 => reader.GetUInt(),
            3 => reader.GetFloat(),
            4 => reader.GetString(),
            byte typeCode => throw new System.NotSupportedException($"Unsupported customization tracked value type code: {typeCode}"),
        };
    }
}
