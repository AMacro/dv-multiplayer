using Multiplayer.Networking.Data.Player;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerJoinedPacket
{
    public byte PlayerId { get; set; }
    public string Username { get; set; }
    public bool IsVR { get; set; }
    public string CharacterId { get; set; }
    public string CrewName { get; set; } = string.Empty;
    public PlayerTrackingData TrackingData { get; set; }
    public PlayerPostureFlags Posture { get; set; }
    public bool IsOnCar { get; set; }
    public ushort CarID { get; set; }
}
