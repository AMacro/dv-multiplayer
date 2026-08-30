using LiteNetLib.Utils;
using Multiplayer.Networking.Data.Items;
using System.Collections.Generic;
using Multiplayer.Networking.Packets;

namespace Multiplayer.Networking.Packets.Serverbound;

internal sealed class ServerboundItemReconciliationPacket : INetSerializable
{
    public List<ItemReconciliationRequest> Items = [];
    public bool IsFinalBatch;

    public void Serialize(NetDataWriter writer)
    {
        if (Items.Count > ItemReconciliationProtocol.MaxItems)
            throw new System.InvalidOperationException($"Too many item reconciliation requests: {Items.Count}");
        writer.Put(IsFinalBatch);
        writer.Put(Items.Count);
        foreach (var item in Items)
        {
            writer.Put(item.LocalId);
            writer.Put(item.BelongsToPlayer);
            item.Snapshot.Serialize(writer);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Items.Clear();
        IsFinalBatch = reader.GetBool();
        int count = reader.GetInt();
        if (count < 0 || count > ItemReconciliationProtocol.MaxItems)
            throw new System.InvalidOperationException($"Invalid item reconciliation count: {count}");
        for (int i = 0; i < count; i++)
        {
            var snapshot = new ItemUpdateData();
            int localId = reader.GetInt();
            bool belongsToPlayer = reader.GetBool();
            snapshot.Deserialize(reader);
            Items.Add(new ItemReconciliationRequest(localId, belongsToPlayer, snapshot));
        }
    }
}

internal readonly struct ItemReconciliationRequest
{
    public readonly int LocalId;
    public readonly bool BelongsToPlayer;
    public readonly ItemUpdateData Snapshot;

    public ItemReconciliationRequest(int localId, bool belongsToPlayer, ItemUpdateData snapshot)
    {
        LocalId = localId;
        BelongsToPlayer = belongsToPlayer;
        Snapshot = snapshot;
    }
}
