using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public struct ItemPositionData
{
    public Vector3 Position;
    public Quaternion Rotation;

    public static ItemPositionData FromItem(NetworkedItem item)
    {
        //Multiplayer.Log($"ItemPositionData.FromItem() Position: {item.Item.transform.position}, Less currentMove: {item.Item.transform.position - WorldMover.currentMove } ");
        return new ItemPositionData
        {
            Position = item.Item.transform.position - WorldMover.currentMove,
            Rotation = item.Item.transform.rotation,
        };
    }

    public static void Serialize(NetDataWriter writer, ItemPositionData data)
    {
        Vector3Serializer.Serialize(writer, data.Position);
        QuaternionSerializer.Serialize(writer, data.Rotation);
    }

    public static ItemPositionData Deserialize(NetDataReader reader)
    {
        return new ItemPositionData
        {
            Position = Vector3Serializer.Deserialize(reader),
            Rotation = QuaternionSerializer.Deserialize(reader),
        };
    }
}
