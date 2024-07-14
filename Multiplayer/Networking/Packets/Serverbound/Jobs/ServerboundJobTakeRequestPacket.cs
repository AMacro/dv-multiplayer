using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.Train;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ServerboundJobTakeRequestPacket
{
    public ushort netId { get; set; }
}
