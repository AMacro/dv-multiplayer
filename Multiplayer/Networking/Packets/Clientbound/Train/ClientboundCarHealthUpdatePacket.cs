using Multiplayer.Networking.Data.Train;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundCarHealthUpdatePacket
{
    public ushort NetId { get; set; }
    public TrainCarHealthData Health { get; set; }
}
