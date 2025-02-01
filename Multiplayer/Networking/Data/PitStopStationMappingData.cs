using DV.ThingTypes;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public readonly struct PitStopStationMappingData(ushort netId, Vector3 location, int selectedCar, Dictionary<ResourceType, ushort> plugMapping)
{
    public readonly ushort NetId = netId;
    public readonly Vector3 Location = location;
    public readonly int SelectedCar = selectedCar;
    public readonly Dictionary<ResourceType, ushort> PlugMapping = plugMapping;


    public static PitStopStationMappingData From(NetworkedPitStopStation netStation)
    {
        var netId = netStation.NetId;
        var location = netStation.transform.position - WorldMover.currentMove;
        var selectedCar = netStation.Station?.pitstop?.SelectedIndex ?? 0;
        var plugMapping = netStation.GetPluggables();

        return new PitStopStationMappingData
            (
                netId,
                location,
                selectedCar,
                plugMapping
            );
    }

    public static void Serialize(NetDataWriter writer, PitStopStationMappingData data)
    {
        writer.Put(data.NetId);
        Vector3Serializer.Serialize(writer, data.Location);
        writer.Put(data.SelectedCar);

        writer.Put(data.PlugMapping.Count);
        foreach (var kvp in data.PlugMapping)
        {
            writer.Put((int)kvp.Key);
            writer.Put(kvp.Value);
        }
    }

    public static PitStopStationMappingData Deserialize(NetDataReader reader)
    {
        var netId = reader.GetUShort();
        var location = Vector3Serializer.Deserialize(reader);
        var selectedCar = reader.GetInt();

        var dictCount = reader.GetInt();

        Dictionary<ResourceType, ushort> plugMapping = [];
        for (int i = 0; i < dictCount; i++)
        {
            plugMapping.Add((ResourceType)reader.GetInt(), reader.GetUShort());
        }

        return new PitStopStationMappingData
            (
                netId,
                location,
                selectedCar,
                plugMapping
            );
    }
}
