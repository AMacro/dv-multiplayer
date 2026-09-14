using LiteNetLib.Utils;
using System;
using System.Collections.Generic;

namespace Multiplayer.Networking.Serialization;

internal static class TrackedValueSerializer
{
    public static void SerializeDictionary(NetDataWriter writer, Dictionary<string, object> values)
    {
        writer.Put(values?.Count ?? 0);
        if (values == null)
            return;

        foreach (var entry in values)
        {
            writer.Put(entry.Key);
            Serialize(writer, entry.Value);
        }
    }

    public static Dictionary<string, object> DeserializeDictionary(NetDataReader reader)
    {
        int count = reader.GetInt();
        if (count < 0 || count > 4096)
            throw new InvalidOperationException($"Invalid tracked value count: {count}");

        var values = new Dictionary<string, object>(count);
        for (int i = 0; i < count; i++)
            values[reader.GetString()] = Deserialize(reader);
        return values;
    }

    public static void Serialize(NetDataWriter writer, object value)
    {
        switch (value)
        {
            case bool boolValue: writer.Put((byte)0); writer.Put(boolValue); break;
            case int intValue: writer.Put((byte)1); writer.Put(intValue); break;
            case uint uintValue: writer.Put((byte)2); writer.Put(uintValue); break;
            case float floatValue: writer.Put((byte)3); writer.Put(floatValue); break;
            case string stringValue: writer.Put((byte)4); writer.Put(stringValue); break;
            default: throw new NotSupportedException($"Unsupported tracked value type: {value?.GetType()}");
        }
    }

    public static object Deserialize(NetDataReader reader) => reader.GetByte() switch
    {
        0 => reader.GetBool(),
        1 => reader.GetInt(),
        2 => reader.GetUInt(),
        3 => reader.GetFloat(),
        4 => reader.GetString(),
        byte typeCode => throw new NotSupportedException($"Unsupported tracked value type code: {typeCode}"),
    };
}
