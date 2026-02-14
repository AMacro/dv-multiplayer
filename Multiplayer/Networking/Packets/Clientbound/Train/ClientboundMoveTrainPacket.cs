using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundMoveTrainPacket
{
    public ushort NetId { get; set; }
    public ushort TrackId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; set; }
    public bool IsTeleporting { get; set; }
}
