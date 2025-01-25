namespace Multiplayer.Networking.Packets.Common.Train;

public class CommonTrainCouplePacket
{
    public ushort NetId { get; set; }
    public bool IsFrontCoupler { get; set; }
    public byte State { get; set; }
    public ushort OtherNetId { get; set; }
    public byte OtherState { get; set; }
    public bool OtherCarIsFrontCoupler { get; set; }
    public bool PlayAudio { get; set; }
    public bool ViaChainInteraction { get; set; }
}
