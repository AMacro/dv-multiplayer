using System;

namespace Multiplayer.Networking.Packets.Common.Train;


public class CommonCouplerInteractionPacket
{
    public ushort NetId { get; set; }
    public ushort OtherNetId { get; set; }
    public bool IsFrontCoupler { get; set; }
    public bool IsFrontOtherCoupler { get; set; }
    public ushort Flags { get; set; }
    // The topology versions the client observed before starting this action.
    // The server rejects an action against a topology it has already replaced.
    public uint TopologyEpoch { get; set; }
    public uint OtherTopologyEpoch { get; set; }
}
