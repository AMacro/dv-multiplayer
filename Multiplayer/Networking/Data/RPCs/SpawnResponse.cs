using LiteNetLib.Utils;
using Multiplayer.Networking.Data.Train;

namespace Multiplayer.Networking.Data.RPCs;

public class SpawnResponse : IRpcResponse
{
    public enum ResponseType : byte
    {
        Success = 0,
        InsufficientPermissions = 1,
        InsufficientFunds = 2,
        InUse = 3
    }

    public ResponseType Response { get; set; }
    public LocoInUseData.LocoInUseReason Reason { get; set; }
    public float Timeout { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Response);
        writer.Put((byte)Reason);
        writer.Put(Timeout);
    }

    public void Deserialize(NetDataReader reader)
    {
        Response = (ResponseType)reader.GetByte();
        Reason = (LocoInUseData.LocoInUseReason)reader.GetByte();
        Timeout = reader.GetFloat();
    }
}
