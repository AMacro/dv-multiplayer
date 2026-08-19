using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using System.Collections.Generic;
using UnityEngine;
using Multiplayer.Networking.Packets.Common.Customization;

namespace Multiplayer.Networking.Data.Customization;

public struct CustomizationRefData
{
    public string IdentificationKey;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(IdentificationKey ?? string.Empty);
    }

    public static CustomizationRefData Deserialize(NetDataReader reader)
    {
        return new CustomizationRefData
        {
            IdentificationKey = reader.GetString(),
        };
    }
}

public sealed class GadgetPlacementData
{
    public ushort ItemNetId;
    public uint SentTick;
    public CustomizationRefData Target;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public Dictionary<string, object> TrackedValues = new();

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(ItemNetId);
        writer.Put(SentTick);
        Target.Serialize(writer);
        Vector3Serializer.Serialize(writer, LocalPosition);
        QuaternionSerializer.Serialize(writer, LocalRotation);
        TrackedValueSerializer.SerializeDictionary(writer, TrackedValues);
    }

    public static GadgetPlacementData Deserialize(NetDataReader reader)
    {
        return new GadgetPlacementData
        {
            ItemNetId = reader.GetUShort(),
            SentTick = reader.GetUInt(),
            Target = CustomizationRefData.Deserialize(reader),
            LocalPosition = Vector3Serializer.Deserialize(reader),
            LocalRotation = QuaternionSerializer.Deserialize(reader),
            TrackedValues = TrackedValueSerializer.DeserializeDictionary(reader),
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
    private const int MaxTargets = 4096;
    private const int MaxGadgets = 4096;
    private const int MaxRelationships = 16384;
    private const int MaxHoles = 16384;
    private const int MaxProcessedActions = 65536;

    public List<uint> ProcessedOriginActionIds = new();
    public List<string> CustomizationKeys = new();
    public List<GadgetPlacementData> Gadgets = new();
    internal List<MountGadgetPacket> Mounts = new();
    internal List<WireGadgetsPacket> Wires = new();
    internal List<LoadSpoolPacket> LoadedSpools = new();
    internal List<SnapItemPacket> SnappedItems = new();
    public List<CustomizationHoleData> Holes = new();

    public void Serialize(NetDataWriter writer)
    {
        ValidateCount(CustomizationKeys.Count, MaxTargets, nameof(CustomizationKeys));
        ValidateCount(Gadgets.Count, MaxGadgets, nameof(Gadgets));
        ValidateCount(Mounts.Count + Wires.Count + LoadedSpools.Count + SnappedItems.Count,
            MaxRelationships, "relationships");
        ValidateCount(Holes.Count, MaxHoles, nameof(Holes));
        ValidateCount(ProcessedOriginActionIds.Count, MaxProcessedActions, nameof(ProcessedOriginActionIds));

        writer.Put(ProcessedOriginActionIds.Count);
        foreach (uint actionId in ProcessedOriginActionIds)
            writer.Put(actionId);

        writer.Put(CustomizationKeys.Count);
        foreach (var key in CustomizationKeys)
            writer.Put(key);

        writer.Put(Gadgets.Count);
        foreach (var gadget in Gadgets)
            gadget.Serialize(writer);

        WritePackets(writer, Mounts);
        WritePackets(writer, Wires);
        WritePackets(writer, LoadedSpools);
        WritePackets(writer, SnappedItems);

        writer.Put(Holes.Count);
        foreach (var hole in Holes)
            hole.Serialize(writer);
    }

    public static CustomizationStateData Deserialize(NetDataReader reader)
    {
        var result = new CustomizationStateData();

        int processedActionCount = ReadCount(reader, MaxProcessedActions, nameof(ProcessedOriginActionIds));
        for (int i = 0; i < processedActionCount; i++)
            result.ProcessedOriginActionIds.Add(reader.GetUInt());

        int customizationCount = ReadCount(reader, MaxTargets, nameof(CustomizationKeys));
        for (int i = 0; i < customizationCount; i++)
            result.CustomizationKeys.Add(reader.GetString());

        int gadgetCount = ReadCount(reader, MaxGadgets, nameof(Gadgets));
        for (int i = 0; i < gadgetCount; i++)
            result.Gadgets.Add(GadgetPlacementData.Deserialize(reader));

        ReadPackets(reader, result.Mounts);
        ReadPackets(reader, result.Wires);
        ReadPackets(reader, result.LoadedSpools);
        ReadPackets(reader, result.SnappedItems);
        ValidateCount(result.Mounts.Count + result.Wires.Count + result.LoadedSpools.Count +
            result.SnappedItems.Count, MaxRelationships, "relationships");

        int holeCount = ReadCount(reader, MaxHoles, nameof(Holes));
        for (int i = 0; i < holeCount; i++)
            result.Holes.Add(CustomizationHoleData.Deserialize(reader));

        return result;
    }

    private static int ReadCount(NetDataReader reader, int maximum, string field)
    {
        int count = reader.GetInt();
        ValidateCount(count, maximum, field);
        return count;
    }

    private static void WritePackets<T>(NetDataWriter writer, List<T> packets)
        where T : INetSerializable
    {
        writer.Put(packets.Count);
        foreach (var packet in packets)
            packet.Serialize(writer);
    }

    private static void ReadPackets<T>(NetDataReader reader, List<T> packets)
        where T : INetSerializable, new()
    {
        int count = ReadCount(reader, MaxRelationships, typeof(T).Name);
        for (int i = 0; i < count; i++)
        {
            var packet = new T();
            packet.Deserialize(reader);
            packets.Add(packet);
        }
    }

    private static void ValidateCount(int count, int maximum, string field)
    {
        if (count < 0 || count > maximum)
            throw new System.InvalidOperationException($"Invalid customization {field} count: {count}");
    }
}
