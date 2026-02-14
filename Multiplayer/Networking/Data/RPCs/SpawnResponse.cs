using LiteNetLib.Utils;

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

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Response);
    }

    public void Deserialize(NetDataReader reader)
    {
        Response = (ResponseType)reader.GetByte();
    }
}
