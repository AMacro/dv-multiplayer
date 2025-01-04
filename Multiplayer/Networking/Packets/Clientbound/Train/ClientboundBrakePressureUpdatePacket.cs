namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundBrakePressureUpdatePacket
{
    public ushort NetId { get; set; }
    public float MainReservoirPressure { get; set; }
    public float BrakePipePressure { get; set; }
    public float BrakeCylinderPressure { get; set; }
}
