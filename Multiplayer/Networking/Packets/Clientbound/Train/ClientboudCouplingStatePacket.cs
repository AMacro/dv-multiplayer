using Multiplayer.Networking.Data.Train;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboudCouplingStatePacket
{
    public ushort NetId { get; set; }
    public CouplingData FrontCouplingData { get; set; }
    public CouplingData RearCouplingData { get; set; }
    public bool FronMuConnected { get; set; }
    public bool RearMuConnected { get; set; }
}
