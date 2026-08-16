using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common;

public enum CustomizationAction : byte { PlaceGadget, RemoveGadget, AddHole, MoveHole, RemoveHole, Mount, Unmount, Wire, Unwire, DropEmptySpool, LoadSpool, SnapItem, UnsnapItem, ReplaceSpool, ReplaceDuctTape }

public sealed class CommonCustomizationPacket : INetSerializable
{
    public uint OriginActionId;
    public CustomizationAction Action;
    public ushort ItemNetId;
    public ushort OtherItemNetId;
    public int IndexA;
    public int IndexB;
    public string TargetKey;
    public Vector3 Position;
    public Vector3 PreviousPosition;
    public Vector3 Normal;
    public Quaternion Rotation;
    public bool Flag;
    public byte OwnerPlayerId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId);
        writer.Put((byte)Action);
        switch (Action)
        {
            case CustomizationAction.PlaceGadget:
                writer.Put(ItemNetId); PutTarget(writer); Vector3Serializer.Serialize(writer, Position);
                QuaternionSerializer.Serialize(writer, Rotation); break;
            case CustomizationAction.RemoveGadget:
                writer.Put(ItemNetId); writer.Put(Flag); break;
            case CustomizationAction.AddHole:
                PutTarget(writer); Vector3Serializer.Serialize(writer, Position); Vector3Serializer.Serialize(writer, Normal); break;
            case CustomizationAction.MoveHole:
                PutTarget(writer); Vector3Serializer.Serialize(writer, Position); Vector3Serializer.Serialize(writer, PreviousPosition);
                Vector3Serializer.Serialize(writer, Normal); break;
            case CustomizationAction.RemoveHole:
                PutTarget(writer); Vector3Serializer.Serialize(writer, PreviousPosition); break;
            case CustomizationAction.Mount:
            case CustomizationAction.SnapItem:
                PutRelationship(writer, includeSecondIndex: false); writer.Put(Flag);
                if (Flag) Vector3Serializer.Serialize(writer, Position); break;
            case CustomizationAction.Unmount:
                writer.Put(ItemNetId); writer.Put(IndexA); break;
            case CustomizationAction.Wire:
            case CustomizationAction.Unwire:
                PutRelationship(writer, includeSecondIndex: true); break;
            case CustomizationAction.UnsnapItem:
                PutRelationship(writer, includeSecondIndex: false); break;
            case CustomizationAction.DropEmptySpool:
                writer.Put(ItemNetId); writer.Put(OtherItemNetId); writer.Put(Flag);
                if (Flag) { Vector3Serializer.Serialize(writer, Position); QuaternionSerializer.Serialize(writer, Rotation); }
                break;
            case CustomizationAction.ReplaceDuctTape:
                writer.Put(ItemNetId); writer.Put(OtherItemNetId); writer.Put(OwnerPlayerId); writer.Put(Flag);
                if (Flag) { Vector3Serializer.Serialize(writer, Position); QuaternionSerializer.Serialize(writer, Rotation); }
                break;
            case CustomizationAction.LoadSpool:
                writer.Put(ItemNetId); writer.Put(OtherItemNetId); break;
            case CustomizationAction.ReplaceSpool:
                writer.Put(ItemNetId); writer.Put(OtherItemNetId); writer.Put(OwnerPlayerId); break;
            default:
                throw new System.InvalidOperationException($"Unsupported customization action: {Action}");
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt();
        Action = (CustomizationAction)reader.GetByte();
        switch (Action)
        {
            case CustomizationAction.PlaceGadget:
                ItemNetId = reader.GetUShort(); GetTarget(reader); Position = Vector3Serializer.Deserialize(reader);
                Rotation = QuaternionSerializer.Deserialize(reader); break;
            case CustomizationAction.RemoveGadget:
                ItemNetId = reader.GetUShort(); Flag = reader.GetBool(); break;
            case CustomizationAction.AddHole:
                GetTarget(reader); Position = Vector3Serializer.Deserialize(reader); Normal = Vector3Serializer.Deserialize(reader); break;
            case CustomizationAction.MoveHole:
                GetTarget(reader); Position = Vector3Serializer.Deserialize(reader); PreviousPosition = Vector3Serializer.Deserialize(reader);
                Normal = Vector3Serializer.Deserialize(reader); break;
            case CustomizationAction.RemoveHole:
                GetTarget(reader); PreviousPosition = Vector3Serializer.Deserialize(reader); break;
            case CustomizationAction.Mount:
            case CustomizationAction.SnapItem:
                GetRelationship(reader, includeSecondIndex: false); Flag = reader.GetBool();
                if (Flag) Position = Vector3Serializer.Deserialize(reader); break;
            case CustomizationAction.Unmount:
                ItemNetId = reader.GetUShort(); IndexA = reader.GetInt(); break;
            case CustomizationAction.Wire:
            case CustomizationAction.Unwire:
                GetRelationship(reader, includeSecondIndex: true); break;
            case CustomizationAction.UnsnapItem:
                GetRelationship(reader, includeSecondIndex: false); break;
            case CustomizationAction.DropEmptySpool:
                ItemNetId = reader.GetUShort(); OtherItemNetId = reader.GetUShort(); Flag = reader.GetBool();
                if (Flag) { Position = Vector3Serializer.Deserialize(reader); Rotation = QuaternionSerializer.Deserialize(reader); }
                break;
            case CustomizationAction.ReplaceDuctTape:
                ItemNetId = reader.GetUShort(); OtherItemNetId = reader.GetUShort(); OwnerPlayerId = reader.GetByte(); Flag = reader.GetBool();
                if (Flag) { Position = Vector3Serializer.Deserialize(reader); Rotation = QuaternionSerializer.Deserialize(reader); }
                break;
            case CustomizationAction.LoadSpool:
                ItemNetId = reader.GetUShort(); OtherItemNetId = reader.GetUShort(); break;
            case CustomizationAction.ReplaceSpool:
                ItemNetId = reader.GetUShort(); OtherItemNetId = reader.GetUShort(); OwnerPlayerId = reader.GetByte(); break;
            default:
                throw new System.InvalidOperationException($"Unsupported customization action: {Action}");
        }
    }

    private void PutTarget(NetDataWriter writer) => writer.Put(TargetKey ?? string.Empty);
    private void GetTarget(NetDataReader reader) => TargetKey = reader.GetString();

    private void PutRelationship(NetDataWriter writer, bool includeSecondIndex)
    {
        writer.Put(ItemNetId); writer.Put(OtherItemNetId); writer.Put(IndexA);
        if (includeSecondIndex) writer.Put(IndexB);
    }

    private void GetRelationship(NetDataReader reader, bool includeSecondIndex)
    {
        ItemNetId = reader.GetUShort(); OtherItemNetId = reader.GetUShort(); IndexA = reader.GetInt();
        if (includeSecondIndex) IndexB = reader.GetInt();
    }
}
