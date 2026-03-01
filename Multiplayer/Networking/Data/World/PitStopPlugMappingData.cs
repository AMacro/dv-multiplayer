using DV.ThingTypes;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using System.Collections.Generic;

namespace Multiplayer.Networking.Data.World;

public readonly struct PitStopPlugMappingData(ushort netId, Dictionary<ResourceType, ushort> plugMapping)
{
    public readonly ushort NetId = netId;
    public readonly Dictionary<ResourceType, ushort> PlugMapping = plugMapping;


    public static PitStopPlugMappingData From(NetworkedPitStopStation netStation)
    {
        var netId = netStation.NetId;
        var plugMapping = netStation.GetPluggables();

        return new PitStopPlugMappingData
            (
                netId,
                plugMapping
            );
    }

    public static void Serialize(NetDataWriter writer, PitStopPlugMappingData data)
    {
        writer.Put(data.NetId);

        writer.Put(data.PlugMapping.Count);
        foreach (var kvp in data.PlugMapping)
        {
            writer.Put((int)kvp.Key);
            writer.Put(kvp.Value);
        }
    }

    public static PitStopPlugMappingData Deserialize(NetDataReader reader)
    {
        var netId = reader.GetUShort();

        var dictCount = reader.GetInt();

        Dictionary<ResourceType, ushort> plugMapping = [];
        for (int i = 0; i < dictCount; i++)
        {
            plugMapping.Add((ResourceType)reader.GetInt(), reader.GetUShort());
        }

        return new PitStopPlugMappingData
            (
                netId,
                plugMapping
            );
    }
}
