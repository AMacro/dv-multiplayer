using LiteNetLib.Utils;
using System.Collections.Generic;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Packets;

namespace Multiplayer.Networking.Packets.Clientbound;

internal sealed class ClientboundItemReconciliationPacket : INetSerializable
{
    public List<ItemReconciliationResult> Items = [];
    public bool IsFinalBatch;

    public void Serialize(NetDataWriter writer)
    {
        if (Items.Count > ItemReconciliationProtocol.MaxItems)
            throw new System.InvalidOperationException($"Too many item reconciliation results: {Items.Count}");
        writer.Put(IsFinalBatch);
        writer.Put(Items.Count);
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            writer.Put(item.LocalId);
            writer.Put(item.NetId);
            writer.Put(item.Snapshot != null);
            item.Snapshot?.Serialize(writer);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Items.Clear();
        IsFinalBatch = reader.GetBool();
        int count = reader.GetInt();
        if (count < 0 || count > ItemReconciliationProtocol.MaxItems)
            throw new System.InvalidOperationException($"Invalid item reconciliation result count: {count}");
        for (int i = 0; i < count; i++)
        {
            int localId = reader.GetInt();
            ushort netId = reader.GetUShort();
            ItemUpdateData snapshot = null;
            if (reader.GetBool())
            {
                snapshot = new ItemUpdateData();
                snapshot.Deserialize(reader);
            }
            Items.Add(new ItemReconciliationResult(localId, netId, snapshot));
        }
    }
}

internal readonly struct ItemReconciliationResult
{
    public readonly int LocalId;
    public readonly ushort NetId;
    public readonly ItemUpdateData Snapshot;

    public ItemReconciliationResult(int localId, ushort netId, ItemUpdateData snapshot)
    {
        LocalId = localId;
        NetId = netId;
        Snapshot = snapshot;
    }
}
