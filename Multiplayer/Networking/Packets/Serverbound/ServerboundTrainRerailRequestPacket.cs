using UnityEngine;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundTrainRerailRequestPacket
{
    public ushort NetId { get; set; }
    public ushort TrackId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; set; }
}
