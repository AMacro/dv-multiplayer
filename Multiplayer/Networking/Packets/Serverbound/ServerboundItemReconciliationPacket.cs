using LiteNetLib.Utils;
using Multiplayer.Networking.Data.Items;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Networking.Packets.Serverbound;

internal sealed class ServerboundItemReconciliationPacket : INetSerializable
{
    private const int MaxItems = 128;
    public List<ItemReconciliationRequest> Items = [];

    public void Serialize(NetDataWriter writer)
    {
        int count = System.Math.Min(Items.Count, MaxItems);
        writer.Put(count);
        foreach (var item in Items.Take(count))
        {
            writer.Put(item.LocalId);
            writer.Put(item.BelongsToPlayer);
            item.Snapshot.Serialize(writer);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Items.Clear();
        int count = reader.GetInt();
        if (count < 0 || count > MaxItems)
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
