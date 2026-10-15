namespace Multiplayer.Networking.Packets.Clientbound.Train;

/// <summary>
/// The server's authoritative commit for a change to one or more trainsets.
/// A client must install this epoch before accepting state snapshots for the
/// affected cars.
/// </summary>
public class ClientboundTrainsetTopologyCommitPacket
{
    public uint Epoch { get; set; }
    public ushort[] NetIds { get; set; }
}
