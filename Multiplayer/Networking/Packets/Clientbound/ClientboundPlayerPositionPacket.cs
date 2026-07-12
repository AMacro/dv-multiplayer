using Multiplayer.Networking.Data.Player;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerPositionPacket
{
    public byte PlayerId { get; set; }
    public PlayerTrackingData TrackingData { get; set; }
    public PlayerPostureFlags Posture { get; set; }
    public bool IsOnCar { get; set; }
    public ushort CarID { get; set; }
}
