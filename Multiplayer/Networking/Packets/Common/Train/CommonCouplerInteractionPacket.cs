using System;

namespace Multiplayer.Networking.Packets.Common.Train;


public class CommonCouplerInteractionPacket
{
    public ushort NetId { get; set; }
    public ushort OtherNetId { get; set; }
    public bool IsFrontCoupler { get; set; }
    public bool IsFrontOtherCoupler { get; set; }
    public ushort Flags { get; set; }
}
