using System;
using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Clientbound;

internal sealed class ClientboundCustomizationAcknowledgmentPacket : INetSerializable
{
    internal const int BatchSize = 256;
    public uint[] ActionIds = [];

    public void Serialize(NetDataWriter writer)
    {
        if (ActionIds.Length > BatchSize)
            throw new InvalidOperationException("Customization acknowledgment batch is too large");
        writer.Put((ushort)ActionIds.Length);
        foreach (uint id in ActionIds)
            writer.Put(id);
    }

    public void Deserialize(NetDataReader reader)
    {
        int count = reader.GetUShort();
        if (count > BatchSize)
            throw new InvalidOperationException("Customization acknowledgment batch is too large");
        ActionIds = new uint[count];
        for (int index = 0; index < count; index++)
            ActionIds[index] = reader.GetUInt();
    }
}
