using DV.ThingTypes;
using LiteNetLib.Utils;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Networking.Data;

public readonly struct PitStopStationData(Dictionary<ResourceType, float> resourceStates)
{
    public readonly Dictionary<ResourceType, float> ResourceState = resourceStates;

    public static PitStopStationData From(CarPitStopParametersBase pitStopParams)
    {
        //extract floats
        var states = pitStopParams.GetCarPitStopParameters().ToDictionary(param =>  param.Key, param => param.Value.value);

        return new PitStopStationData(states);
    }

    public static void Serialize(NetDataWriter writer, PitStopStationData data)
    {
        writer.Put(data.ResourceState.Count);
        foreach (var kvp in data.ResourceState)
        {
            writer.Put((int)kvp.Key);
            writer.Put(kvp.Value);
        }
    }

    public static PitStopStationData Deserialize(NetDataReader reader)
    {
        var statesCount = reader.GetInt();

        Dictionary<ResourceType, float> states = [];
        for (int i = 0; i < statesCount; i++)
            states.Add((ResourceType)reader.GetInt(), reader.GetFloat());

        return new PitStopStationData(states);
    }
}
