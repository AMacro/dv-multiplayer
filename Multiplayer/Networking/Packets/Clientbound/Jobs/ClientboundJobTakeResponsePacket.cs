using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.Train;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ClientboundJobTakeResponsePacket
{
    public ushort netId { get; set; }
    public bool granted { get; set; }
    public byte playerId { get; set; }
}
