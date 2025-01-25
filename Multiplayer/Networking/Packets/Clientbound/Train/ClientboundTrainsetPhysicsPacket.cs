using Multiplayer.Networking.Data.Train;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundTrainsetPhysicsPacket
{
    public int FirstNetId { get; set; }
    public int LastNetId { get; set; } 
    public uint Tick { get; set; }
    public TrainsetMovementPart[] TrainsetParts { get; set; }
}
