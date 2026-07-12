using Multiplayer.Networking.Data.Player;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundPlayerPositionPacket
{
    public PlayerTrackingData TrackingData { get; set; }
    public PlayerPostureFlags Posture { get; set; }
    public bool IsOnCar { get; set; }
    public ushort CarID { get; set; }
}
