using DV.ThingTypes;
using LiteNetLib.Utils;
using System.Linq;

namespace Multiplayer.Networking.Data;

public enum LocoResourceModuleFillingState : byte
{
    None = 0,
    Filling = 1,
    Draining = 2,
}
public readonly struct LocoResourceModuleData(ResourceType resourceType, float[] values, LocoResourceModuleFillingState fillingState)
{
    public readonly ResourceType ResourceType = resourceType;
    public readonly float[] Values = values;
    public readonly LocoResourceModuleFillingState FillingState = fillingState;

    public static LocoResourceModuleData From(LocoResourceModule resources)
    {
        //extract floats
        var values = resources.resourceData.Select(d => d.unitsToBuy).ToArray();

        LocoResourceModuleFillingState fillingState = LocoResourceModuleFillingState.None;
        if (resources.isFilling)
        {
            fillingState = LocoResourceModuleFillingState.Filling;
        }
        else if (resources.isDraining)
        {
            fillingState = LocoResourceModuleFillingState.Draining;
        }

        Multiplayer.LogDebug(() => $"LocoResourceModuleData.From({resources.resourceType}) values count: {values.Length}, values: [{string.Join(", ", values)}]");

        return new LocoResourceModuleData(resources.resourceType, values, fillingState);
    }

    public static void Serialize(NetDataWriter writer, LocoResourceModuleData data)
    {
        writer.Put((int)data.ResourceType);

        writer.Put(data.Values.Length);
        foreach (var val in data.Values)
            writer.Put(val);

        writer.Put((byte)data.FillingState);
    }

    public static LocoResourceModuleData Deserialize(NetDataReader reader)
    {
        var type = (ResourceType)reader.GetInt();

        var valueCount = reader.GetInt();

        float[] states = new float[valueCount];
        for (int i = 0; i < valueCount; i++)
            states[i] = reader.GetFloat();

        LocoResourceModuleFillingState fillingState = (LocoResourceModuleFillingState)reader.GetByte();

        return new LocoResourceModuleData(type, states, fillingState);
    }
}
