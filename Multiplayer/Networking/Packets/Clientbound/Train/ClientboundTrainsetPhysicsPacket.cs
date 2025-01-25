using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundTrainsetPhysicsPacket
{
    public int NetId { get; set; }
    public uint Tick { get; set; }
    public TrainsetMovementPart[] TrainsetParts { get; set; }
}
