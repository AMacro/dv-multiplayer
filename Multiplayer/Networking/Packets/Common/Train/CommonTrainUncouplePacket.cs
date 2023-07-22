namespace Multiplayer.Networking.Packets.Common.Train;

public class CommonTrainUncouplePacket
{
    public string CarGUID { get; set; }
    public bool IsFrontCoupler { get; set; }
    public bool PlayAudio { get; set; }
    public bool ViaChainInteraction { get; set; }
    public bool DueToBrokenCouple { get; set; }
}
