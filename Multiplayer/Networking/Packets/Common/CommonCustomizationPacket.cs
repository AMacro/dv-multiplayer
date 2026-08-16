using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common;

public enum CustomizationAction : byte { PlaceGadget, RemoveGadget, AddHole, MoveHole, RemoveHole, Mount, Unmount, Wire, Unwire, DropEmptySpool, LoadSpool, SnapItem, UnsnapItem, ReplaceSpool, ReplaceDuctTape }

public sealed class CommonCustomizationPacket : INetSerializable
{
    public CustomizationAction Action;
    public ushort ItemNetId;
    public ushort OtherItemNetId;
    public int IndexA;
    public int IndexB;
    public byte TargetKind;
    public ushort TargetTrainCarNetId;
    public Vector3 Position;
    public Vector3 PreviousPosition;
    public Vector3 Normal;
    public Quaternion Rotation;
    public bool Flag;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Action); writer.Put(ItemNetId); writer.Put(OtherItemNetId); writer.Put(IndexA); writer.Put(IndexB); writer.Put(TargetKind); writer.Put(TargetTrainCarNetId);
        Vector3Serializer.Serialize(writer, Position); Vector3Serializer.Serialize(writer, PreviousPosition);
        Vector3Serializer.Serialize(writer, Normal); QuaternionSerializer.Serialize(writer, Rotation); writer.Put(Flag);
    }

    public void Deserialize(NetDataReader reader)
    {
        Action = (CustomizationAction)reader.GetByte(); ItemNetId = reader.GetUShort(); OtherItemNetId = reader.GetUShort(); IndexA = reader.GetInt(); IndexB = reader.GetInt(); TargetKind = reader.GetByte();
        TargetTrainCarNetId = reader.GetUShort(); Position = Vector3Serializer.Deserialize(reader);
        PreviousPosition = Vector3Serializer.Deserialize(reader); Normal = Vector3Serializer.Deserialize(reader);
        Rotation = QuaternionSerializer.Deserialize(reader); Flag = reader.GetBool();
    }
}
