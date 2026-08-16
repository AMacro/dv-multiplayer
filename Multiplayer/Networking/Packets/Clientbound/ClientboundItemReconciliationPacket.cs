using LiteNetLib.Utils;
using System.Collections.Generic;

namespace Multiplayer.Networking.Packets.Clientbound;

internal sealed class ClientboundItemReconciliationPacket : INetSerializable
{
    private const int MaxItems = 128;
    public List<ItemReconciliationResult> Items = [];

    public void Serialize(NetDataWriter writer)
    {
        int count = System.Math.Min(Items.Count, MaxItems);
        writer.Put(count);
        for (int i = 0; i < count; i++)
        {
            var item = Items[i];
            writer.Put(item.LocalId);
            writer.Put(item.NetId);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Items.Clear();
        int count = reader.GetInt();
        if (count < 0 || count > MaxItems)
            throw new System.InvalidOperationException($"Invalid item reconciliation result count: {count}");
        for (int i = 0; i < count; i++)
            Items.Add(new ItemReconciliationResult(reader.GetInt(), reader.GetUShort()));
    }
}

internal readonly struct ItemReconciliationResult
{
    public readonly int LocalId;
    public readonly ushort NetId;

    public ItemReconciliationResult(int localId, ushort netId)
    {
        LocalId = localId;
        NetId = netId;
    }
}
