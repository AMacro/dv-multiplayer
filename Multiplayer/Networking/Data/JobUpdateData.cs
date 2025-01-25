using DV.ThingTypes;
using LiteNetLib.Utils;
namespace Multiplayer.Networking.Data;

public struct JobUpdateStruct : INetSerializable
{
    public ushort JobNetID;
    public bool Invalid;
    public JobState JobState;
    public float StartTime;
    public float FinishTime;
    public ushort ItemNetID;
    public ushort ValidationStationId;
    public ItemPositionData ItemPositionData;

    public readonly void Serialize(NetDataWriter writer)
    {
        writer.Put(JobNetID);
        writer.Put(Invalid);

        //Invalid jobs will be deleted / deregistered
        if (Invalid)
            return;

        writer.Put((byte)JobState);
        writer.Put(StartTime);
        writer.Put(FinishTime);
        writer.Put(ItemNetID);
        writer.Put(ValidationStationId);
        ItemPositionData.Serialize(writer,ItemPositionData);
    }

    public void Deserialize(NetDataReader reader)
    {
        JobNetID = reader.GetUShort();
        Invalid = reader.GetBool();

        if (Invalid)
            return;

        JobState = (JobState)reader.GetByte();
        StartTime = reader.GetFloat();
        FinishTime = reader.GetFloat();
        ItemNetID = reader.GetUShort();
        ValidationStationId = reader.GetUShort();
        ItemPositionData = ItemPositionData.Deserialize(reader);
    }
}
